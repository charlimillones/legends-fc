using System;
using System.IO;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.World;

// Usage: dotnet run --project dotnet/LegendsFC.Tools -- world-report [seed]
var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "data", "rules"))) dir = dir.Parent;
var data = GameData.LoadFromDirectory(Path.Combine(dir.FullName, "data"));
ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 2026;
if (args.Length > 0 && args[0] == "protege-report")
{
    var g = new WorldGenerator(data);
    var wr = g.Generate(seed);
    Console.WriteLine($"{data.FacilityName(LegendsFC.Core.Model.Facility.Academy)} level | avg potential | avg price (EUR) | price share");
    foreach (var lvl in new[] { 1, 3, 5, 8, 10 })
    {
        var club = wr.Clubs.First();
        club.Facilities[LegendsFC.Core.Model.Facility.Academy].Level = lvl;
        var rr = new LegendsFC.Core.Util.GameRandom((ulong)lvl);
        var rows = Enumerable.Range(0, 200).Select(_ => g.CreateYearlyProtege(wr, club, rr, LegendsFC.Core.Model.Position.CM, null)).ToList();
        double share = Math.Min(data.Protege.PriceShareMax, data.Protege.PriceShareBase + data.Protege.PriceSharePerLevel * lvl);
        Console.WriteLine($"{lvl,13} | {rows.Average(r => r.player.Potential),13:F1} | {rows.Average(r => (double)r.priceEur),15:N0} | {share:P0}");
    }
    return;
}

