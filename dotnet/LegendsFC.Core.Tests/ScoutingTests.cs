using System;
using System.IO;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Saves;
using LegendsFC.Core.Scouting;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

/// <summary>Scouting (rules decided Oct 7; numbers PROPOSAL, Oct 9 night).</summary>
public class ScoutingTests : IDisposable
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfc-scout-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;
    public ScoutingTests(ITestOutputHelper o) { _out = o; }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static Club SetLevel(GameWorld w, Club c, int level) { c.Facilities[Facility.ScoutingCentre] = new FacilityState { Level = level, Condition = 100 }; return c; }

    [Fact]
    public void Coverage_GrowsWithTheScoutingCentre()
    {
        var w = new WorldGenerator(D).Generate(31);
        var club = w.Clubs.First(c => w.ClubLeague[c.Id] == "ENG-2");
        Player In(string key) => w.Players.First(p => p.ClubId != null && (w.ClubLeague.TryGetValue(p.ClubId, out var l) && l == key || (l == null && w.Clubs.First(c => c.Id == p.ClubId).CountryId == key)));
        var sameLeague = In("ENG-2"); var sameCountry = In("ENG-1"); var europe = In("ESP-1"); var brazil = In("BRA-1"); var portugal = In("POR");
        bool Sees(int level, Player p) => Scouts.Visible(w, SetLevel(w, club, level), p, D);
        Assert.True(Sees(1, sameLeague)); Assert.False(Sees(1, sameCountry));
        Assert.True(Sees(2, sameCountry)); Assert.False(Sees(2, europe));
        Assert.True(Sees(4, europe)); Assert.True(Sees(4, portugal)); Assert.False(Sees(4, brazil));
        Assert.True(Sees(6, brazil));
        Assert.True(Sees(8, w.Players.First(p => p.ClubId != null && w.ClubLeague[p.ClubId] == null && w.Clubs.First(c => c.Id == p.ClubId).CountryId == "MEX")));
    }

    [Fact]
    public void TheViewNeverHasHiddenFields_OwnPlayersExact_OthersRanges_PotentialNeverExact()
    {
        var names = typeof(PlayerView).GetFields().Select(f => f.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Archetype") || n.Contains("Decline") || n == "RetireAge" || n == "Potential");
        var w = new WorldGenerator(D).Generate(33);
        var club = SetLevel(w, w.Clubs.First(c => w.ClubLeague[c.Id] == "ESP-1"), 1);
        var mine = w.Players.First(p => p.ClubId == club.Id);
        var v = Scouts.View(w, club, mine, D);
        Assert.True(v.Exact);
        Assert.Equal(mine.Attributes.Values, v.AttributeLow);
        Assert.Null(v.PotentialLow);
        var other = w.Players.First(p => p.ClubId != null && p.ClubId != club.Id && w.ClubLeague[p.ClubId] == "ESP-1" && p.PersonalityId != null);
        foreach (var level in Enumerable.Range(1, 10))
        {
            SetLevel(w, club, level);
            var o = Scouts.View(w, club, other, D);
            int half = D.Scouting.RangeByLevel[level - 1];
            for (int i = 0; i < AttributeSet.Count; i++)
            {
                Assert.InRange(other.Attributes.Values[i], o.AttributeLow[i], o.AttributeHigh[i]);
                Assert.True(o.AttributeHigh[i] - o.AttributeLow[i] <= 2 * half);
            }
            if (level < 7) Assert.Null(o.PotentialLow);
            else { Assert.InRange(other.Potential, o.PotentialLow.Value, o.PotentialHigh.Value); Assert.True(o.PotentialHigh > o.PotentialLow); }
            Assert.Equal(level >= 9, o.PersonalityKnown);
            Assert.Equal(level >= 9 ? other.PersonalityId : null, o.PersonalityId);
        }
        // The same view twice in a season: the ranges don't jump around.
        Assert.Equal(JsonConvert.SerializeObject(Scouts.View(w, club, other, D)), JsonConvert.SerializeObject(Scouts.View(w, club, other, D)));
    }

    [Fact]
    public void Scouts_ReportOnTheirTask_BetterScoutsAreMoreAccurate_ReportsOpenTheMarket()
    {
        var store = new SaveStore(_root, D.Saves);
        var s = GameSession.NewWorld(D, store, "x", 35, "EUR", "t"); s.Autosave = false;
        s.PickClub(s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ARG-2").Id, "t");
        SetLevel(s.World, s.UserClub, 1);
        s.UserClub.Balance = 2_000_000_000;
        var good = s.FreeScouts.First(); var poor = s.FreeScouts.Last();
        Assert.Equal(LegendsFC.Core.Squad.HireResult.Done, s.HireScout(good.Id, 2));
        Assert.Equal(LegendsFC.Core.Squad.HireResult.Done, s.HireScout(poor.Id, 2));
        s.SetScoutTask(good.Id, "BRA", null, null, 21);
        s.SetScoutTask(poor.Id, "BRA", null, null, 21);
        for (int i = 0; i < 20; i++) s.AdvanceWeek("t");
        var reports = s.Reports;
        Assert.NotEmpty(reports);
        Assert.All(reports, r =>
        {
            var p = s.World.Players.First(x => x.Id == r.PlayerId);
            Assert.True(s.World.SeasonStartYear - p.BirthYear <= 21);                              // the task's age limit (he may have moved club since)
            Assert.InRange(p.Potential, r.PotentialLow, r.PotentialHigh);
        });
        double Width(Scout sc) => reports.Where(r => r.ScoutId == sc.Id).Average(r => r.PotentialHigh - r.PotentialLow);
        _out.WriteLine($"good scout ({good.Rating}) range {Width(good):F1}, poor scout ({poor.Rating}) {Width(poor):F1}; {reports.Count} reports; personalities seen by the good one: {reports.Count(r => r.ScoutId == good.Id && r.PersonalityId != null)}");
        Assert.True(Width(good) < Width(poor));
        var reported = s.World.Players.First(p => p.Id == reports.First(r => r.ScoutId == good.Id).PlayerId);
        Assert.True(Scouts.Visible(s.World, s.UserClub, reported, D));                       // outside his coverage, but scouted
        Assert.Contains(s.SearchPlayers(maxAge: 21), v => v.Id == reported.Id);
        Assert.All(s.SearchPlayers(), v => Assert.True(Scouts.Visible(s.World, s.UserClub, s.World.Players.First(p => p.Id == v.Id), D)));
        Assert.Contains(s.World.Inbox.Messages, m => m.TriggerId == "MSG-SCOUT-REPORT");
    }

    [Fact]
    public void ScoutingNeverChangesTheGame()
    {
        string Play(string country)
        {
            var store = new SaveStore(Path.Combine(_root, country), D.Saves);
            var s = GameSession.NewWorld(D, store, "x", 37, "EUR", "t"); s.Autosave = false;
            s.PickClub(s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "BRA-1").Id, "t");
            var scout = s.FreeScouts.First(); s.UserClub.Balance += 10_000_000;
            s.HireScout(scout.Id, 2);
            s.SetScoutTask(scout.Id, country, null, null, null);
            for (int i = 0; i < 15; i++) s.AdvanceWeek("t");
            s.World.ScoutReports.Clear(); s.World.Inbox = new LegendsFC.Core.Inbox.InboxState();
            foreach (var x in s.World.Scouts) x.Task = new ScoutTask();
            s.World.ReportSeq = 0;
            return JsonConvert.SerializeObject(new { s.World, Rng = s.Rng.GetState() }, SaveStore.Json);
        }
        Assert.Equal(Play("ESP"), Play("ARG"));
    }

    [Fact]
    public void OpponentReport_FromLevel3_FormationAndBestPlayer()
    {
        var w = new WorldGenerator(D).Generate(39);
        var me = w.Clubs.First(c => w.ClubLeague[c.Id] == "ENG-1"); var them = w.Clubs.Last(c => w.ClubLeague[c.Id] == "ENG-1");
        Assert.Null(Scouts.Opponent(w, SetLevel(w, me, 2), them, D));
        var info = Scouts.Opponent(w, SetLevel(w, me, 3), them, D);
        Assert.NotNull(info);
        Assert.Contains(info.Value.formation, D.Squad.Formations.Keys);
        Assert.Equal(them.Id, info.Value.best.ClubId);
    }
}
