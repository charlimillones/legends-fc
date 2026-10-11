using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Saves;
using LegendsFC.Core.Season;
using LegendsFC.Core.Squad;
using LegendsFC.Core.Util;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// A match the user played live goes into the season through the same code path as every sim result (Oct 11 brief):
/// the peek at the next fixture, the played result replacing the sim for that one fixture, and knockout settling.
/// </summary>
public class PlayedMatchTests : IDisposable
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfc-played-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;
    public PlayedMatchTests(ITestOutputHelper o) { _out = o; }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static string Snapshot(GameSession s) => JsonConvert.SerializeObject(new { s.World, Rng = s.Rng.GetState() }, SaveStore.Json);

    /// <summary>Seed 77, the 11th club of ENG-1 by reputation, advanced <paramref name="weeks"/> weeks (8 = the eve of the first league week).</summary>
    private GameSession New(int weeks, string tag = "a", ulong seed = 77)
    {
        var s = GameSession.NewWorld(D, new SaveStore(Path.Combine(_root, tag), D.Saves), "x", seed, "EUR", "t");
        s.Autosave = false;
        s.PickClub(s.World.Clubs.Where(c => s.World.ClubLeague[c.Id] == "ENG-1").OrderByDescending(c => c.Reputation).ElementAt(10).Id, "t");
        for (int i = 0; i < weeks; i++) s.AdvanceWeek("t");
        return s;
    }

    /// <summary>Everyone in both starting elevens plays 90 minutes; no goals.</summary>
    private static PlayedMatch FullMatch(UserFixture fx, double km = 10.0)
    {
        var m = new PlayedMatch { CompetitionId = fx.CompetitionId, HomeClubId = fx.Fixture.Home, AwayClubId = fx.Fixture.Away };
        foreach (var (sheet, club) in new[] { (fx.HomeSheet, fx.Fixture.Home), (fx.AwaySheet, fx.Fixture.Away) })
            foreach (var p in sheet.Starters.Where(p => p != null))
                m.Players.Add(new PlayedPlayer { PlayerId = p.Id, ClubId = club, Minutes = 90, Km = km, Passes = 30, Shots = 1 });
        return m;
    }

    private static List<Player> Outfield(TeamSheet sheet) => sheet.Starters.Where(p => p != null && sheet.SlotPositions[sheet.Starters.IndexOf(p)] != Position.GK).ToList();

    private static TableRow Row(CompetitionRun run, string club)
    {
        var t = new LeagueTable(run.Clubs, D.MatchSim);
        foreach (var round in run.PlayedRounds) foreach (var r in round) t.Add(r);
        return t.Standings().First(x => x.ClubId == club);
    }

    private static StatLine Line(Player p, int season, string comp, string club)
        => p.Stats.FirstOrDefault(l => l.Season == season && l.CompetitionId == comp && l.ClubId == club) ?? new StatLine();

    // ---- stage 1: the peek

    [Fact]
    public void NextUserFixture_LooksOnly_NoStateChangeAndNoRandomNumbers()
    {
        var s = New(8);
        string before = Snapshot(s);
        var rng = s.Rng.GetState();
        var fx = s.NextUserFixture();
        var again = s.NextUserFixture();
        Assert.Equal(before, Snapshot(s));
        Assert.Equal(rng, s.Rng.GetState());
        Assert.NotNull(fx);
        Assert.Equal(9, fx.Week);
        Assert.Equal("ENG-1", fx.CompetitionId);
        Assert.False(fx.Decide);
        Assert.Null(fx.FirstLeg);
        Assert.True(fx.Fixture.Home == s.World.UserClubId || fx.Fixture.Away == s.World.UserClubId);
        Assert.Equal(s.World.UserClubId, fx.UserSheet.ClubId);
        Assert.Equal(11, fx.HomeSheet.Starters.Count(p => p != null));
        Assert.Equal(11, fx.AwaySheet.Starters.Count(p => p != null));
        Assert.Equal(fx.HomeSheet.Starters.Select(p => p?.Id), again.HomeSheet.Starters.Select(p => p?.Id));
        Assert.Equal(fx.AwaySheet.Mentality, again.AwaySheet.Mentality);
        Assert.Null(New(0, "b").NextUserFixture());                             // pre-season: no match in week 1
    }

    [Fact]
    public void NextUserFixture_UsesTheTacticsScreenLineup()
    {
        var s = New(8);
        var fx = s.NextUserFixture();
        var mine = fx.UserSheet;
        // Swap two outfield starters on the Tactics screen: the peek shows the user's own lineup.
        var ids = mine.Starters.Select(p => p.Id).ToList();
        (ids[1], ids[2]) = (ids[2], ids[1]);
        s.SetLineup(ids, mine.Bench.Select(p => p.Id).ToList());
        s.SetMentality(1);
        var fx2 = s.NextUserFixture();
        Assert.Equal(ids, fx2.UserSheet.Starters.Select(p => p.Id));
        Assert.Equal(1, fx2.UserSheet.Mentality);
    }

    [Fact]
    public void NextUserFixture_AgreesWithTheSim_StartersAndPositions()
    {
        // Weeks 9-26 (no transfer window): the starting elevens of the user's first match each week are the peeked ones.
        var s = New(8);
        int checkedWeeks = 0;
        for (int week = 9; week <= 26; week++)
        {
            var fx = s.NextUserFixture();
            var report = s.AdvanceWeek("t");
            if (fx == null) { Assert.Empty(report.UserMatches); continue; }
            var r = report.UserMatches.First();
            Assert.Equal((fx.Fixture.Home, fx.Fixture.Away), (r.Home, r.Away));
            foreach (var sheet in new[] { fx.HomeSheet, fx.AwaySheet })
            {
                var expected = Enumerable.Range(0, sheet.Starters.Count).Where(i => sheet.Starters[i] != null)
                    .Select(i => sheet.Starters[i].Id + "@" + sheet.SlotPositions[i]).OrderBy(x => x, StringComparer.Ordinal);
                var actual = r.Players.Where(l => l.ClubId == sheet.ClubId && l.Started).Select(l => l.PlayerId + "@" + l.Position).OrderBy(x => x, StringComparer.Ordinal);
                Assert.Equal(expected, actual);
            }
            checkedWeeks++;
        }
        _out.WriteLine($"{checkedWeeks} weeks checked");
        Assert.True(checkedWeeks >= 15);
    }

    // ---- stage 2: the played match in the season

    private static PlayedMatch ThreeOne(UserFixture fx, out Dictionary<string, (int goals, int assists)> expected, out (string off, string on) sub)
    {
        var m = FullMatch(fx, km: 10.5);
        var us = Outfield(fx.UserSheet); var them = Outfield(fx.OpponentSheet);
        string usClub = fx.UserSheet.ClubId, themClub = fx.OpponentSheet.ClubId;
        string a = us[us.Count - 1].Id, b = us[us.Count - 2].Id, c = us[us.Count - 3].Id;   // the forwards are the last slots
        string d = them[them.Count - 1].Id, e = them[them.Count - 2].Id;
        m.Goals.Add(new PlayedGoal { Minute = 12, ClubId = usClub, ScorerId = a, AssistId = c });
        m.Goals.Add(new PlayedGoal { Minute = 40, ClubId = usClub, ScorerId = b, AssistId = a });
        m.Goals.Add(new PlayedGoal { Minute = 60, ClubId = themClub, ScorerId = d, AssistId = e });
        m.Goals.Add(new PlayedGoal { Minute = 77, ClubId = usClub, ScorerId = a });
        if (fx.UserIsHome) { m.HomeGoals = 3; m.AwayGoals = 1; } else { m.HomeGoals = 1; m.AwayGoals = 3; }
        expected = new Dictionary<string, (int, int)> { [a] = (2, 1), [b] = (1, 0), [c] = (0, 1), [d] = (1, 0), [e] = (0, 1) };
        // One substitution: the user's first outfield starter goes off at 70, a bench player comes on.
        var off = us[0]; var on = fx.UserSheet.Bench.First(p => p.MainPosition != Position.GK);
        m.Players.First(p => p.PlayerId == off.Id).Minutes = 70;
        m.Players.Add(new PlayedPlayer { PlayerId = on.Id, ClubId = usClub, Minutes = 20, Km = 2.6, Passes = 9, Shots = 0 });
        foreach (var p in m.Players.Where(p => p.PlayerId == a || p.PlayerId == b || p.PlayerId == d)) p.Shots = 4;
        sub = (off.Id, on.Id);
        return m;
    }

    [Fact]
    public void APlayed3To1_GoesIntoResultsTableStatsEnergyRatingsAndForm()
    {
        var s = New(8);
        var w = s.World; var c = D.Squad;
        var fx = s.NextUserFixture();
        var played = ThreeOne(fx, out var expected, out var sub);
        string us = fx.UserSheet.ClubId, them = fx.OpponentSheet.ClubId;
        int season = w.SeasonStartYear;
        var run = w.Calendar.Runs.First(r => r.CompetitionId == fx.CompetitionId);
        var usRow0 = Row(run, us); var themRow0 = Row(run, them);
        var players = played.Players.ToDictionary(p => p.PlayerId, p => w.Players.First(x => x.Id == p.PlayerId));
        var stats0 = played.Players.ToDictionary(p => p.PlayerId, p => Line(players[p.PlayerId], season, fx.CompetitionId, p.ClubId));
        var stats0Copy = stats0.ToDictionary(kv => kv.Key, kv => (kv.Value.Apps, kv.Value.Minutes, kv.Value.Goals, kv.Value.Assists, kv.Value.Starts, kv.Value.SubApps));
        var energy0 = players.ToDictionary(kv => kv.Key, kv => kv.Value.Energy);
        var ratings0 = players.ToDictionary(kv => kv.Key, kv => kv.Value.RecentMatchRatings.Count);

        var report = s.AdvanceWeek("t", played);

        // Results: the live score, in the report and in the competition's results.
        var r = report.UserMatches.First();
        Assert.Equal((fx.Fixture.Home, fx.Fixture.Away, played.HomeGoals, played.AwayGoals), (r.Home, r.Away, r.HomeGoals, r.AwayGoals));
        var saved = run.PlayedRounds.Last().Single(x => x.Home == fx.Fixture.Home && x.Away == fx.Fixture.Away);
        Assert.Equal((played.HomeGoals, played.AwayGoals), (saved.HomeGoals, saved.AwayGoals));
        Assert.Equal(4, r.Events.Count(e => e.Type == EventType.Goal));
        Assert.DoesNotContain(r.Events, e => e.Type != EventType.Goal);                 // no simulated cards, injuries or subs
        Assert.Equal(played.Players.Count, r.Players.Count);

        // League table: +1 played, +3 points, 3 for and 1 against for the user; the opponent the other way round.
        var usRow = Row(run, us); var themRow = Row(run, them);
        Assert.Equal((usRow0.Played + 1, usRow0.Won + 1, usRow0.Points + 3, usRow0.GoalsFor + 3, usRow0.GoalsAgainst + 1),
                     (usRow.Played, usRow.Won, usRow.Points, usRow.GoalsFor, usRow.GoalsAgainst));
        Assert.Equal((themRow0.Played + 1, themRow0.Lost + 1, themRow0.Points, themRow0.GoalsFor + 1, themRow0.GoalsAgainst + 3),
                     (themRow.Played, themRow.Lost, themRow.Points, themRow.GoalsFor, themRow.GoalsAgainst));

        foreach (var pp in played.Players)
        {
            var p = players[pp.PlayerId];
            var line = r.Players.Single(l => l.PlayerId == pp.PlayerId);
            // Minutes, Km, passes and shots recorded as played.
            Assert.Equal((pp.Minutes, pp.Km, pp.Passes, pp.Shots), (line.Minutes, line.Km, line.Passes, line.Shots));
            Assert.Equal(pp.PlayerId != sub.on, line.Started);
            // Season stats: apps, minutes, goals and assists.
            var st = Line(p, season, fx.CompetitionId, pp.ClubId); var st0 = stats0Copy[pp.PlayerId];
            expected.TryGetValue(pp.PlayerId, out var ga);
            Assert.Equal((st0.Apps + 1, st0.Minutes + pp.Minutes, st0.Goals + ga.goals, st0.Assists + ga.assists),
                         (st.Apps, st.Minutes, st.Goals, st.Assists));
            Assert.Equal((ga.goals, ga.assists), (line.Goals, line.Assists));
            Assert.Equal(pp.PlayerId == sub.on ? st0.SubApps + 1 : st0.SubApps, st.SubApps);
            // Rating 1-10 for everyone with enough minutes, added to his form.
            if (pp.Minutes >= D.MatchEvents.MinMinutesForRating)
            {
                Assert.InRange(line.Rating, 1, 10);
                Assert.Equal(line.Rating, p.RecentMatchRatings.Last());
                Assert.True(p.RecentMatchRatings.Count == Math.Min(D.MatchEvents.FormMatches, ratings0[pp.PlayerId] + 1));
            }
            // Energy: the match cost by the existing formula, then the weekly rest by regime (no random event hit these players this week).
            double cost = (pp.Km * c.EnergyPerKm + pp.Passes * c.EnergyPerPass + pp.Shots * c.EnergyPerShot) * Fitness.StaminaFactor(p, c);
            double afterMatch = Math.Max(0, Math.Round(energy0[pp.PlayerId] - cost, 1));
            double rest = c.WeeklyRecovery[p.Regime ?? "moderate"] * (p.PersonalityId != null && c.PersonalityRecovery.TryGetValue(p.PersonalityId, out var m) ? m : 1);
            Assert.Equal(Math.Min(100, Math.Round(afterMatch + rest, 1)), p.Energy);
        }
        // The winners rate higher on average than the losers (won/lost is in the rating rule).
        double Avg(string club) => r.Players.Where(l => l.ClubId == club && l.Rating > 0).Average(l => l.Rating);
        Assert.True(Avg(us) > Avg(them));
        Assert.NotNull(r.PlayerOfTheMatch);
        _out.WriteLine($"{r.Home} {r.HomeGoals}-{r.AwayGoals} {r.Away}; MOTM {r.PlayerOfTheMatch}; avg rating {Avg(us):F2} v {Avg(them):F2}");
    }

    [Fact]
    public void OtherFixturesThatWeek_AreStillSimulated()
    {
        // Same seed: one world plays the week in sim mode, the other with the user's match played. Every other fixture of
        // that round is still simulated; those played before the user's match have the very same results (same random draws).
        var sim = New(8, "s"); var live = New(8, "l");
        var fx = live.NextUserFixture();
        sim.AdvanceWeek("t");
        live.AdvanceWeek("t", ThreeOne(fx, out _, out _));
        var simRound = sim.World.Calendar.Runs.First(r => r.CompetitionId == fx.CompetitionId).PlayedRounds.Last();
        var liveRound = live.World.Calendar.Runs.First(r => r.CompetitionId == fx.CompetitionId).PlayedRounds.Last();
        int i = simRound.FindIndex(r => r.Home == fx.Fixture.Home && r.Away == fx.Fixture.Away);
        Assert.Equal(simRound.Count, liveRound.Count);
        for (int k = 0; k < i; k++)
            Assert.Equal((simRound[k].Home, simRound[k].Away, simRound[k].HomeGoals, simRound[k].AwayGoals), (liveRound[k].Home, liveRound[k].Away, liveRound[k].HomeGoals, liveRound[k].AwayGoals));
        Assert.Equal(simRound.Count - 1, liveRound.Count(r => !(r.Home == fx.Fixture.Home && r.Away == fx.Fixture.Away)));   // the rest were simulated
    }

    [Fact]
    public void SameSeedAndSamePlayedMatch_GiveTheSameWorldTwice()
    {
        var a = New(8, "a"); var b = New(8, "b");
        var fxa = a.NextUserFixture(); var fxb = b.NextUserFixture();
        a.AdvanceWeek("t", ThreeOne(fxa, out _, out _));
        b.AdvanceWeek("t", ThreeOne(fxb, out _, out _));
        for (int i = 0; i < 4; i++) { a.AdvanceWeek("t"); b.AdvanceWeek("t"); }
        Assert.Equal(Snapshot(a), Snapshot(b));
    }

    [Fact]
    public void InvalidPlayedMatches_ThrowAndChangeNothing()
    {
        var s = New(8);
        var fx = s.NextUserFixture();
        string before = Snapshot(s);
        var bad = new List<(string why, Action<PlayedMatch> spoil)>
        {
            ("wrong competition", m => m.CompetitionId = "ENG-FA"),
            ("home and away swapped", m => { var h = m.HomeClubId; m.HomeClubId = m.AwayClubId; m.AwayClubId = h; }),
            ("a club not in the match", m => m.AwayClubId = s.World.Clubs.First(c => c.Id != fx.Fixture.Home && c.Id != fx.Fixture.Away).Id),
            ("scorer not in the list", m => m.Goals[0].ScorerId = fx.UserSheet.Bench.Last().Id),
            ("assist not in the list", m => m.Goals[0].AssistId = "PLY-NOPE"),
            ("negative minutes", m => m.Players[3].Minutes = -1),
            ("negative distance", m => m.Players[3].Km = -0.5),
            ("negative passes", m => m.Players[3].Passes = -2),
            ("negative goals", m => m.HomeGoals = -1),
            ("goals listed don't match the score", m => m.Goals.RemoveAt(0)),
            ("player not on the team sheet", m => m.Players.Add(new PlayedPlayer { PlayerId = s.World.Players.First(p => p.ClubId == fx.Fixture.Home && !fx.HomeSheet.Starters.Concat(fx.HomeSheet.Bench).Any(x => x?.Id == p.Id)).Id, ClubId = fx.Fixture.Home, Minutes = 5 })),
            ("player listed twice", m => m.Players.Add(m.Players[0])),
            ("extra time in a league match", m => m.ExtraTimePlayed = true),
        };
        foreach (var (why, spoil) in bad)
        {
            var m = ThreeOne(fx, out _, out _);
            spoil(m);
            var ex = Assert.Throws<ArgumentException>(() => s.AdvanceWeek("t", m));
            _out.WriteLine(why + ": " + ex.Message);
            Assert.Equal(before, Snapshot(s));
        }
        Assert.Throws<ArgumentException>(() => New(0, "z").AdvanceWeek("t", ThreeOne(fx, out _, out _)));   // no match this week
        // The valid one still plays.
        s.AdvanceWeek("t", ThreeOne(fx, out _, out _));
        Assert.Equal(9, s.World.Calendar.Week);
    }

    [Fact]
    public void AnOwnGoal_CountsForTheOtherClub_AndSurvivesSaveAndLoad()
    {
        var store = new SaveStore(Path.Combine(_root, "save"), D.Saves);
        var s = GameSession.NewWorld(D, store, "x", 77, "EUR", "t");
        s.Autosave = false;
        s.PickClub(s.World.Clubs.Where(c => s.World.ClubLeague[c.Id] == "ENG-1").OrderByDescending(c => c.Reputation).ElementAt(10).Id, "t");
        for (int i = 0; i < 8; i++) s.AdvanceWeek("t");
        var fx = s.NextUserFixture();
        var m = FullMatch(fx);
        var defender = Outfield(fx.OpponentSheet)[0];
        m.Goals.Add(new PlayedGoal { Minute = 33, ClubId = fx.UserSheet.ClubId, ScorerId = defender.Id, OwnGoal = true });
        if (fx.UserIsHome) m.HomeGoals = 1; else m.AwayGoals = 1;
        int goals0 = defender.Stats.Sum(l => l.Goals);
        var r = s.AdvanceWeek("t", m).UserMatches.First();
        Assert.Equal(goals0, defender.Stats.Sum(l => l.Goals));                     // an own goal is not his goal
        var e = r.Events.Single(x => x.Type == EventType.Goal);
        Assert.True(e.OwnGoal); Assert.Equal(fx.UserSheet.ClubId, e.ClubId);
        s.Save("t");
        var back = GameSession.Load(D, store, s.SlotId);
        Assert.Equal(Snapshot(s), Snapshot(back));
        Assert.True(back.World.Calendar.Runs.First(x => x.CompetitionId == fx.CompetitionId).PlayedRounds.Last()
            .Single(x => x.Home == fx.Fixture.Home).Events.Single(x => x.Type == EventType.Goal).OwnGoal);
    }

    // ---- the engine on its own: knockouts and energy

    /// <summary>A cup tie between the user's club and its next league opponent, played through MatchEngine.Play with a live result.</summary>
    private (MatchResult result, PlayedMatch played, UserFixture fx, MatchEngine.Context ctx) Cup(int homeGoals, int awayGoals, bool extraTimePlayed, bool roundHasExtraTime = true, int minutes = 90, double km = 10.0)
    {
        var s = New(8);
        var league = s.NextUserFixture();
        var f = league.Fixture;
        MatchEngine.TeamSheets("ENG-FA", f, s.World, new RatingTable(D), D, false, out var home, out var away);
        var fx = new UserFixture { CompetitionId = "ENG-FA", Stage = "Round", Fixture = f, Decide = true, ExtraTime = roundHasExtraTime, HomeSheet = home, AwaySheet = away, UserIsHome = league.UserIsHome };
        var m = FullMatch(fx, km);
        foreach (var p in m.Players) p.Minutes = minutes;
        m.HomeGoals = homeGoals; m.AwayGoals = awayGoals; m.ExtraTimePlayed = extraTimePlayed;
        for (int g = 0; g < homeGoals; g++) m.Goals.Add(new PlayedGoal { Minute = 20 + g, ClubId = f.Home, ScorerId = Outfield(home).Last().Id });
        for (int g = 0; g < awayGoals; g++) m.Goals.Add(new PlayedGoal { Minute = 50 + g, ClubId = f.Away, ScorerId = Outfield(away).Last().Id });
        PlayedMatchCheck.Validate(m, fx);
        var ctx = new MatchEngine.Context { World = s.World, Data = D, Ratings = new RatingTable(D), Rng = new GameRandom(2026), Played = m, PlayedFixture = fx };
        var r = MatchEngine.Play("ENG-FA", f, true, roundHasExtraTime, null, ctx);
        Assert.True(ctx.PlayedApplied);
        return (r, m, fx, ctx);
    }

    [Fact]
    public void AKnockout_PlayedLevelWithoutExtraTime_GetsSimulatedExtraTime()
    {
        var (r, m, fx, _) = Cup(0, 0, extraTimePlayed: false);
        Assert.True(r.AfterExtraTime);
        Assert.True(r.Events.Where(e => e.Type == EventType.Goal).All(e => e.Minute >= 91 && e.Minute <= 120));
        Assert.Equal(r.HomeGoals == r.AwayGoals, r.PenaltyWinner != null);
        // Everyone who played the full 90 stayed on: 30 more minutes and the sim's distance estimate for them.
        foreach (var l in r.Players) { Assert.Equal(120, l.Minutes); Assert.True(l.Km > 10.0); }
        _out.WriteLine($"0-0 after 90, {r.HomeGoals}-{r.AwayGoals} after extra time, penalties: {r.PenaltyWinner ?? "-"}");
        // Same tie, but the round has no extra time: straight to penalties.
        var (r2, _, _, _) = Cup(1, 1, extraTimePlayed: false, roundHasExtraTime: false);
        Assert.False(r2.AfterExtraTime);
        Assert.Equal((1, 1), (r2.HomeGoals, r2.AwayGoals));
        Assert.NotNull(r2.PenaltyWinner);
    }

    [Fact]
    public void AKnockout_PlayedWithExtraTimeAndStillLevel_GoesToPenalties()
    {
        var (r, _, fx, _) = Cup(1, 1, extraTimePlayed: true, minutes: 120);
        Assert.True(r.AfterExtraTime);
        Assert.Equal((1, 1), (r.HomeGoals, r.AwayGoals));                          // no simulated goals added
        Assert.Contains(r.PenaltyWinner, new[] { fx.Fixture.Home, fx.Fixture.Away });
        Assert.All(r.Players, l => Assert.Equal(120, l.Minutes));
        // A winner on the day needs no penalties.
        var (won, _, _, _) = Cup(2, 1, extraTimePlayed: false);
        Assert.False(won.AfterExtraTime);
        Assert.Null(won.PenaltyWinner);
    }

    [Fact]
    public void APlayer_90MinutesAnd11Km_LosesEnergyByTheExistingFormula()
    {
        var s = New(8);
        var fx = s.NextUserFixture();
        var m = FullMatch(fx, km: 11.0);
        var who = fx.UserSheet.Starters[5];
        var pp = m.Players.Single(p => p.PlayerId == who.Id);
        pp.Passes = 45; pp.Shots = 2;
        double e0 = who.Energy;
        var ctx = new MatchEngine.Context { World = s.World, Data = D, Ratings = new RatingTable(D), Rng = new GameRandom(9), Played = m, PlayedFixture = fx };
        var r = MatchEngine.Play(fx.CompetitionId, fx.Fixture, false, false, null, ctx);
        var c = D.Squad;
        double cost = (11.0 * c.EnergyPerKm + 45 * c.EnergyPerPass + 2 * c.EnergyPerShot) * Fitness.StaminaFactor(who, c);
        Assert.Equal(Math.Max(0, Math.Round(e0 - cost, 1)), who.Energy);
        Assert.True(who.Energy < e0);
        var line = r.Players.Single(l => l.PlayerId == who.Id);
        Assert.Equal((90, 11.0, 45, 2), (line.Minutes, line.Km, line.Passes, line.Shots));   // measured values are not re-estimated
        _out.WriteLine($"energy {e0} -> {who.Energy} (cost {cost:F2}, stamina {who.Attributes[Attr.Stamina]})");
    }
}
