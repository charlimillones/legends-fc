using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

/// <summary>Cups and competitions, accepted by Carlos on Oct 9 (competitions-draft.md, data/world/cups.json).</summary>
public class CupTests
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly ITestOutputHelper _out;
    public CupTests(ITestOutputHelper o) { _out = o; }

    // One played season per seed, shared by the tests (a season takes about 2 s).
    private static readonly Dictionary<int, GameWorld> Played = new Dictionary<int, GameWorld>();
    private static GameWorld Season(int seed, int seasons = 1)
    {
        lock (Played)
        {
            int key = seed * 10 + seasons;
            if (Played.TryGetValue(key, out var done)) return done;
            var w = new WorldGenerator(D).Generate((ulong)seed);
            var rng = new GameRandom((ulong)seed + 1);
            var cycle = new SeasonCycle(D); var cal = new SeasonCalendar(D);
            for (int s = 0; s < seasons; s++)
            {
                cal.Start(w, rng);
                while (!w.Calendar.SeasonOver) cal.PlayWeek(w, rng);
                if (s < seasons - 1) cycle.EndSeason(w, rng, cal.Outcomes(w));
            }
            return Played[key] = w;
        }
    }

    private static CompetitionRun Run(GameWorld w, string id) => w.Calendar.Runs.Single(r => r.CompetitionId == id);
    private static CupDef Def(string id) => D.Cups.Cups.Single(c => c.Id == id);
    private static IEnumerable<(string stage, int index, MatchResult m)> Matches(CompetitionRun r)
        => r.PlayedRounds.SelectMany((res, i) => res.Select(m => (r.PlayedStages[i], i, m)));
    private static List<string> ClubsIn(CompetitionRun r, string stage)
        => Matches(r).Where(x => x.stage == stage).SelectMany(x => new[] { x.m.Home, x.m.Away }).Distinct().ToList();
    private static int Division(GameWorld w, string club) => w.Calendar.Seeds[club].Division;

    // ------------------------------------------------------------------ data

    [Fact]
    public void CupData_ReferencesExist_AndWeeksFitTheSeason()
    {
        var leagues = new HashSet<string>(D.Competitions.Select(c => c.Id));
        var cups = new HashSet<string>(D.Cups.Cups.Select(c => c.Id));
        var countries = new HashSet<string>(D.Countries.Select(c => c.Id));
        Assert.Equal(cups.Count, D.Cups.Cups.Count);
        Assert.Empty(leagues.Intersect(cups));
        foreach (var c in D.Cups.Cups)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name), c.Id);
            if (c.CountryId != null) Assert.Contains(c.CountryId, countries);
            foreach (var s in c.Entry.Concat(c.Qualifying))
            {
                if (s.League != null) Assert.True(leagues.Contains(s.League) || cups.Contains(s.League), c.Id + " " + s.League);
                if (s.Title != null) Assert.Contains(s.Title.Split(':')[0], leagues.Concat(cups));
                if (s.Fallback != null && !s.Fallback.StartsWith("confed:")) Assert.True(leagues.Contains(s.Fallback) || cups.Contains(s.Fallback), s.Fallback);
                if (s.Import != null) Assert.Contains(s.Import.Split(':')[0], cups);
                if (s.Country != null) Assert.Contains(s.Country, countries);
            }
            var weeks = c.SpreadWeeks.Count == 2 ? c.SpreadWeeks : c.Weeks;
            Assert.NotEmpty(weeks);
            Assert.All(weeks, wk => Assert.InRange(wk, 1, 52));
            Assert.Equal(weeks.OrderBy(x => x), weeks);
        }
    }

    [Fact]
    public void EverySeason_EveryCupIsPlayedToTheEnd_InItsOwnWeeks()
    {
        foreach (var seasons in new[] { 1, 2 })
        {
            var w = Season(41, seasons);
            foreach (var def in D.Cups.Cups)
            {
                var run = Run(w, def.Id);
                Assert.True(run.Finished, def.Id);
                Assert.True(run.Outcome.Titles.ContainsKey(def.Kind == CupKind.Ranking ? "Champion" : CupFormats.Winner), def.Id);
                if (def.SpreadWeeks.Count == 0) Assert.Equal(def.Weeks, w.Calendar.RoundWeeks[def.Id]);   // the test world fills every listed week
                Assert.Equal(run.PlannedRounds, run.PlayedRounds.Count);
                // Prize stages are real stage names.
                Assert.All(def.Prizes.Stages.Keys, st => Assert.Contains(st, run.PlayedStages));
                // Extra time only happens to decide a tie, so a match still level after it went to penalties.
                // (A second leg can end level on the day and still be decided on aggregate.)
                foreach (var (stage, i, m) in Matches(run).Where(x => x.m.AfterExtraTime))
                {
                    var leg1 = Matches(run).Where(x => x.index < i && x.stage == stage && x.m.Home == m.Away && x.m.Away == m.Home).Select(x => (MatchResult?)x.m).LastOrDefault();
                    int home = m.HomeGoals + (leg1?.AwayGoals ?? 0), away = m.AwayGoals + (leg1?.HomeGoals ?? 0);
                    if (home == away) Assert.NotNull(m.PenaltyWinner); else Assert.Null(m.PenaltyWinner);
                }
            }
        }
    }

    [Fact]
    public void NoClubPlaysMoreThanThreeTimesInAWeek()
    {
        var w = Season(41);
        int max = w.Calendar.Runs.SelectMany(r => r.PlayedRounds.Select((res, i) => (week: w.Calendar.RoundWeeks[r.CompetitionId][i], res)))
            .SelectMany(x => x.res.SelectMany(m => new[] { (x.week, m.Home), (x.week, m.Away) }))
            .GroupBy(x => x).Max(g => g.Count());
        Assert.InRange(max, 1, 3);
    }

    // ------------------------------------------------------------------ domestic cups

    [Fact]
    public void EnglishCup_SecondDivisionStarts_TopDivisionJoinsInTheRoundOf32_DrawsGoToExtraTime()
    {
        var w = Season(41); var run = Run(w, "ENG-FA");
        Assert.Equal(new[] { "Round 1", "Round of 32", "Round of 16", "Quarter-final", "Semi-final", "Final" }, run.PlayedStages);
        var r1 = ClubsIn(run, "Round 1");
        Assert.Equal(24, r1.Count);
        Assert.All(r1, c => Assert.Equal(2, Division(w, c)));
        Assert.Equal(32, ClubsIn(run, "Round of 32").Count);
        Assert.Equal(20, ClubsIn(run, "Round of 32").Count(c => Division(w, c) == 1));
        // No replays: a draw goes to extra time, then penalties.
        Assert.All(Matches(run).Where(x => x.m.PenaltyWinner != null), x => Assert.True(x.m.AfterExtraTime));
        Assert.Contains(Matches(run), x => x.m.AfterExtraTime);
        Assert.True(run.PlayedRounds.Last().Count == 1);
        Assert.Equal(51, w.Calendar.RoundWeeks["ENG-FA"].Last());
    }

    [Fact]
    public void LeagueCup_StraightToPenalties_EuropeanClubsJoinLate_TwoLeggedSemis()
    {
        var w = Season(41); var run = Run(w, "ENG-LC");
        Assert.Equal(new[] { "Round 1", "Round of 32", "Round of 16", "Quarter-final", "Semi-final", "Semi-final", "Final" }, run.PlayedStages);
        Assert.All(Matches(run).Where(x => x.stage != "Semi-final" && x.stage != "Final"), x => Assert.False(x.m.AfterExtraTime));
        Assert.Contains(Matches(run), x => x.stage != "Semi-final" && x.m.PenaltyWinner != null);
        var european = new HashSet<string>(Run(w, "UEFA-CC").Clubs.Concat(Run(w, "UEFA-EC").Clubs));
        var late = run.Pools["Late"];
        Assert.NotEmpty(late);
        Assert.All(late, c => Assert.Contains(c, european));
        Assert.Empty(ClubsIn(run, "Round 1").Intersect(late));
        Assert.Equal(late.Count, ClubsIn(run, "Round of 32").Intersect(late).Count());
    }

    [Fact]
    public void SpanishCup_LowerDivisionAtHome_SuperCupClubsJoinInTheRoundOf32()
    {
        var w = Season(41); var run = Run(w, "ESP-CDR");
        foreach (var (stage, _, m) in Matches(run).Where(x => x.stage.StartsWith("Round")))
            if (Division(w, m.Home) != Division(w, m.Away)) Assert.True(Division(w, m.Home) > Division(w, m.Away), stage);
        var sup = Run(w, "ESP-SUP").Clubs;
        Assert.Equal(4, sup.Count);
        Assert.Equal(sup.OrderBy(x => x), run.Pools["Late"].OrderBy(x => x));
        Assert.Empty(ClubsIn(run, "Round 1").Intersect(sup));
        Assert.Equal(new[] { "Semi-final", "Semi-final" }, run.PlayedStages.Where(s => s == "Semi-final"));
        Assert.Equal(43, w.Calendar.RoundWeeks["ESP-CDR"].Last());
    }

    [Fact]
    public void BrazilianCup_ChampionsCupClubsJoinInTheRoundOf16_TwoLegsFromThere_TwoLeggedFinal()
    {
        var w = Season(41); var run = Run(w, "BRA-CUP");
        Assert.Equal(new[] { "Round 1", "Round 2", "Round of 16", "Round of 16", "Quarter-final", "Quarter-final", "Semi-final", "Semi-final", "Final", "Final" }, run.PlayedStages);
        var lib = new HashSet<string>(Run(w, "CONMEBOL-LIB").Clubs.Where(c => w.Calendar.Seeds[c].CountryId == "BRA"));
        Assert.Equal(lib.OrderBy(x => x), run.Pools["Late"].OrderBy(x => x));
        Assert.Empty(ClubsIn(run, "Round 1").Concat(ClubsIn(run, "Round 2")).Intersect(lib));
        Assert.Equal(16, ClubsIn(run, "Round of 16").Count);
        // Level aggregate: straight to penalties, never extra time.
        Assert.All(Matches(run), x => Assert.False(x.m.AfterExtraTime));
    }

    [Fact]
    public void ArgentineCup_NeutralVenues_NoExtraTime_EveryLeagueClubPlays()
    {
        var w = Season(41); var run = Run(w, "ARG-CUP");
        Assert.Equal(66, run.Clubs.Count);
        Assert.Equal(4, ClubsIn(run, "Round 1").Count);
        Assert.Equal(64, ClubsIn(run, "Round of 64").Count);
        Assert.All(Matches(run), x => Assert.False(x.m.AfterExtraTime));
    }

    // ------------------------------------------------------------------ continental

    [Fact]
    public void ChampionsCup_Qualifying_LeaguePhaseOf12_TopFourSkipThePlayOff_LosersDropToTheEuropaCup()
    {
        var w = Season(41); var cc = Run(w, "UEFA-CC"); var ec = Run(w, "UEFA-EC");
        Assert.Equal(10, cc.Pools["Direct"].Count);
        Assert.Equal(4, cc.Pools["Qualifying"].Count);
        Assert.Equal(new[] { "Qualifying", "Qualifying" }, cc.PlayedStages.Take(2));
        var qualifyingClubs = ClubsIn(cc, "Qualifying");
        var phase = ClubsIn(cc, "League phase");
        Assert.Equal(12, phase.Count);
        Assert.Equal(2, qualifyingClubs.Intersect(phase).Count());
        var losers = qualifyingClubs.Except(phase).ToList();
        Assert.Equal(losers.OrderBy(x => x), ec.Inputs["UEFA-CC:QualifyingLosers"].OrderBy(x => x));
        Assert.Equal(12, ClubsIn(ec, "League phase").Count);
        Assert.Subset(new HashSet<string>(ClubsIn(ec, "League phase")), new HashSet<string>(losers));

        foreach (var run in new[] { cc, ec })
        {
            var table = run.Outcome.Tables["League phase"];
            Assert.All(table, row => Assert.Equal(6, row.Played));
            var lp = Matches(run).Where(x => x.stage == "League phase").Select(x => x.m).ToList();
            Assert.All(table, row => Assert.Equal(3, lp.Count(m => m.Home == row.ClubId)));
            Assert.All(lp, m => Assert.NotEqual(w.Calendar.Seeds[m.Home].CountryId, w.Calendar.Seeds[m.Away].CountryId));   // never a club from its own country
            var top4 = table.Take(4).Select(r => r.ClubId).ToList();
            Assert.Empty(ClubsIn(run, "Knockout play-off").Intersect(top4));
            Assert.Equal(8, ClubsIn(run, "Knockout play-off").Count);
            Assert.Equal(8, ClubsIn(run, "Quarter-final").Count);
            Assert.Subset(new HashSet<string>(ClubsIn(run, "Quarter-final")), new HashSet<string>(top4));
            Assert.True(run.PlayedRounds.Last().Count == 1 && run.PlayedStages.Last() == "Final");
        }
        Assert.Equal(52, w.Calendar.RoundWeeks["UEFA-CC"].Last());
    }

    [Fact]
    public void LeaguePhaseDraw_TwoFromEveryPot_OneHomeOneAway()
    {
        var teams = Enumerable.Range(0, 12).Select(i => "T" + i.ToString("00")).ToList();
        string Country(string t) => new[] { "ENG", "ESP", "ENG", "ESP", "ENG", "ESP", "POR", "ENG", "ESP", "NED", "ENG", "ESP" }[int.Parse(t.Substring(1))];
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var rounds = CupFormats.SwissDraw(teams, 3, Country, new GameRandom(seed));
            Assert.Equal(6, rounds.Count);
            Assert.All(rounds, r => Assert.Equal(12, r.SelectMany(f => new[] { f.Home, f.Away }).Distinct().Count()));
            var all = rounds.SelectMany(r => r).ToList();
            Assert.Equal(36, all.Select(f => string.Join("-", new[] { f.Home, f.Away }.OrderBy(x => x))).Distinct().Count());   // no repeat
            foreach (var t in teams)
                for (int pot = 0; pot < 3; pot++)
                {
                    var inPot = teams.Skip(pot * 4).Take(4).ToList();
                    Assert.Equal(1, all.Count(f => f.Home == t && inPot.Contains(f.Away)));
                    Assert.Equal(1, all.Count(f => f.Away == t && inPot.Contains(f.Home)));
                }
            Assert.All(all, f => Assert.NotEqual(Country(f.Home), Country(f.Away)));
        }
    }

    [Fact]
    public void SouthAmericanCups_Groups_ThirdsAndQualifyingLosersDropDown()
    {
        var w = Season(41); var lib = Run(w, "CONMEBOL-LIB"); var sud = Run(w, "CONMEBOL-SUD");
        Assert.Equal(12, lib.Pools["Direct"].Count);
        Assert.Equal(8, lib.Pools["Qualifying"].Count);
        Assert.Equal(16, ClubsIn(lib, "Group stage").Count);
        Assert.Equal(4, lib.Outcome.Tables.Keys.Count(k => k.StartsWith("Group ")));
        Assert.All(lib.Outcome.Tables.Where(t => t.Key.StartsWith("Group ")), t => Assert.All(t.Value, row => Assert.Equal(6, row.Played)));
        var thirds = lib.Outcome.Tables.Where(t => t.Key.StartsWith("Group ")).Select(t => t.Value[2].ClubId).ToList();
        Assert.Equal(thirds.OrderBy(x => x), sud.Inputs["CONMEBOL-LIB:GroupThirds"].OrderBy(x => x));
        Assert.Subset(new HashSet<string>(ClubsIn(sud, "Knockout play-off")), new HashSet<string>(thirds));
        var qLosers = ClubsIn(lib, "Qualifying").Except(ClubsIn(lib, "Group stage")).ToList();
        Assert.Equal(4, qLosers.Count);
        Assert.Subset(new HashSet<string>(ClubsIn(sud, "Group stage")), new HashSet<string>(qLosers));
        // Libertadores: top 2 of each group in the quarter-finals; level two-legged ties go straight to penalties.
        Assert.Equal(8, ClubsIn(lib, "Quarter-final").Count);
        Assert.All(Matches(lib).Where(x => x.stage != "Final"), x => Assert.False(x.m.AfterExtraTime));
        // South American Cup: group winners wait in the quarter-finals.
        var sudWinners = sud.Outcome.Tables.Where(t => t.Key.StartsWith("Group ")).Select(t => t.Value[0].ClubId).ToList();
        Assert.Empty(ClubsIn(sud, "Knockout play-off").Intersect(sudWinners));
        Assert.Subset(new HashSet<string>(ClubsIn(sud, "Quarter-final")), new HashSet<string>(sudWinners));
    }

    [Fact]
    public void GroupDraw_KeepsCountriesApartWhenPossible()
    {
        var teams = Enumerable.Range(0, 16).Select(i => "T" + i.ToString("00")).ToList();
        string Country(string t) => int.Parse(t.Substring(1)) % 4 == 0 ? "URU" : int.Parse(t.Substring(1)) % 2 == 0 ? "BRA" : "ARG";
        var groups = CupFormats.DrawGroups(teams, 4, Country, new GameRandom(3));
        Assert.All(groups, g => Assert.Equal(4, g.Count));
        Assert.All(groups, g => Assert.Equal(1, g.Count(t => Country(t) == "URU")));
        Assert.All(groups, g => Assert.InRange(g.Count(t => Country(t) == "ARG"), 2, 2));
    }

    [Fact]
    public void NorthAmericanCup_EightClubs_SeededTwoLegs_SingleFinal()
    {
        var w = Season(41); var run = Run(w, "CONCACAF-CC");
        Assert.Equal(8, run.Clubs.Count);
        Assert.Equal(new[] { "Quarter-final", "Quarter-final", "Semi-final", "Semi-final", "Final" }, run.PlayedStages);
        Assert.Equal(4, run.Clubs.Count(c => w.Calendar.Seeds[c].CountryId == "MEX"));
    }

    // ------------------------------------------------------------------ places and super cups

    [Fact]
    public void ACupWinnerAlreadyPlaced_PassesTheSpotToTheNextClubInTheTable()
    {
        var w = new WorldGenerator(D).Generate(8);
        var eng = w.Clubs.Where(c => w.ClubLeague[c.Id] == "ENG-1").OrderBy(c => c.Id).Select(c => c.Id).ToList();
        var table = eng.Select(id => new TableRow { ClubId = id }).ToList();
        w.LastOutcomes = new List<CompetitionOutcome>
        {
            new CompetitionOutcome { CompetitionId = "ENG-1", Tables = { ["League"] = table }, Titles = { ["Champion"] = eng[0] } },
            new CompetitionOutcome { CompetitionId = "ENG-FA", Titles = { ["Winner"] = eng[1], ["RunnerUp"] = eng[12] } },   // already in the Champions Cup
            new CompetitionOutcome { CompetitionId = "ENG-LC", Titles = { ["Winner"] = eng[10] } },
        };
        var e = Qualification.Resolve(w, D);
        Assert.Equal(eng.Take(4), e["UEFA-CC"].Direct.Where(eng.Contains));
        Assert.Equal(new[] { eng[4] }, e["UEFA-CC"].Qualifying.Where(eng.Contains));
        // Europa Cup: the cup winner's spot passes to 6th; the League Cup winner (11th) goes in; then the next in the table (7th).
        Assert.Equal(new[] { eng[5], eng[10], eng[6] }, e["UEFA-EC"].Direct.Where(eng.Contains));
        // English Super Cup: champion v cup winner.
        Assert.Equal(new[] { eng[0], eng[1] }, e["ENG-SUP"].Direct);
    }

    [Fact]
    public void SecondSeason_SuperCupsAndTheIntercontinentalCupUseLastSeasonsWinners()
    {
        var w1 = Season(41);
        string T(string comp, string title) => Run(w1, comp).Outcome.Titles[title];
        var w = Season(41, 2);
        Assert.Equal(new[] { T("UEFA-CC", "Winner"), T("UEFA-EC", "Winner") }.OrderBy(x => x), Run(w, "UEFA-SUP").Clubs.OrderBy(x => x));
        Assert.Equal(new[] { T("CONCACAF-CC", "Winner"), T("CONMEBOL-LIB", "Winner"), T("UEFA-CC", "Winner") }, Run(w, "INT-CUP").Clubs);
        Assert.Contains(T("ENG-1", "Champion"), Run(w, "ENG-SUP").Clubs);
        Assert.Contains(T("ARG-1", "Apertura"), Run(w, "ARG-SUP").Clubs);
        // The cup-only rankings set next season's places: the Portuguese ranking winner is in the Champions Cup.
        Assert.Contains(T("POR-R", "Champion"), Run(w, "UEFA-CC").Pools["Direct"]);
        Assert.Contains(w.Honours, h => h.CompetitionId == "UEFA-CC" && h.Title == "Winner");
    }

    // ------------------------------------------------------------------ money, coefficients, saves

    [Fact]
    public void PrizeMoney_IsPaidAtTheEndOfTheSeason_AndTheChampionsCupWinnerEarnsMost()
    {
        var w = Season(41);
        var money = CupRewards.PrizeMoney(w.Calendar.Runs, D);
        string ccWinner = Run(w, "UEFA-CC").Outcome.Titles["Winner"];
        var ccOnly = CupRewards.PrizeMoney(new[] { Run(w, "UEFA-CC") }, D);
        Assert.InRange(ccOnly[ccWinner], 45e6, 70e6);                          // 15M league phase + stages + wins + 6M winner (about 60M)
        Assert.Equal(money.Values.Max(), money.Values.OrderByDescending(x => x).First());
        var libWinner = Run(w, "CONMEBOL-LIB").Outcome.Titles["Winner"];
        Assert.InRange(CupRewards.PrizeMoney(new[] { Run(w, "CONMEBOL-LIB") }, D)[libWinner], 18e6, 27e6);
        _out.WriteLine($"Cup money paid this season: {money.Values.Sum() / 1e6:F0}M to {money.Count} clubs");

        var copy = JsonConvert.DeserializeObject<GameWorld>(JsonConvert.SerializeObject(w));
        var before = copy.Clubs.ToDictionary(c => c.Id, c => c.Balance);
        var report = new SeasonCycle(D).EndSeason(copy, new GameRandom(1), new SeasonCalendar(D).Outcomes(copy));
        Assert.Equal(money.Values.Sum(), report.Income.Values.Sum(i => i.Cups), 0);
        Assert.Equal(money[ccWinner], report.Income[ccWinner].Cups, 0);
    }

    [Fact]
    public void Coefficients_KeepTheLastFiveSeasons_AndSeedTheDraws()
    {
        var w = Season(41, 2);
        Assert.NotEmpty(w.Coefficients);
        Assert.All(w.Coefficients.Values, l => Assert.InRange(l.Count, 1, 5));
        string ccWinner = Season(41).Calendar.Runs.Single(r => r.CompetitionId == "UEFA-CC").Outcome.Titles["Winner"];
        Assert.True(w.Coefficients[ccWinner].Sum() >= 10);
        Assert.True(w.Calendar.Seeds[ccWinner].Score >= w.Coefficients[ccWinner].Sum());
    }

    [Theory]
    [InlineData(7)]    // between the qualifying legs and the leagues starting
    [InlineData(28)]   // the 3rd-placed clubs have dropped into the South American Cup, its play-off not yet drawn
    [InlineData(45)]
    public void AHalfPlayedCupSeasonSurvivesASave(int saveWeek)
    {
        string Finish(bool save)
        {
            var w = new WorldGenerator(D).Generate(12);
            var cal = new SeasonCalendar(D); var rng = new GameRandom(13);
            cal.Start(w, rng);
            for (int i = 0; i < saveWeek; i++) cal.PlayWeek(w, rng);
            if (save) w.Calendar = JsonConvert.DeserializeObject<CalendarState>(JsonConvert.SerializeObject(w.Calendar));
            while (!w.Calendar.SeasonOver) cal.PlayWeek(w, rng);
            return string.Join("|", w.Calendar.Runs.Select(r => r.CompetitionId + ":" + string.Join(",", r.Outcome.Titles.Select(t => t.Key + "=" + t.Value))
                + ":" + string.Join(",", r.PlayedRounds.SelectMany(x => x).Select(m => m.Home + m.HomeGoals + m.AwayGoals + m.Away + m.PenaltyWinner))));
        }
        Assert.Equal(Finish(false), Finish(true));
    }
}
