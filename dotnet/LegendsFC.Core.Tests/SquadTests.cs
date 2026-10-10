using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Squad;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;
using Xunit.Abstractions;

/// <summary>Squad and tactics (agreed with Carlos Oct 9, with his changes on energy, regimes, injuries and suspensions).</summary>
public class SquadTests
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly ITestOutputHelper _out;
    public SquadTests(ITestOutputHelper o) { _out = o; }

    private static MatchEngine.Context Ctx(GameWorld w, ulong seed = 1) => new MatchEngine.Context { World = w, Data = D, Ratings = new RatingTable(D), Rng = new GameRandom(seed) };
    private static Club ClubIn(GameWorld w, string league, int rank = 0) => w.Clubs.Where(c => w.ClubLeague[c.Id] == league).OrderByDescending(c => c.Reputation).ThenBy(c => c.Id).ElementAt(rank);

    [Fact]
    public void EightFormations_ElevenSlots_OneGoalkeeper()
    {
        Assert.Equal(new[] { "4-4-2", "4-3-3", "4-2-3-1", "4-1-4-1", "3-5-2", "3-4-3", "5-3-2", "4-5-1" }, D.Squad.Formations.Keys);
        Assert.All(D.Squad.Formations.Values, f => { Assert.Equal(11, f.Count); Assert.Equal(1, f.Count(s => s == "GK")); });
        Assert.Equal(Position.LB, Lineups.SlotPosition("LWB"));
        Assert.Equal(Position.RB, Lineups.SlotPosition("RWB"));
        Assert.Equal((11, 9, 5, 5), (D.Squad.Starters, D.Squad.Bench, D.Squad.Substitutions, D.Squad.SavedLineups));
    }

    [Fact]
    public void AiPicksAFullTeam_NoInjuredOrSuspended_BenchOfNineWithAKeeper()
    {
        var w = new WorldGenerator(D).Generate(3);
        var club = ClubIn(w, "ENG-1");
        var squad = LegendsFC.Core.Transfers.Market.Squad(w, club.Id).Where(p => p.MainPosition != Position.GK).ToList();
        squad[0].Injury = new Injury { TypeId = "INJ-KNOCK", WeeksLeft = 1, TotalWeeks = 1 };
        squad[1].Bans.Add(new Ban { Scope = Discipline.Competition, Key = "ENG-1", MatchesLeft = 1 });
        var r = new RatingTable(D);
        var sheet = Lineups.Pick(w, club, "ENG-1", r, D);
        Assert.Equal(11, sheet.Starters.Count(p => p != null));
        Assert.Equal(Position.GK, sheet.Starters[sheet.SlotPositions.IndexOf(Position.GK)].MainPosition);
        Assert.InRange(sheet.Bench.Count, 1, 9);
        Assert.Contains(sheet.Bench, p => p.MainPosition == Position.GK);
        var used = sheet.Starters.Concat(sheet.Bench).Select(p => p.Id).ToList();
        Assert.DoesNotContain(squad[0].Id, used);
        Assert.DoesNotContain(squad[1].Id, used);
        Assert.Equal(used.Count, used.Distinct().Count());
        // The ban is only for the league: he can play in the cup.
        Assert.True(Lineups.Available(w, squad[1], "ENG-FA", D));
    }

    [Fact]
    public void TheUsersLineupIsUsed_AndAnUnavailablePlayerIsReplaced()
    {
        var w = new WorldGenerator(D).Generate(3);
        var club = ClubIn(w, "ESP-1"); w.UserClubId = club.Id;
        var r = new RatingTable(D);
        club.Tactics.Formation = "5-3-2"; club.Tactics.Mentality = 2;
        var auto = Lineups.Pick(w, club, "ESP-1", r, D);
        club.Tactics.Lineup = auto.Starters.Select(p => p.Id).ToList();
        // Swap two outfield players: the user's choice stands even if it's worse.
        (club.Tactics.Lineup[1], club.Tactics.Lineup[10]) = (club.Tactics.Lineup[10], club.Tactics.Lineup[1]);
        var mine = Lineups.Pick(w, club, "ESP-1", r, D);
        Assert.Equal("5-3-2", mine.Formation);
        Assert.Equal(2, mine.Mentality);
        Assert.Equal(club.Tactics.Lineup, mine.Starters.Select(p => p.Id));
        var strong = Lineups.Strength(auto, r, D.Squad); var weak = Lineups.Strength(mine, r, D.Squad);
        Assert.True(weak.overall < strong.overall);                       // out of position costs rating
        var hurt = mine.Starters[4];
        hurt.Injury = new Injury { WeeksLeft = 2, TotalWeeks = 2 };
        var again = Lineups.Pick(w, club, "ESP-1", r, D);
        Assert.DoesNotContain(hurt, again.Starters);
        Assert.Equal(11, again.Starters.Count(p => p != null));
    }

    [Fact]
    public void Mentality_AttackingScoresAndConcedesMore()
    {
        double Goals(int mentality, bool scored)
        {
            var w = new WorldGenerator(D).Generate(5);
            var a = ClubIn(w, "ENG-1", 5); var b = ClubIn(w, "ENG-1", 6);
            w.UserClubId = a.Id; a.Tactics.Mentality = mentality;
            var ctx = Ctx(w, 77);
            int total = 0;
            for (int i = 0; i < 600; i++)
            {
                foreach (var p in w.Players) { p.Energy = 100; p.Injury = null; p.Bans.Clear(); }
                var m = MatchEngine.Play("ENG-1", new Fixture(a.Id, b.Id), false, false, null, ctx);
                total += scored ? m.HomeGoals : m.AwayGoals;
            }
            return total / 600.0;
        }
        double attackFor = Goals(2, true), balancedFor = Goals(0, true), attackAgainst = Goals(2, false), balancedAgainst = Goals(0, false);
        _out.WriteLine($"scored {balancedFor:F2} -> {attackFor:F2}, conceded {balancedAgainst:F2} -> {attackAgainst:F2}");
        Assert.InRange(attackFor / balancedFor, 1.06, 1.30);              // +16% expected
        Assert.InRange(attackAgainst / balancedAgainst, 1.03, 1.25);      // +12% expected
    }

    [Fact]
    public void MatchEnergy_DistancePassesAndShots_NotGoals_LessWithStamina()
    {
        var w = new WorldGenerator(D).Generate(5);
        var ctx = Ctx(w);
        var p = w.Players.First(x => x.MainPosition == Position.CM);
        double Cost(int stamina, int goals, int passes, int shots)
        {
            var vals = p.Attributes.Values; vals[(int)Attr.Stamina] = stamina; p.Attributes.Values = vals;
            p.Energy = 100;
            Fitness.MatchEnergy(p, new PlayerMatch { Position = Position.CM, Minutes = 90, Km = 11.6, Passes = passes, Shots = shots, Goals = goals }, 0, ctx);
            return 100 - p.Energy;
        }
        double baseCost = Cost(50, 0, 55, 1);
        Assert.InRange(baseCost, 20, 26);                                   // 11.6 km x 1.6 + 55 x 0.08 + 1 x 0.4 = 23.4
        Assert.Equal(baseCost, Cost(50, 3, 55, 1), 6);                      // goals don't matter
        Assert.True(Cost(50, 0, 90, 5) > baseCost);                         // being active does
        Assert.True(Cost(90, 0, 55, 1) < baseCost && Cost(20, 0, 55, 1) > baseCost);
    }

    [Fact]
    public void Recovery_LightIsFull_ModerateAndHeavyLess_InjuredRest()
    {
        var w = new WorldGenerator(D).Generate(5);
        var ps = w.Players.Where(p => p.ClubId != null && p.PersonalityId == null).Take(4).ToList();
        var regimes = new[] { "light", "moderate", "heavy", "moderate" };
        for (int i = 0; i < 4; i++) { ps[i].Energy = 30; ps[i].Regime = regimes[i]; }
        ps[3].Injury = new Injury { WeeksLeft = 3, TotalWeeks = 3 };
        Fitness.WeekEnd(w, D, new GameRandom(1), null);
        Assert.Equal(100, ps[0].Energy);
        Assert.Equal(70, ps[1].Energy);
        Assert.Equal(55, ps[2].Energy);
        Assert.Equal(100, ps[3].Energy);
        Assert.InRange(ps[3].Injury.WeeksLeft, 1, 2);
        var work = w.Players.First(p => p.ClubId != null && p.PersonalityId == "PER-WORKHORSE");
        work.Energy = 30; work.Regime = "moderate"; work.Injury = null;
        Fitness.WeekEnd(w, D, new GameRandom(2), null);
        Assert.Equal(80, work.Energy);                                       // 40 x 1.25
    }

    [Fact]
    public void Injuries_HaveTypesAndLengths_TheMedicalBuildingShortensThem()
    {
        var w = new WorldGenerator(D).Generate(5);
        var club = w.Clubs[0]; var p = w.Players.First(x => x.ClubId == club.Id);
        double Average(int level)
        {
            club.Facilities[Facility.MedicalCentre].Level = level; club.Facilities[Facility.MedicalCentre].Condition = 100;
            var ctx = Ctx(w, 9);
            return Enumerable.Range(0, 3000).Average(_ => Fitness.Injure(p, club, ctx).TotalWeeks);
        }
        double low = Average(1), high = Average(10);
        _out.WriteLine($"average injury: Medical level 1 {low:F1} weeks, level 10 {high:F1}");
        Assert.True(high < low * 0.65);
        Assert.Equal(10, D.Injuries.Count);
        Assert.Equal(1.0, D.Injuries.Sum(i => i.Share), 6);
    }

    // ------------------------------------------------------------------ suspensions (each competition's real rules)

    private static int Yellows(Player p, string comp, int count, int roundsPlayed)
    {
        int bans = 0;
        for (int i = 0; i < count; i++) if (Discipline.Yellow(p, comp, roundsPlayed, D) != null) bans++;
        return bans;
    }

    [Fact]
    public void England_5ByMatch19_10ByMatch32_RedBansCoverEveryEnglishCompetition()
    {
        var p = new Player { Id = "P1" };
        Discipline.Yellow(p, "ENG-1", 1, D); Discipline.Yellow(p, "ENG-1", 2, D); Discipline.Yellow(p, "ENG-1", 3, D); Discipline.Yellow(p, "ENG-1", 4, D);
        var b = Discipline.Yellow(p, "ENG-1", 19, D);
        Assert.Equal(1, b.MatchesLeft);
        var late = new Player { Id = "P2" };
        for (int i = 0; i < 4; i++) Discipline.Yellow(late, "ENG-1", 10, D);
        Assert.Null(Discipline.Yellow(late, "ENG-1", 20, D));                 // 5th yellow after match 19: no ban
        for (int i = 0; i < 4; i++) Discipline.Yellow(late, "ENG-1", 25, D);
        Assert.Equal(2, Discipline.Yellow(late, "ENG-1", 30, D).MatchesLeft); // 10th by match 32: 2 matches
        var red = Discipline.Red(new Player(), "ENG-1", true, new GameRandom(1), D);
        Assert.Equal((Discipline.Domestic, "ENG", 1), (red.Scope, red.Key, red.MatchesLeft));
        var q = new Player { Id = "P3" }; q.Bans.Add(red);
        var w = new GameWorld();
        Assert.True(Discipline.IsSuspended(w, q, "ENG-FA", D));               // a league red covers the cups too
        Assert.True(Discipline.IsSuspended(w, q, "ENG-LC", D));
        Assert.False(Discipline.IsSuspended(w, q, "UEFA-CC", D));             // but not Europe
        Assert.False(Discipline.IsSuspended(w, q, "ESP-1", D));
    }

    [Fact]
    public void Spain5_Brazil3_Argentina5_EachCompetitionCountsItsOwn()
    {
        Assert.Equal(2, Yellows(new Player(), "ESP-1", 10, 1));
        Assert.Equal(3, Yellows(new Player(), "BRA-1", 9, 1));
        Assert.Equal(1, Yellows(new Player(), "ARG-1", 5, 1));
        var p = new Player();
        Yellows(p, "ESP-1", 4, 1); Yellows(p, "ESP-CDR", 2, 1);
        Assert.Equal(4, p.Yellows["ESP-1"]);                                  // cup yellows don't count for the league
        var red = Discipline.Red(new Player(), "ESP-1", false, new GameRandom(3), D);
        Assert.Equal((Discipline.Competition, "ESP-1"), (red.Scope, red.Key));   // Spanish league reds stay in the league
        Assert.InRange(red.MatchesLeft, 1, 3);
    }

    [Fact]
    public void Europe_4_6_8_10_WipedAfterTheQuarterFinals_RedsCarryBetweenEuropeanCups()
    {
        var p = new Player();
        Assert.Equal(0, Yellows(p, "UEFA-CC", 3, 1));
        Assert.Equal(1, Yellows(p, "UEFA-CC", 1, 1));                         // 4th
        Assert.Equal(0, Yellows(p, "UEFA-CC", 1, 1));                         // 5th
        Assert.Equal(1, Yellows(p, "UEFA-CC", 1, 1));                         // 6th
        var red = Discipline.Red(new Player(), "UEFA-CC", true, new GameRandom(1), D);
        var q = new Player(); q.Bans.Add(red);
        Assert.True(Discipline.IsSuspended(new GameWorld(), q, "UEFA-EC", D));
        Assert.False(Discipline.IsSuspended(new GameWorld(), q, "ENG-1", D));

        var w = new GameWorld(); w.Players.Add(p);
        var run = new CompetitionRun { CompetitionId = "UEFA-CC" };
        run.PlayedStages.AddRange(new[] { "League phase", "Quarter-final", "Quarter-final" });
        Discipline.BeforeRound(w, run, new Round { Stage = "Semi-final" }, D);
        Assert.False(p.Yellows.ContainsKey("UEFA-CC"));
    }

    [Fact]
    public void Argentina_YellowsWipedForThePlayoffs_SouthAmericaAfterTheGroups()
    {
        var p = new Player(); var w = new GameWorld(); w.Players.Add(p);
        Yellows(p, "ARG-1", 4, 1); Yellows(p, "CONMEBOL-SUD", 2, 1);
        var arg = new CompetitionRun { CompetitionId = "ARG-1" }; arg.PlayedStages.Add("Apertura");
        Discipline.BeforeRound(w, arg, new Round { Stage = "Apertura" }, D);
        Assert.Equal(4, p.Yellows["ARG-1"]);
        Discipline.BeforeRound(w, arg, new Round { Stage = "Apertura playoffs" }, D);
        Assert.False(p.Yellows.ContainsKey("ARG-1"));
        var sud = new CompetitionRun { CompetitionId = "CONMEBOL-SUD" }; sud.PlayedStages.Add("Group stage");
        Discipline.BeforeRound(w, sud, new Round { Stage = "Knockout play-off" }, D);
        Assert.False(p.Yellows.ContainsKey("CONMEBOL-SUD"));
        Yellows(p, "CONMEBOL-SUD", 1, 1);
        sud.PlayedStages.Add("Knockout play-off");
        Discipline.BeforeRound(w, sud, new Round { Stage = "Quarter-final" }, D);
        Assert.Equal(1, p.Yellows["CONMEBOL-SUD"]);                           // wiped once only, after the groups
    }

    [Fact]
    public void BansAreServed_InTheMatchesTheyCover()
    {
        var p = new Player();
        p.Bans.Add(new Ban { Scope = Discipline.Competition, Key = "ENG-1", MatchesLeft = 2 });
        p.Bans.Add(new Ban { Scope = Discipline.Domestic, Key = "ENG", MatchesLeft = 1 });
        Discipline.Serve(new[] { p }, "UEFA-CC", D);
        Assert.Equal(2, p.Bans.Count);
        Discipline.Serve(new[] { p }, "ENG-FA", D);
        Assert.Single(p.Bans);
        Discipline.Serve(new[] { p }, "ENG-1", D); Discipline.Serve(new[] { p }, "ENG-1", D);
        Assert.Empty(p.Bans);
    }

    // ------------------------------------------------------------------ a whole season

    [Fact]
    public void ASeason_RealisticCardsInjuriesGoalsAndRatings_FormAndStatsRecorded()
    {
        var w = new WorldGenerator(D).Generate(2026);
        var cal = new SeasonCalendar(D); var rng = new GameRandom(2031);
        cal.Start(w, rng);
        while (!w.Calendar.SeasonOver) cal.PlayWeek(w, rng);
        var all = w.Calendar.Runs.SelectMany(r => r.PlayedRounds.SelectMany(x => x)).ToList();
        var ev = all.SelectMany(m => m.Events).ToList();
        double teams = 2.0 * all.Count;
        double yellows = ev.Count(e => e.Type == EventType.Yellow) / teams, reds = ev.Count(e => e.Type == EventType.Red || e.Type == EventType.SecondYellow) / teams;
        double injuries = ev.Count(e => e.Type == EventType.Injury) / teams, goals = all.Average(m => m.HomeGoals + m.AwayGoals);
        _out.WriteLine($"per team per match: yellows {yellows:F2}, reds {reds:F3}, injuries {injuries:F2}; goals per match {goals:F2}");
        Assert.InRange(yellows, 1.5, 2.1);
        Assert.InRange(reds, 0.06, 0.15);
        Assert.InRange(injuries, 0.25, 0.5);
        Assert.InRange(goals, 2.4, 3.2);
        Assert.All(all, m => Assert.Equal(m.HomeGoals + m.AwayGoals, m.Events.Count(e => e.Type == EventType.Goal)));
        Assert.All(all, m => Assert.NotNull(m.PlayerOfTheMatch));
        var lines = w.Players.SelectMany(p => p.Stats).ToList();
        double avg = lines.Sum(l => l.RatingSum) / lines.Sum(l => l.Rated);
        Assert.InRange(avg, 6.2, 6.7);                                         // form stays around development's neutral 6.5
        Assert.Equal(all.Sum(m => m.HomeGoals + m.AwayGoals) - ev.Count(e => e.Type == EventType.Goal && e.PlayerId == null), lines.Sum(l => l.Goals));
        Assert.All(w.Players.Where(p => p.RecentMatchRatings.Count > 0), p => Assert.InRange(p.RecentMatchRatings.Count, 1, 10));
        var subs = ev.Count(e => e.Type == EventType.Substitution) / teams;
        Assert.InRange(subs, 3, 5);
        // Every club's subs stay within 5 a match.
        foreach (var m in all) foreach (var club in new[] { m.Home, m.Away })
            Assert.InRange(m.Events.Count(e => e.Type == EventType.Substitution && e.ClubId == club), 0, 5);
    }

    [Fact]
    public void TheUserSetsFormationLineupRegimesAndSavedLineups()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lfc-squad-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = LegendsFC.Core.Saves.GameSession.NewWorld(D, new LegendsFC.Core.Saves.SaveStore(root, D.Saves), "x", 9, "EUR", "t");
            s.Autosave = false;
            s.PickClub(ClubIn(s.World, "ARG-1").Id, "t");
            s.SetFormation("3-5-2");
            var sheet = s.AutoPick("ARG-1");
            Assert.Equal(11, s.UserClub.Tactics.Lineup.Count);
            Assert.Equal("LWB", sheet.Slots[4]);
            s.SetMentality(5);
            Assert.Equal(2, s.UserClub.Tactics.Mentality);
            Assert.True(s.SaveLineup("Cup team"));
            s.SetFormation("4-4-2");
            Assert.Empty(s.UserClub.Tactics.Lineup);                      // new slots: pick again
            Assert.True(s.LoadLineup("Cup team"));
            Assert.Equal("3-5-2", s.UserClub.Tactics.Formation);
            for (int i = 0; i < 5; i++) s.SaveLineup("L" + i);
            Assert.Equal(5, s.UserClub.Tactics.Saved.Count);
            Assert.Throws<ArgumentException>(() => s.SetLineup(s.UserClub.Tactics.Lineup.Take(10).ToList(), new List<string>()));
            Assert.Throws<ArgumentException>(() => s.SetFormation("4-6-0"));
            var someone = s.UserClub.Tactics.Lineup[3];
            s.SetRegime(someone, "light");
            Assert.Equal("light", s.World.Players.First(p => p.Id == someone).Regime);
            Assert.Throws<ArgumentException>(() => s.SetRegime(someone, "extreme"));
            for (int i = 0; i < 12; i++) s.AdvanceWeek("t");
            Assert.Equal("3-5-2", s.UserClub.Tactics.Formation);           // the AI never changes the user's formation
            Assert.Contains(s.World.Players, p => p.ClubId == s.UserClub.Id && p.Stats.Any(st => st.Apps > 0));
        }
        finally { try { System.IO.Directory.Delete(root, true); } catch { } }
    }
}