if (args.Length > 0 && args[0] == "flip-test")
{
    // Approved trade-loop check: buy a player, list him straight away in the same window, sell to the best bidder.
    // Strategy: offer 92% of the asking price (pay the full price if they counter), then counter the best bid at +10%.
    var fw = new WorldGenerator(data).Generate(seed);
    var user = fw.Clubs.First(c => fw.ClubLeague[c.Id] == "ENG-1");
    fw.UserClubId = user.Id; user.Balance = 5_000_000_000;
    var rng = new LegendsFC.Core.Util.GameRandom(seed + 3);
    LegendsFC.Core.Transfers.AiMarket.OpenWindow(fw, "summer", rng, data);
    var results = new System.Collections.Generic.List<double>(); int noBids = 0, failedBuys = 0, tries = 0;
    var pool = fw.Players.Where(p => p.ClubId != null && p.ClubId != user.Id).ToList();
    while (results.Count + noBids < 300 && tries < 3000)
    {
        tries++;
        var p = pool[rng.NextInt(0, pool.Count - 1)];
        if (p.ClubId == null || p.ClubId == user.Id) continue;
        int minYears = args.Length > 2 ? int.Parse(args[2]) : 0, maxYears = args.Length > 3 ? int.Parse(args[3]) : 99;
        int left = p.ContractEndYear - fw.SeasonStartYear;
        if (left < minYears || left > maxYears) continue;
        while (LegendsFC.Core.Transfers.Squads.Count(fw, user.Id) > 20)
        {   // keep room: move someone of ours out for free (not part of the test)
            var x = fw.Players.First(q => q.ClubId == user.Id && q != p); LegendsFC.Core.Transfers.Squads.Leave(fw, x);
        }
        var talk = LegendsFC.Core.Transfers.Market.OpenSigning(fw, user, p, data, out var bl);
        if (talk == null) continue;
        long fee = (long)(talk.ClubDemand * 0.92), wage = (long)Math.Ceiling(talk.WageDemand);
        var r = LegendsFC.Core.Transfers.Market.Offer(fw, talk, fee, wage, talk.PreferredYears, rng, data, out bl);
        if (r == LegendsFC.Core.Transfers.Reply.Countered || r == LegendsFC.Core.Transfers.Reply.KeepTalking)
        { fee = (long)Math.Ceiling(talk.ClubDemand); r = LegendsFC.Core.Transfers.Market.Offer(fw, talk, fee, wage, talk.PreferredYears, rng, data, out bl); }
        if (r != LegendsFC.Core.Transfers.Reply.Accepted) { failedBuys++; continue; }
        LegendsFC.Core.Transfers.Market.List(fw, p, null, rng, data);
        var bids = fw.Market.Talks.Where(t => t.Kind == LegendsFC.Core.Transfers.TalkKind.Bid && t.PlayerId == p.Id && t.Status == LegendsFC.Core.Transfers.TalkStatus.Pending).ToList();
        if (bids.Count == 0) { noBids++; LegendsFC.Core.Transfers.Market.Unlist(fw, p); continue; }
        foreach (var b in bids) b.Status = LegendsFC.Core.Transfers.TalkStatus.Open;
        var topBid = bids.OrderByDescending(b => b.Bid).First();
        long ask = (long)(topBid.Bid * 1.10);
        var sr = LegendsFC.Core.Transfers.Market.RespondToBid(fw, topBid, LegendsFC.Core.Transfers.Market.SellerAction.Counter, ask, rng, data);
        long sale = sr == LegendsFC.Core.Transfers.Reply.Accepted ? ask : 0;
        if (sale == 0)
        {
            var still = bids.Where(b => b.Status == LegendsFC.Core.Transfers.TalkStatus.Open).OrderByDescending(b => b.Bid).FirstOrDefault();
            if (still == null) { noBids++; LegendsFC.Core.Transfers.Market.Unlist(fw, p); continue; }
            sale = still.Bid; LegendsFC.Core.Transfers.Market.RespondToBid(fw, still, LegendsFC.Core.Transfers.Market.SellerAction.Accept, 0, rng, data);
        }
        results.Add(sale / (double)Math.Max(1, fee) - 1);
    }
    var srt = results.OrderBy(x => x).ToList();
    Console.WriteLine($"Flips: {results.Count} sold, {noBids} got no bid, {failedBuys} buys failed");
    Console.WriteLine($"Profit in {results.Count(x => x > 0) * 100.0 / results.Count:F0}% | average {results.Average() * 100:+0.0;-0.0}% | median {srt[srt.Count / 2] * 100:+0;-0}% | worst 10% {srt[srt.Count / 10] * 100:+0;-0}% | best 10% {srt[srt.Count * 9 / 10] * 100:+0;-0}%");
    return;
}
if (args.Length > 0 && args[0] == "wage-room")
{
    var ww = new WorldGenerator(data).Generate(seed);
    var wc = new LegendsFC.Core.Season.SeasonCycle(data); var wr = new LegendsFC.Core.Util.GameRandom(seed + 7);
    for (int s = 0; s < 4; s++)
    {
        var rows = ww.Clubs.Select(c => (c, bar: LegendsFC.Core.Money.Finance.WageBar(c, ww.MoneyKey(c), "standard", data.Finance), bill: LegendsFC.Core.Transfers.Market.WageBill(ww, c.Id),
                                       inc: LegendsFC.Core.Transfers.AiMarket.ExpectedIncome(ww, c, data))).ToList();
        var ratio = rows.Select(r => r.bill / r.bar).OrderBy(x => x).ToList();
        Console.WriteLine($"Season start {ww.SeasonStartYear}: bill/bar p10 {ratio[ratio.Count/10]:P0} median {ratio[ratio.Count/2]:P0} p90 {ratio[ratio.Count*9/10]:P0}; over bar {ratio.Count(x => x > 1)}/{ratio.Count}; bill/income median {rows.Select(r => r.bill / r.inc).OrderBy(x => x).ElementAt(rows.Count/2):P0}; squad avg {ww.Clubs.Average(c => ww.Players.Count(p => p.ClubId == c.Id)):F1}");
        wc.Advance(ww, wr);
    }
    return;
}
if (args.Length > 0 && args[0] == "cups-report")
{
    // Cups over N seasons: entrants, winners, prize money. Usage: cups-report [seed] [seasons]
    int seasons = args.Length > 2 ? int.Parse(args[2]) : 3;
    var cw = new WorldGenerator(data).Generate(seed);
    var cc = new LegendsFC.Core.Season.SeasonCycle(data);
    var cr = new LegendsFC.Core.Util.GameRandom(seed + 11);
    string Nm(string id) => id == null ? "-" : cw.Clubs.First(c => c.Id == id) is var c ? $"{c.Name} ({cw.ClubLeague[c.Id] ?? c.CountryId})" : id;
    var swc = System.Diagnostics.Stopwatch.StartNew();
    for (int s = 0; s < seasons; s++)
    {
        var cal = new LegendsFC.Core.Season.SeasonCalendar(data);
        cal.Start(cw, cr);
        var runs = cw.Calendar.Runs.ToList();
        Console.WriteLine($"=== {cw.SeasonStartYear}/{cw.SeasonStartYear + 1 - 2000}");
        foreach (var run in runs.Where(r => data.Cups.Cups.Any(c => c.Id == r.CompetitionId && c.Kind != LegendsFC.Core.Season.CupKind.Ranking)))
            Console.WriteLine($"  {run.CompetitionId,-13} {run.Clubs.Count,2} clubs, {run.PlannedRounds,2} rounds, weeks {string.Join(",", cw.Calendar.RoundWeeks[run.CompetitionId])}");
        while (!cw.Calendar.SeasonOver) cal.PlayWeek(cw, cr);
        var money = LegendsFC.Core.Season.CupRewards.PrizeMoney(cw.Calendar.Runs, data);
        foreach (var run in cw.Calendar.Runs.Where(r => data.Cups.Cups.Any(c => c.Id == r.CompetitionId)))
        {
            var o = run.Outcome;
            string title = o.Titles.TryGetValue("Winner", out var wnr) ? wnr : o.Titles.TryGetValue("Champion", out var ch) ? ch : null;
            int et = run.PlayedRounds.SelectMany(x => x).Count(x => x.AfterExtraTime), pens = run.PlayedRounds.SelectMany(x => x).Count(x => x.PenaltyWinner != null);
            Console.WriteLine($"  {run.CompetitionId,-13} winner {Nm(title)}, {run.PlayedRounds.Sum(x => x.Count)} matches, {et} extra time, {pens} shootouts" + (title != null && money.TryGetValue(title, out var m) && m > 0 ? $", winner's cup money this season (all cups) {m / 1e6:F1}M" : ""));
        }
        foreach (var lg in new[] { "ENG-1", "BRA-1", "ARG-1" })
        {
            var ids = cw.Clubs.Where(c => cw.ClubLeague[c.Id] == lg).Select(c => c.Id).ToList();
            Console.WriteLine($"  {lg} cup prize money: avg {ids.Average(i => money.TryGetValue(i, out var v) ? v : 0) / 1e6:F1}M, max {ids.Max(i => money.TryGetValue(i, out var v) ? v : 0) / 1e6:F1}M");
        }
        var weekLoad = cw.Calendar.Runs.SelectMany(r => r.PlayedRounds.Select((res, i) => (week: cw.Calendar.RoundWeeks[r.CompetitionId][i], res)))
            .SelectMany(x => x.res.SelectMany(m => new[] { (x.week, m.Home), (x.week, m.Away) }))
            .GroupBy(x => x).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        Console.WriteLine($"  most matches for one club in one week: {weekLoad}");
        cc.EndSeason(cw, cr, cal.Outcomes(cw));
    }
    Console.WriteLine($"{seasons} seasons in {swc.Elapsed.TotalSeconds:F1} s");
    return;
}
if (args.Length > 0 && args[0] == "market-report")
{
    // Transfer market over 10 seasons. Usage: market-report [seed]
    var mw = new WorldGenerator(data).Generate(seed);
    var mc = new LegendsFC.Core.Season.SeasonCycle(data);
    var mr = new LegendsFC.Core.Util.GameRandom(seed + 7);
    double Rt(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, data.PositionRatings);
    double Avg(string league) => mw.Clubs.Where(c => mw.ClubLeague[c.Id] == league)
        .Average(c => mw.Players.Where(p => p.ClubId == c.Id).Select(Rt).OrderByDescending(x => x).Take(11).Average());
    string Mm(double v) => (v / 1e6).ToString("0.0") + "M";
    var sw2 = System.Diagnostics.Stopwatch.StartNew();
    Console.WriteLine("Season  | transfers | loans | free agents | fees total | avg fee | fee/value | renewed | left | ENG-1 XI | ARG-1 XI | ENG-1 cash avg | clubs at 0");
    for (int s = 0; s < 10; s++)
    {
        int hb = mw.Market.History.Count;
        var rep = mc.Advance(mw, mr);
        var deals = mw.Market.History.Skip(hb).Where(h => !h.Loan && !h.FreeAgent).ToList();
        double ratio = deals.Count == 0 ? 0 : deals.Average(h => h.Fee / Math.Max(1, h.ValueAtDeal));
        double eng = mw.Clubs.Where(c => mw.ClubLeague[c.Id] == "ENG-1").Average(c => (double)c.Balance);
        Console.WriteLine($"{rep.SeasonStartYear}/{rep.SeasonStartYear + 1 - 2000} | {rep.Transfers,9} | {rep.Loans,5} | {rep.FreeAgentSignings,11} | {Mm(rep.TransferFees),10} | {Mm(deals.Count == 0 ? 0 : deals.Average(x => (double)x.Fee)),7} | {ratio,9:F2} | {rep.Renewed,7} | {rep.LeftAtContractEnd,4} | {Avg("ENG-1"),8:F1} | {Avg("ARG-1"),8:F1} | {Mm(eng),14} | {rep.ClubsAtZero}");
    }
    Console.WriteLine($"10 seasons in {sw2.Elapsed.TotalSeconds:F1} s");
    foreach (var g in mw.Clubs.GroupBy(c => mw.ClubLeague[c.Id] ?? c.CountryId).OrderBy(g => g.Key))
        Console.WriteLine($"  {g.Key,-6} avg facility level {g.Average(c => c.Facilities.Values.Average(f => f.Level)):F1} (max {g.Max(c => c.Facilities.Values.Average(f => f.Level)):F1}), condition {g.Average(c => c.Facilities.Values.Average(f => f.Condition)):F0}%, spent on facilities {Mm(g.Average(c => (double)c.SpentOnFacilities))} per club, cash {Mm(g.Average(c => (double)c.Balance))}");
    Console.WriteLine(string.Join(", ", mw.Market.Stats.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value)));
    var top = mw.Market.History.Where(h => !h.Loan && !h.FreeAgent).OrderByDescending(h => h.Fee).Take(5);
    Console.WriteLine("Biggest fees: " + string.Join(", ", top.Select(h => Mm(h.Fee))));
    return;
}
if (args.Length > 0 && args[0] == "finance-dump")
{
    // One line per club at world creation: key, rank, teams, reputation, capacity, wage bill, home games, fan mood
    var dw = new WorldGenerator(data).Generate(seed);
    foreach (var g in dw.Clubs.GroupBy(c => dw.ClubLeague[c.Id] ?? c.CountryId))
    {
        var ranked = g.OrderByDescending(c => c.Reputation).ToList();
        int home = LegendsFC.Core.Season.SeasonSimulator.HomeLeagueMatches(g.Key, ranked.Count, data);
        for (int i = 0; i < ranked.Count; i++)
        {
            var c = ranked[i];
            Console.WriteLine($"{g.Key},{i + 1},{ranked.Count},{c.Reputation},{c.StadiumCapacity},{dw.Players.Where(p => p.ClubId == c.Id).Sum(p => (double)p.Wage):F0},{home},{c.FanMood:F1},{string.Join(";", dw.Players.Where(p => p.ClubId == c.Id).Select(p => LegendsFC.Core.Transfers.FreeAgents.MarketValueEur(p, dw.SeasonStartYear, p.ContractEndYear - dw.SeasonStartYear, data).ToString("F0")))}");
        }
    }
    return;
}
if (args.Length > 0 && args[0] == "finance-report")
{
    // Money over 10 seasons, per league (EUR). Usage: finance-report [seed]
    var fw = new WorldGenerator(data).Generate(seed);
    var cycle = new LegendsFC.Core.Season.SeasonCycle(data);
    var frng = new LegendsFC.Core.Util.GameRandom(seed + 1);
    string Key(Club c) => fw.ClubLeague[c.Id] ?? c.CountryId;
    var start = fw.Clubs.ToDictionary(c => c.Id, c => (double)c.Balance);
    var startKey = fw.Clubs.ToDictionary(c => c.Id, Key);
    string M(double v) => (v / 1e6).ToString("0.0") + "M";
    var startBill = fw.Clubs.ToDictionary(c => c.Id, c => fw.Players.Where(p => p.ClubId == c.Id).Sum(p => (double)p.Wage));
    LegendsFC.Core.Season.SeasonReport first = null;
    var history = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, double>>();
    for (int s = 0; s < 10; s++)
    {
        var rep = cycle.Advance(fw, frng);
        first ??= rep;
        history.Add(fw.Clubs.ToDictionary(c => c.Id, c => (double)c.Balance));
        if (s == 9)
        {
            Console.WriteLine($"Season 10 ({rep.SeasonStartYear}/{rep.SeasonStartYear + 1 - 2000}): {rep.Renewed} renewed, {rep.LeftAtContractEnd} left at contract end ({rep.NotOfferedRenewal} not offered, {rep.RefusedRenewal} said no), {rep.FreeAgentSignings} free-agent signings, {rep.ClubsAtZero} clubs at zero");
        }
    }
    Console.WriteLine();
    Console.WriteLine("Season 1, by starting league (averages per club):");
    Console.WriteLine("League | start balance | income Y1 | wages Y1 | wages/income | start balance / wages | balance Y1 | Y5 | Y10 | in debt Y10");
    foreach (var g in fw.Clubs.GroupBy(c => startKey[c.Id]).OrderBy(g => g.Key))
    {
        var ids = g.Select(c => c.Id).ToList();
        double inc = ids.Average(id => first.Income[id].Total), wages = ids.Average(id => first.WageBill[id]);
        Console.WriteLine($"{g.Key,-6} | {M(ids.Average(id => start[id])),13} | {M(inc),9} | {M(wages),8} | {wages / inc,12:P0} | {ids.Average(id => start[id]) / ids.Average(id => startBill[id]),21:F2} | {M(ids.Average(id => history[0][id])),10} | {M(ids.Average(id => history[4][id])),6} | {M(ids.Average(id => history[9][id])),6} | {ids.Count(id => history[9][id] < 0),3}/{ids.Count}");
    }
    Console.WriteLine();
    Console.WriteLine("Wages / income, best vs worst club in each league (season 1):");
    foreach (var g in fw.Clubs.GroupBy(c => startKey[c.Id]).OrderBy(g => g.Key))
    {
        var r = g.Select(c => first.WageBill[c.Id] / first.Income[c.Id].Total).ToList();
        Console.WriteLine($"{g.Key,-6} min {r.Min():P0}  median {r.OrderBy(x => x).ElementAt(r.Count / 2):P0}  max {r.Max():P0}");
    }
    return;
}

