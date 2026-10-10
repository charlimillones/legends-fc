using System;
using System.IO;
using System.Linq;
using LegendsFC.Core.Career;
using LegendsFC.Core.Model;
using LegendsFC.Core.Saves;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;
using Xunit.Abstractions;

/// <summary>Board, jobs, season summary, awards, random events and the protégé (rules decided Oct 7-9; numbers PROPOSAL).</summary>
public class CareerTests : IDisposable
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfc-career-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;
    public CareerTests(ITestOutputHelper o) { _out = o; }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private GameSession New(ulong seed, string league, int rank = 0)
    {
        var s = GameSession.NewWorld(D, new SaveStore(Path.Combine(_root, seed + league + rank), D.Saves), "x", seed, "EUR", "t");
        s.Autosave = false;
        s.PickClub(s.World.Clubs.Where(c => s.World.ClubLeague[c.Id] == league).OrderByDescending(c => c.Reputation).ElementAt(rank).Id, "t");
        return s;
    }

    [Fact]
    public void TheBoard_OffersThreeObjectives_TheAmbitiousOneRaisesTheWageBar()
    {
        var s = New(41, "ESP-1", 6);
        var o = s.ObjectiveOptions();
        Assert.True(o["safe"] > o["standard"] && o["standard"] > o["ambitious"]);
        Assert.Equal("standard", s.Career.ObjectiveKind);
        double standard = LegendsFC.Core.Transfers.Market.WageRoom(s.World, s.UserClub, D);
        Assert.True(s.ChooseObjective("ambitious"));
        Assert.True(LegendsFC.Core.Transfers.Market.WageRoom(s.World, s.UserClub, D) > standard);
        Assert.Equal(o["ambitious"], s.Career.ObjectiveTarget);
        for (int i = 0; i < 9; i++) s.AdvanceWeek("t");
        Assert.False(s.ChooseObjective("safe"));                                   // the league has started
        double chance = s.WageAskChance(0.1);
        Assert.InRange(chance, 0.05, 0.95);
        s.AskForWages(0.1);
        Assert.False(s.AskForWages(0.1));                                          // once a season
    }

    [Fact]
    public void ASeason_SummaryAwardsObjectiveAndReputation()
    {
        var s = New(43, "ENG-1");
        double rep = s.Career.Reputation;
        for (int i = 0; i < 52; i++) s.AdvanceWeek("t");
        var sum = s.Career.Seasons.Single();
        _out.WriteLine($"{sum.Position}/{sum.Teams} W{sum.Won} D{sum.Drawn} L{sum.Lost}, cups {string.Join(", ", sum.Cups.Select(c => c.CompetitionId + " " + c.Stage + (c.Won ? " WON" : "")))}, top scorer {sum.TopScorerGoals}, best {sum.BiggestWin}, worst {sum.BiggestDefeat}, objective {sum.ObjectiveKind} {sum.ObjectiveTarget} met {sum.ObjectiveMet}; reputation {rep:F0} -> {s.Career.Reputation:F0}, confidence {s.Career.Confidence:F0}");
        Assert.Equal(38, sum.Won + sum.Drawn + sum.Lost);
        Assert.Contains(sum.Cups, c => c.CompetitionId == "ENG-FA");
        Assert.True(sum.TopScorerGoals > 0);
        Assert.NotEqual(rep, s.Career.Reputation);
        var awards = s.AwardsOf(2026);
        Assert.Equal(8 * 5 + 2, awards.Count);                                       // 5 per league + 2 world awards
        Assert.Contains(awards, a => a.Award == "Golden Ball" && a.Scope == "WORLD");
        Assert.All(awards.Where(a => a.Award == "Young Player of the Season"), a => Assert.True(2026 - s.World.Players.First(p => p.Id == a.PlayerId).BirthYear <= 21));
        Assert.Contains(s.World.Inbox.Messages, m => m.From == "The board");
    }

    [Fact]
    public void SackedAfterABadRun_OffersFromLowerClubs_NeverGameOver()
    {
        var s = New(45, "BRA-1");
        for (int i = 0; i < 20; i++) s.AdvanceWeek("t");
        var old = s.UserClub;
        s.Career.Confidence = 5;
        s.AdvanceWeek("t");
        Assert.True(s.Career.Unemployed);
        Assert.Null(s.World.UserClubId);
        Assert.InRange(s.Career.Offers.Count, 2, 3);
        Assert.All(s.Career.Offers, o => Assert.True(s.World.Clubs.First(c => c.Id == o.ClubId).Reputation < old.Reputation));
        s.AdvanceWeek("t");                                                          // the world plays on without you
        var offer = s.Career.Offers[0].ClubId;
        Assert.True(s.AcceptJob(offer));
        Assert.Equal(offer, s.World.UserClubId);
        Assert.False(s.Career.Unemployed);
        Assert.True(s.Career.Tenures.First().Sacked);
    }

    [Fact]
    public void RandomEvents_RareBalancedAndForEveryone_DecisionsDefaultAfterAWeek()
    {
        var w = new WorldGenerator(D).Generate(47);
        var cycle = new SeasonCycle(D); var rng = new GameRandom(48);
        w.UserClubId = w.Clubs.First(c => w.ClubLeague[c.Id] == "ENG-2").Id;
        Board.Start(w, w.Clubs.First(c => c.Id == w.UserClubId), D);
        cycle.Advance(w, rng); cycle.Advance(w, rng);
        var all = w.ClubEvents.Values.SelectMany(x => x).ToList();
        double perSeason = all.Count / (2.0 * w.Clubs.Count);
        _out.WriteLine($"events per club per season {perSeason:F2}; good {all.Count(e => e.Kind == "good")}, bad {all.Count(e => e.Kind == "bad")}, choice {all.Count(e => e.Kind == "choice")}, disasters {all.Count(e => e.Kind == "disaster")}");
        Assert.InRange(perSeason, 1.5, 4.0);
        Assert.InRange(all.Count(e => e.Kind == "good") / (double)all.Count(e => e.Kind == "bad"), 0.6, 1.6);
        Assert.All(w.ClubEvents.Values, log => Assert.All(log.Where(e => e.Kind != "disaster").GroupBy(e => e.Season), g => Assert.True(g.Count() <= D.Events.MaxPerSeason)));   // disasters are separate and very rare
        // No event repeats for a club within 3 seasons.
        Assert.All(w.ClubEvents.Values, log => Assert.Equal(log.Count, log.Select(e => e.Id).Distinct().Count()));
        Assert.Empty(w.Career.Decisions.Where(x => x.Season < w.SeasonStartYear));   // old decisions were answered by default
    }

    [Fact]
    public void Events_DoWhatTheySay()
    {
        var w = new WorldGenerator(D).Generate(49);
        var club = w.Clubs.First(c => w.ClubLeague[c.Id] == "ARG-1");
        var rng = new GameRandom(1);
        EventDef E(string id) => D.Events.Events.First(e => e.Id == id);
        double cond = club.Facilities[Facility.Stadium].Condition;
        Assert.True(Events.Fire(w, club, E("EVT-STORM"), rng, D));
        Assert.True(club.Facilities[Facility.Stadium].Condition <= cond - 15);
        long money = club.Balance;
        Assert.True(Events.Fire(w, club, E("EVT-DONATION"), rng, D));
        Assert.True(club.Balance > money);
        var squad = LegendsFC.Core.Transfers.Market.Squad(w, club.Id);
        Assert.True(Events.Fire(w, club, E("EVT-FLU"), rng, D));
        Assert.Contains(squad, p => p.Energy <= 70);
        Assert.True(Events.Fire(w, club, E("EVT-ACCIDENT"), rng, D));
        Assert.Contains(squad, p => p.Injury?.TypeId == "EVT-ACCIDENT");
        int levels = club.Facilities.Values.Sum(f => f.Level);
        Assert.True(Events.Fire(w, club, E("EVT-INVESTOR"), rng, D));
        Assert.Equal(levels + 1, club.Facilities.Values.Sum(f => f.Level));
        int before = LegendsFC.Core.Transfers.Market.Squad(w, club.Id).Count;
        if (before < 32) { Assert.True(Events.Fire(w, club, E("EVT-YOUTH-CUP"), rng, D)); Assert.Equal(before + 1, LegendsFC.Core.Transfers.Market.Squad(w, club.Id).Count); }
    }

    [Fact]
    public void TheUserDecides_AndTheChoiceHappens()
    {
        var s = New(51, "ENG-1", 10);
        var club = s.UserClub;
        s.UserClub.FanMood = 50;
        Assert.True(Events.Fire(s.World, club, D.Events.Events.First(e => e.Id == "EVT-NAMING"), new GameRandom(2), D));
        var dec = s.Career.Decisions.Single();
        var msg = s.World.Inbox.Messages.Last();
        Assert.Equal(dec.Id, msg.DecisionId);
        Assert.Equal(new[] { "Accept", "Decline" }, msg.Options);
        long money = club.Balance;
        Assert.True(s.Decide(dec.Id, 0));
        Assert.True(club.Balance > money);
        Assert.Equal(42, club.FanMood);
        Assert.Empty(s.Career.Decisions);
    }

    [Fact]
    public void TheProtege_FirstFreeAndCustom_ThenOneASeasonForAPrice()
    {
        var s = New(53, "ARG-1");
        s.UserClub.Facilities[Facility.Academy] = new FacilityState { Level = 5, Condition = 100 };
        var first = s.CreateFirstProtege("Diego Legend", "ARG", Position.AM, Foot.Left, "PER-LEADER");
        Assert.Equal(("Diego Legend", "ARG", Position.AM, Foot.Left, "PER-LEADER"), (first.Name, first.NationalityId, first.MainPosition, first.Foot, first.PersonalityId));
        Assert.Equal((int)Math.Round(D.WorldGen.Potential.AcademyBase + D.WorldGen.Potential.AcademyPerLevel * 5 + 5), first.Potential);
        Assert.Throws<InvalidOperationException>(() => s.CreateFirstProtege("Again", "ARG", Position.ST, null, null));
        s.UserClub.Balance = 1_000_000_000;
        long before = s.UserClub.Balance;
        var (p, price) = s.BuyProtege(Position.ST, null);
        Assert.NotNull(p);
        Assert.Equal(before - price, s.UserClub.Balance);
        Assert.Throws<InvalidOperationException>(() => s.BuyProtege(null, "PER-LEADER"));
        s.AdvanceWeek("t");
        Assert.Contains(s.World.Inbox.Messages, m => m.TriggerId == "MSG-PROTEGE");
    }
}
