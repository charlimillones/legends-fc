using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Career;
using LegendsFC.Core.Inbox;
using LegendsFC.Core.Model;
using Squad = LegendsFC.Core.Squad;
using LegendsFC.Core.Money;
using LegendsFC.Core.Saves;
using LegendsFC.Core.Season;
using LegendsFC.Core.Transfers;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Xunit;

/// <summary>Long careers (Oct 10 night, 20-season test run): small saves, trimmed history, spare money, fairer objectives.</summary>
public class LongCareerTests
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;

    [Fact]
    public void StatLines_SaveAsShortArrays_AndOldObjectsStillLoad()
    {
        var p = new ArchivedPlayer { Id = "P1" };
        p.Stats.Add(new StatLine { Season = 2026, CompetitionId = "ENG-1", ClubId = "C1", Apps = 30, Starts = 28, SubApps = 2, Minutes = 2500, Goals = 12, Assists = 5, CleanSheets = 0, Yellows = 3, Reds = 1, Rated = 29, PlayerOfTheMatch = 4, RatingSum = 145.79999999999998 });
        p.Stats.Add(new StatLine { Season = 2025, ClubId = "C2" });
        string json = JsonConvert.SerializeObject(p, SaveStore.Json);
        Assert.Contains("[2026,\"ENG-1\",\"C1\",30,28,2,2500,12,5,0,3,1,29,4,145.79999999999998]", json);
        var back = JsonConvert.DeserializeObject<ArchivedPlayer>(json, SaveStore.Json);
        Assert.Equal(JsonConvert.SerializeObject(p.Stats), JsonConvert.SerializeObject(back.Stats));
        Assert.Null(back.Stats[1].CompetitionId);

        var old = JsonConvert.DeserializeObject<List<StatLine>>("[{\"Season\":2024,\"CompetitionId\":null,\"ClubId\":\"C9\",\"Apps\":7,\"Goals\":2,\"Rated\":7,\"RatingSum\":46.2,\"AverageRating\":6.6}]", SaveStore.Json);
        Assert.Equal(2024, old[0].Season); Assert.Equal(7, old[0].Apps); Assert.Equal(2, old[0].Goals); Assert.Equal(6.6, old[0].AverageRating, 6);
    }

    [Fact]
    public void SeasonEnd_KeepsFiveSeasonsOfTransfers()
    {
        var w = new WorldGenerator(D).Generate(5);
        int year = w.SeasonStartYear;
        for (int s = 0; s < 8; s++) w.Market.History.Add(new TransferRecord { Season = year - s, PlayerId = "P" + s });
        new SeasonCycle(D).TrimHistory(w);
        Assert.Equal(D.Saves.KeepTransferSeasons, w.Market.History.Count);
        Assert.All(w.Market.History, h => Assert.True(year - h.Season < D.Saves.KeepTransferSeasons));
    }

    [Fact]
    public void TheInbox_KeepsTheNewest100_ButNeverAnOpenDecision()
    {
        var w = new WorldGenerator(D).Generate(5);
        w.UserClubId = w.Clubs[0].Id;
        w.Career.Decisions.Add(new Decision { Id = 7 });
        w.Inbox.Messages.Add(new InboxMessage { Id = 1, Kind = MessageKind.Decision, DecisionId = 7 });   // the oldest, still open
        w.Inbox.Messages.Add(new InboxMessage { Id = 2, Kind = MessageKind.Decision, DecisionId = 6 });   // answered
        for (int i = 3; i <= 250; i++) w.Inbox.Messages.Add(new InboxMessage { Id = i });
        InboxEngine.Trim(w, D);
        Assert.Equal(D.InboxRules.MaxMessages, w.Inbox.Messages.Count);
        Assert.Contains(w.Inbox.Messages, m => m.Id == 1);
        Assert.Contains(w.Inbox.Messages, m => m.Id == 250);
        Assert.DoesNotContain(w.Inbox.Messages, m => m.Id == 2);
        Assert.Equal(250 - D.InboxRules.MaxMessages + 2, w.Inbox.Messages.Where(m => m.Id != 1).Min(m => m.Id));
    }

    [Fact]
    public void SpareMoney_RaisesTheWageBar_OnlyAboveOneSeasonOfIncome()
    {
        var f = D.Finance;
        var club = new Club { Id = "C", Reputation = 70, Balance = 300_000_000, LastSeasonIncome = 100_000_000 };
        double withSpare = Finance.WageBar(club, "ENG-1", "standard", f);
        club.Balance = 90_000_000;
        double noSpare = Finance.WageBar(club, "ENG-1", "standard", f);
        Assert.Equal(f.SpareMoneyToWageBar * 200_000_000, withSpare - noSpare, 0);
        club.LastSeasonIncome = 0; club.Balance = 300_000_000;                               // nothing settled yet: no spare money
        Assert.Equal(noSpare, Finance.WageBar(club, "ENG-1", "standard", f), 0);
    }

    [Fact]
    public void AMissedObjective_CostsMoreTheFurtherItIsMissed()
    {
        var standard = D.Board.Objectives["standard"];
        double one = Board.MissedConfidence(standard, 1), three = Board.MissedConfidence(standard, 3), ten = Board.MissedConfidence(standard, 10);
        Assert.True(one < 0 && three < one && ten <= three);
        Assert.Equal(standard.ConfidenceMissed, ten);                                         // never more than the full penalty
        Assert.Equal(-standard.ConfidencePerPlaceMissed, one);
    }

    [Fact]
    public void RetiredPlayers_AreArchivedSmall_NeverPlayedOnesAreDropped()
    {
        var w = new WorldGenerator(D).Generate(8);
        var cycle = new SeasonCycle(D); var rng = new LegendsFC.Core.Util.GameRandom(9);
        cycle.Advance(w, rng); cycle.Advance(w, rng);
        Assert.NotEmpty(w.RetiredPlayers);
        Assert.DoesNotContain(w.Players, p => p.Retired && p.RetiredYear < w.SeasonStartYear);
        Assert.All(w.RetiredPlayers, a => Assert.True(a.Stats.Count > 0 || a.FormerClubIds.Count > 0));
        Assert.All(w.RetiredPlayers, a => Assert.Equal(a.Stats.Count, a.Stats.Select(s => (s.Season, s.ClubId)).Distinct().Count()));   // one line per season and club
        var any = w.RetiredPlayers[0];
        Assert.Equal(any.Name, w.PlayerName(any.Id));
    }

    [Fact]
    public void TheUsersClub_StartsWith31AtMost_SoTheFirstProtegeFits()
    {
        var store = new SaveStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lfc-31-" + System.Guid.NewGuid().ToString("N")), D.Saves);
        var s = GameSession.NewWorld(D, store, "x", 31, "EUR", "t");
        s.Autosave = false;
        var full = s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ENG-1");
        var donor = s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ENG-2");
        foreach (var p in s.World.Players.Where(p => p.ClubId == donor.Id).Take(32 - s.World.Players.Count(x => x.ClubId == full.Id)).ToList()) p.ClubId = full.Id;
        Assert.Equal(32, s.World.Players.Count(p => p.ClubId == full.Id));
        s.PickClub(full.Id, "t");
        Assert.Equal(D.Development.UserStartMaxSquad, s.World.Players.Count(p => p.ClubId == full.Id));
        Assert.True(s.World.Players.Count(p => p.ClubId == full.Id && p.MainPosition == Position.GK) >= D.Development.AiMinGoalkeepers);
        var pro = s.CreateFirstProtege("Test Kid", full.CountryId, Position.ST, Foot.Right, null);
        Assert.Equal(32, s.World.Players.Count(p => p.ClubId == full.Id));
    }

    [Fact]
    public void AFullSquad_GetsAWarning_AndGraduatesWithoutRoomLeaveAsFreeAgents()
    {
        var store = new SaveStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lfc-room-" + System.Guid.NewGuid().ToString("N")), D.Saves);
        var s = GameSession.NewWorld(D, store, "x", 32, "EUR", "t");
        s.Autosave = false;
        var club = s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ESP-1");
        s.PickClub(club.Id, "t");
        int year = s.World.SeasonStartYear;
        void Fill()
        {
            // Nobody leaves at the season end, and the squad is full.
            foreach (var p in s.World.Players.Where(p => p.ClubId == club.Id)) p.ContractEndYear = Math.Max(p.ContractEndYear, year + 3);
            var donor = s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ESP-2");
            foreach (var p in s.World.Players.Where(p => p.ClubId == donor.Id).Take(32 - s.World.Players.Count(x => x.ClubId == club.Id)).ToList())
            { p.ClubId = club.Id; p.ContractEndYear = year + 3; }
            s.World.Market.SquadsChanged();
        }
        var warned = false; var noRoom = false;
        for (int i = 0; i < 52; i++)
        {
            if (i == 40 || i == 50) Fill();
            s.AdvanceWeek("t");
            if (s.World.UserClubId == null) return;   // sacked: nothing more to check in this world
            warned |= s.NewMessages.Any(m => m.TriggerId == "MSG-ACADEMY-ROOM");
            noRoom |= s.NewMessages.Any(m => m.TriggerId == "MSG-ACADEMY-NO-ROOM");
        }
        for (int i = 0; i < 2; i++) { s.AdvanceWeek("t"); noRoom |= s.NewMessages.Any(m => m.TriggerId == "MSG-ACADEMY-NO-ROOM"); }
        Assert.True(warned);
        Assert.True(noRoom);
        Assert.True(s.World.Players.Count(p => p.ClubId == club.Id) <= D.Development.MaxSquadSize);
        Assert.Contains(s.World.Players, p => p.AcademyClubId == club.Id && p.ClubId == null && p.FormerClubIds.Contains(club.Id) && p.BirthYear >= year - 17);
    }

    [Fact]
    public void Relegation_CutsWages30Percent_AndATopDivisionClubGetsAParachute()
    {
        var w = new WorldGenerator(D).Generate(61);
        var cycle = new SeasonCycle(D); var rng = new LegendsFC.Core.Util.GameRandom(62);
        var wagesBefore = w.Players.Where(p => p.ClubId != null).ToDictionary(p => p.Id, p => p.Wage);
        var leagueBefore = new System.Collections.Generic.Dictionary<string, string>(w.ClubLeague);
        var cal = new SeasonCalendar(D);
        cal.Start(w, rng);
        while (!w.Calendar.SeasonOver) cal.PlayWeek(w, rng);
        var outcomes = cal.Outcomes(w);
        var down = outcomes.First(o => o?.CompetitionId == "ENG-1").Relegated[0];
        var wagesAtEnd = w.Players.Where(p => p.ClubId == down && p.LoanFromClubId == null).ToDictionary(p => p.Id, p => p.Wage);
        cycle.EndSeason(w, rng, outcomes);
        var club = w.Clubs.First(c => c.Id == down);
        Assert.Equal("ENG-2", w.ClubLeague[down]);
        // Players who renewed at the season end have new wages; the rest kept 70% of theirs.
        Assert.True(wagesAtEnd.Keys.Count(id => w.Players.Any(p => p.Id == id && p.ClubId == down && Math.Abs(p.Wage - wagesAtEnd[id] * 0.7) <= 1)) > 10);
        double gap = D.Finance.For("ENG-1").TvPerClubEur - D.Finance.For("ENG-2").TvPerClubEur;
        Assert.Equal((long)Math.Round(gap * D.Finance.ParachuteShareOfTvGap), club.ParachuteEur);
    }

    [Fact]
    public void TheAssistant_PicksTheMentality_AndRestsTiredPlayers_UntilYouTakeOver()
    {
        var store = new SaveStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lfc-asst-" + System.Guid.NewGuid().ToString("N")), D.Saves);
        var s = GameSession.NewWorld(D, store, "x", 63, "EUR", "t");
        s.Autosave = false;
        s.PickClub(s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ENG-1").Id, "t");
        var t = s.UserClub.Tactics;
        Assert.True(t.AssistantMentality && t.AssistantRest);
        var p = s.World.Players.First(x => x.ClubId == s.World.UserClubId && x.MainPosition != Position.GK);
        s.SetRegime(p.Id, "heavy");
        p.Energy = 40;
        Squad.Fitness.AiRegimes(s.World, D);
        Assert.Equal("light", p.Regime);                 // rested
        p.Energy = 90;
        Squad.Fitness.AiRegimes(s.World, D);
        Assert.Equal("heavy", p.Regime);                 // back on the regime you chose
        s.SetMentality(2);
        Assert.False(t.AssistantMentality);              // choosing yourself turns it off
        s.SetAssistant(true, false);
        p.Energy = 40;
        Squad.Fitness.AiRegimes(s.World, D);
        Assert.Equal("heavy", p.Regime);                 // resting is off
    }
}