var sw = System.Diagnostics.Stopwatch.StartNew();
var w = new WorldGenerator(data).Generate(seed);
sw.Stop();
double R(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, data.PositionRatings);
var byClub = w.Players.GroupBy(p => p.ClubId).ToDictionary(g => g.Key, g => g.ToList());

Console.WriteLine($"Seed {seed}: {w.Clubs.Count} clubs, {w.Players.Count} players, generated in {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"Personalities: {w.Players.Count(p => p.PersonalityId != null) * 100.0 / w.Players.Count:F1}% | A Keepers: {w.Players.Count(p => p.ArchetypeId == "ARC-RARE-A-KEEPER")}");
Console.WriteLine();
Console.WriteLine("League    Clubs  Best club (avg XI)                  Worst club (avg XI)");
foreach (var key in w.ClubLeague.Values.Where(v => v != null).Distinct().OrderBy(v => v))
{
    var clubs = w.Clubs.Where(c => w.ClubLeague[c.Id] == key)
        .Select(c => (c, xi: byClub[c.Id].Select(R).OrderByDescending(x => x).Take(11).Average())).OrderByDescending(t => t.xi).ToList();
    Console.WriteLine($"{key,-9} {clubs.Count,5}  {clubs[0].c.Name,-28} {clubs[0].xi,5:F1}   {clubs[^1].c.Name,-28} {clubs[^1].xi,5:F1}");
}
var best = w.Players.OrderByDescending(R).Take(5);
Console.WriteLine();
Console.WriteLine("Top 5 players:");
foreach (var p in best)
    Console.WriteLine($"  {p.Name,-24} {p.MainPosition,-3} {R(p),5:F1} pot {p.Potential} age {w.SeasonStartYear - p.BirthYear} ({w.Clubs.First(c => c.Id == p.ClubId).Name})");
var kids = w.Players.Where(p => w.SeasonStartYear - p.BirthYear <= 18).OrderByDescending(p => p.Potential).Take(5);
Console.WriteLine("Top 5 academy prospects:");
foreach (var p in kids)
    Console.WriteLine($"  {p.Name,-24} {p.MainPosition,-3} {R(p),5:F1} pot {p.Potential} age {w.SeasonStartYear - p.BirthYear} ({w.Clubs.First(c => c.Id == p.ClubId).Name})");

// Season summary (sim mode, leagues only)
var sim = new LegendsFC.Core.Season.SeasonSimulator(data);
var tables = sim.PlayLeagues(w, new LegendsFC.Core.Util.GameRandom(seed + 1));
Console.WriteLine();
Console.WriteLine("Sim season 2026/27 (leagues only): champion, points, draws");
foreach (var kv in tables.OrderBy(k => k.Key))
{
    var t = kv.Value;
    double draws = t.Sum(r => r.Drawn) / (double)t.Sum(r => r.Played) * 100;
    Console.WriteLine($"  {kv.Key,-6} {w.Clubs.First(c => c.Id == t[0].ClubId).Name,-30} {t[0].Points,3} pts ({t[0].Played} games)  last: {t[^1].Points,3}  draws {draws:F0}%");
}
