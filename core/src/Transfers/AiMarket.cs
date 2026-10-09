using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Transfers
{
    /// <summary>
    /// The living market (confirmed Oct 8–9): windows day by day, AI clubs buying for their needs with part of
    /// their money, AI-to-AI deals with the same negotiation rules as the user, bids for listed players (0–5),
    /// occasional bids for unlisted players, AI loans for young fringe players, and a deadline rush.
    /// </summary>
    public static class AiMarket
    {
        public sealed class Need { public string Group; public double MinRating; public double Priority; }

        public static readonly string[] Groups = { "GK", "DEF", "MID", "FWD" };

        // Ratings don't change during a window (training happens in the season), so they're cached per window (not saved).
        private static double R(GameWorld w, Player p, GameData d) => Pricing.CachedRating(w, p, d);

        // ------------------------------------------------------------------ window

        public static void OpenWindow(GameWorld w, string kind, GameRandom rng, GameData d)
        {
            var m = w.Market; var wc = d.Transfers.Windows;
            m.Window = kind; m.WindowDay = 0; m.WindowLength = kind == "winter" ? wc.WinterDays : wc.SummerDays;
            m.SquadsChanged(); m.RatingCache = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var l in m.Listings.Where(l => !l.BidsCreated).ToList()) CreateListingBids(w, l, rng, d);
            foreach (var club in w.Clubs.Where(c => c.Id != w.UserClubId)) ListSurplus(w, club, d);
            Ms(w, "ms:open", sw);
        }

        public static void CloseWindow(GameWorld w, GameData d)
        {
            var m = w.Market;
            m.ReleasedAtClose += ReleaseUnsold(w, d);
            foreach (var t in m.Talks.Where(t => (t.Status == TalkStatus.Open || t.Status == TalkStatus.Pending)
                                               && (t.Kind == TalkKind.Transfer || t.Kind == TalkKind.Loan || t.Kind == TalkKind.Bid)))
                t.Status = TalkStatus.Cancelled;
            // AI clubs rethink their lists every window; a user listing still on next window brings new bids.
            m.Listings.RemoveAll(l => { var p = w.Players.FirstOrDefault(x => x.Id == l.PlayerId); return p == null || p.ClubId == null || p.ClubId != w.UserClubId; });
            foreach (var l in m.Listings) l.BidsCreated = false;
            m.Talks.RemoveAll(t => t.Status != TalkStatus.Open && t.Status != TalkStatus.Pending && t.OpenedDay < m.Day - 60);
            m.Cooldowns.RemoveAll(c => c.UntilDay <= m.Day);
            m.Window = null; m.WindowDay = 0;
        }

        /// <summary>One day of the market. Deadline rush: AI activity ×3 on the last 3 days.</summary>
        public static void AdvanceDay(GameWorld w, GameRandom rng, GameData d)
        {
            var m = w.Market; var t = d.Transfers;
            m.Day++;
            if (!m.WindowOpen) return;
            m.WindowDay++;

            // Bids arrive; bidders answer a plain "no" from the day before.
            foreach (var bid in m.Talks.Where(x => x.Kind == TalkKind.Bid && x.Status == TalkStatus.Pending && x.ArrivesDay <= m.Day)) bid.Status = TalkStatus.Open;
            foreach (var bid in m.Talks.Where(x => x.Kind == TalkKind.Bid && x.Status == TalkStatus.Open && x.AwaitingBidder).ToList())
            {
                bid.AwaitingBidder = false;
                double u = rng.NextDouble();
                if (u < t.Responses.AfterRejectImprove && bid.BidderMax > bid.Bid + 1)
                    bid.Bid = (long)Math.Round(bid.Bid + (bid.BidderMax - bid.Bid) / 2);
                else if (u >= t.Responses.AfterRejectImprove + t.Responses.AfterRejectHold)
                    Market.EndTalks(w, bid, d);   // withdraws
            }

            double mult = m.InDeadlineRush(t.Windows.DeadlineDays) ? t.Windows.DeadlineMultiplier : 1;
            var ai = w.Clubs.Where(c => c.Id != w.UserClubId).ToList();
            Shuffle(ai, rng);
            var swi = System.Diagnostics.Stopwatch.StartNew();
            var index = BuildIndex(w, d);
            Ms(w, "ms:index", swi);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var club in ai)
            {
                if (rng.Chance(Math.Min(1, t.Ai.SearchChancePerDay * mult))) Search(w, club, index, rng, d);
                if (rng.Chance(Math.Min(1, t.Loans.AiLoanChancePerDay * mult))) LoanOut(w, club, rng, d);
            }
            Ms(w, "ms:clubs", sw);
            if (w.UserClubId != null && rng.Chance(t.Bids.UnsolicitedChancePerDay * mult)) UnsolicitedBid(w, rng, d);

            if (m.WindowDay >= m.WindowLength) CloseWindow(w, d);
        }

        /// <summary>Runs a whole window (headless seasons and sims).</summary>
        public static void RunWindow(GameWorld w, string kind, GameRandom rng, GameData d)
        {
            OpenWindow(w, kind, rng, d);
            while (w.Market.WindowOpen) AdvanceDay(w, rng, d);
        }

        // ------------------------------------------------------------------ needs

        /// <summary>
        /// What a club is looking for: a position group below its normal size, a starter more than 3 below the
        /// club's level (average of its best 11), or else an upgrade on its weakest starter. Bigger gaps come first.
        /// </summary>
        public static List<Need> Needs(GameWorld w, Club club, GameData d)
        {
            var a = d.Transfers.Ai;
            var squad = Market.Squad(w, club.Id);
            var needs = new List<Need>();
            if (squad.Count == 0) return needs;
            double level = squad.Select(p => R(w, p, d)).OrderByDescending(x => x).Take(11).Average();
            foreach (var g in Groups)
            {
                var inGroup = squad.Where(p => Season.SeasonCycle.CoachGroup(p.MainPosition) == g).Select(p => R(w, p, d)).OrderByDescending(x => x).ToList();
                int starters = a.StartersPerGroup.TryGetValue(g, out int s) ? s : 1;
                a.ShortGroupMinimum.TryGetValue(g, out int min);
                double weakest = inGroup.Count >= starters ? inGroup[starters - 1] : 0;
                if (inGroup.Count < min)
                    needs.Add(new Need { Group = g, MinRating = Math.Max(weakest - 5, level - 2 * a.WeakStarterGap), Priority = 10 + (min - inGroup.Count) });
                else
                    needs.Add(new Need { Group = g, MinRating = weakest + a.UpgradeMargin, Priority = weakest < level - a.WeakStarterGap ? 5 + level - weakest : level - weakest });
            }
            return needs.OrderByDescending(n => n.Priority).ToList();
        }

        private static Dictionary<string, List<Player>> BuildIndex(GameWorld w, GameData d)
        {
            var idx = Groups.ToDictionary(g => g, g => new List<Player>());
            foreach (var p in w.Players)
                if (!p.Retired && p.LoanFromClubId == null) idx[Season.SeasonCycle.CoachGroup(p.MainPosition)].Add(p);
            foreach (var g in Groups) idx[g] = idx[g].OrderBy(p => R(w, p, d)).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();   // by rating, for targeted searches
            return idx;
        }

        private static int LowerBound(GameWorld w, List<Player> sorted, double rating, GameData d)
        {
            int lo = 0, hi = sorted.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (R(w, sorted[mid], d) < rating) lo = mid + 1; else hi = mid; }
            return lo;
        }

        /// <summary>Wage room counting the players it has listed as already gone (they're leaving this window or released at its end).</summary>
        public static double ProjectedRoom(GameWorld w, Club club, GameData d)
            => Market.WageRoom(w, club, d) + w.Players.Where(p => p.ClubId == club.Id && w.Market.IsListed(p.Id)).Sum(p => (double)p.Wage);

        /// <summary>
        /// End of window: an AI club still above its normal squad size releases the listed players nobody bought
        /// (weakest first), keeping the 16 minimum and 2 goalkeepers (PROPOSAL; like a contract termination).
        /// </summary>
        private static int ReleaseUnsold(GameWorld w, GameData d)
        {
            int n = 0; var a = d.Transfers.Ai;
            foreach (var club in w.Clubs.Where(c => c.Id != w.UserClubId))
            {
                int extra = Squads.Count(w, club.Id) - a.NormalSquadSize;
                if (extra <= 0) continue;
                foreach (var p in w.Players.Where(p => p.ClubId == club.Id && p.LoanFromClubId == null && w.Market.IsListed(p.Id)).OrderBy(p => R(w, p, d)).ToList())
                {
                    if (extra <= 0 || !Squads.CanSell(w, club.Id, d)) break;
                    if (p.MainPosition == Position.GK && w.Players.Count(x => x.ClubId == club.Id && x.MainPosition == Position.GK) <= d.Development.AiMinGoalkeepers) continue;
                    Squads.Leave(w, p); extra--; n++;
                }
            }
            return n;
        }

        private static double Budget(Club c, GameData d) => Math.Max(0, c.Balance * d.Transfers.Ai.BudgetShareOfBalance);

        // ------------------------------------------------------------------ AI selling

        /// <summary>
        /// At the start of a window an AI club lists players it doesn't need (PROPOSAL): everyone beyond its normal squad
        /// size (26, the weakest players over 21 first), and, if its wages are above its expected income, its most
        /// expensive players outside the best 3 until the excess is covered. Listed players are cheaper (importance ×0.85).
        /// </summary>
        public static void ListSurplus(GameWorld w, Club club, GameData d)
        {
            var a = d.Transfers.Ai;
            var squad = Market.Squad(w, club.Id).Where(p => p.LoanFromClubId == null).ToList();
            int extra = squad.Count - a.NormalSquadSize;
            if (extra > 0)
                foreach (var p in squad.Where(p => Pricing.Age(w, p) > d.Transfers.Loans.AiLoanMaxAge && p.MainPosition != Position.GK)
                                       .OrderBy(p => R(w, p, d)).Take(extra))
                    AddListing(w, p);
            double income = ExpectedIncome(w, club, d), bill = Market.WageBill(w, club.Id);
            double excess = bill - a.WageShareBeforeSelling * income;
            if (excess <= 0) return;
            foreach (var p in squad.Where(p => Pricing.SquadRank(w, p, d) > d.Transfers.Importance.KeyPlayers).OrderByDescending(p => p.Wage))
            {
                if (excess <= 0) break;
                AddListing(w, p); excess -= p.Wage;
            }
        }

        private static void AddListing(GameWorld w, Player p)
        {
            if (w.Market.IsListed(p.Id)) return;
            w.Market.Listings.Add(new Listing { PlayerId = p.Id, BidsCreated = true });
        }

        /// <summary>Season income the club can expect at mid-table in its current league.</summary>
        public static double ExpectedIncome(GameWorld w, Club club, GameData d)
        {
            string key = w.MoneyKey(club);
            int teams = w.ClubLeague.Count(x => x.Value != null && x.Value == key);
            if (teams == 0) teams = 1;
            int home = Season.SeasonSimulator.HomeLeagueMatches(key, teams, d);
            return Money.Finance.SeasonIncome(club, key, (teams + 1) / 2, teams, home, d.Finance).Total;
        }

        // ------------------------------------------------------------------ AI buying

        private static void Search(GameWorld w, Club club, Dictionary<string, List<Player>> index, GameRandom rng, GameData d)
        {
            var a = d.Transfers.Ai;
            if (Squads.Count(w, club.Id) >= d.Development.MaxSquadSize) { w.Market.Count("squadFull"); return; }
            w.Market.Count("search");
            var need = Needs(w, club, d).FirstOrDefault();
            if (need == null) { w.Market.Count("noNeed"); return; }
            var pool = index[need.Group];
            // Scouts look at players a little better than what the club has: rating between the need and need + SearchBand.
            int lo = LowerBound(w, pool, need.MinRating, d), hi = LowerBound(w, pool, need.MinRating + a.SearchBand, d);
            if (hi <= lo) { w.Market.Count("emptyBand"); return; }
            double budget = Budget(club, d), room = Market.WageRoom(w, club, d);
            Player best = null; double bestRating = 0;
            for (int i = 0; i < a.CandidatesPerSearch; i++)
            {
                var p = pool[rng.NextInt(lo, hi - 1)];
                if (p.ClubId == club.Id || p.Retired || p.LoanFromClubId != null) continue;
                double r = R(w, p, d);
                if (r < need.MinRating || r <= bestRating) continue;
                var why = Market.CanApproach(w, club, p, d);
                if (why != Blocked.None) { w.Market.Count("blocked:" + why); continue; }
                if (w.Market.Talks.Any(t => t.PlayerId == p.Id && t.BuyerClubId == club.Id && (t.Status == TalkStatus.Open || t.Status == TalkStatus.Pending))) continue;
                double price = p.ClubId == null ? 0 : Pricing.AskingPrice(w, p, d);
                if (price * a.OpeningBidMin > budget) { w.Market.Count("tooExpensive"); continue; }
                if (Pricing.WageDemand(w, club, p, false, d) > room) { w.Market.Count("wageTooHigh"); continue; }
                best = p; bestRating = r;
            }
            if (best == null) { w.Market.Count("noCandidate"); return; }
            if (best.ClubId != null && best.ClubId == w.UserClubId) { CreateBid(w, club, best, unsolicited: true, rng, d); return; }
            Buy(w, club, best, rng, d);
        }

        /// <summary>The AI negotiates with the same rules as the user: opens below the asking price and moves toward it, up to its limit.</summary>
        public static bool Buy(GameWorld w, Club club, Player p, GameRandom rng, GameData d)
        {
            var a = d.Transfers.Ai;
            var talk = Market.OpenSigning(w, club, p, d, out var blocked);
            if (talk == null) return false;
            double budget = Budget(club, d), room = Market.WageRoom(w, club, d);
            double maxFee = Math.Min(budget, talk.ClubDemand * a.MaxOverAsking);
            double maxWage = Math.Min(room, talk.WageDemand * a.MaxOverWageDemand);
            double fee = talk.Kind == TalkKind.FreeAgent ? 0 : Math.Min(maxFee, talk.ClubDemand * rng.Uniform(a.OpeningBidMin, a.OpeningBidMax));
            double wage = Math.Min(talk.WageDemand, maxWage);
            int years = talk.PreferredYears;
            for (int round = 0; round < 8; round++)
            {
                var reply = Market.Offer(w, talk, (long)Math.Round(fee), (long)Math.Max(1, Math.Round(wage)), years, rng, d, out blocked);
                if (reply == Reply.Accepted) { w.Market.Count("buy:done"); return true; }
                if (reply == Reply.Invalid) { w.Market.Count("buy:invalid:" + blocked); talk.Status = TalkStatus.Cancelled; return false; }
                if (reply == Reply.WalkedAway) { w.Market.Count(talk.ClubAgreed ? "buy:playerWalked" : "buy:clubWalked"); return false; }
                bool clubSide = !talk.ClubAgreed;
                double target = clubSide ? (reply == Reply.Countered ? talk.CounterFee : talk.ClubDemand) : (reply == Reply.Countered ? talk.CounterWage : talk.WageDemand);
                double limit = clubSide ? maxFee : maxWage;
                double current = clubSide ? fee : wage;
                double next = reply == Reply.Countered && target <= limit ? target : Math.Min(limit, (current + target) / 2);
                if (next <= current + 1) { w.Market.Count(clubSide ? "buy:feeLimit" : "buy:wageLimit"); Market.EndTalks(w, talk, d); return false; }   // can't go higher: the AI walks away
                if (clubSide) fee = next; else { wage = next; years = talk.PreferredYears; }
            }
            Market.EndTalks(w, talk, d);
            return false;
        }

        // ------------------------------------------------------------------ bids for the user's players

        /// <summary>Listing a player brings 0–5 bids (Carlos, Oct 9): every AI club that needs him, would improve with him and can afford him; the 5 that need him most.</summary>
        public static int CreateListingBids(GameWorld w, Listing l, GameRandom rng, GameData d)
        {
            l.BidsCreated = true;
            var p = Market.PlayerById(w, l.PlayerId);
            if (p.ClubId == null) return 0;
            var b = d.Transfers.Bids;
            string group = Season.SeasonCycle.CoachGroup(p.MainPosition);
            double rating = R(w, p, d), value = Pricing.Value(w, p, d);
            var interested = new List<(Club club, double priority)>();
            foreach (var club in w.Clubs)
            {
                if (club.Id == p.ClubId || club.Id == w.UserClubId) continue;
                if (Market.CanApproach(w, club, p, d) != Blocked.None) continue;
                var need = Needs(w, club, d).FirstOrDefault(n => n.Group == group);
                if (need == null || rating < need.MinRating) continue;
                if (value * b.BidMin > Budget(club, d) || Pricing.WageDemand(w, club, p, false, d) > Market.WageRoom(w, club, d)) continue;
                interested.Add((club, need.Priority + (rating - need.MinRating)));
            }
            int n = 0;
            foreach (var (club, _) in interested.OrderByDescending(x => x.priority).ThenBy(x => x.club.Id, StringComparer.Ordinal).Take(b.MaxBids))
            {
                var t = CreateBid(w, club, p, unsolicited: false, rng, d);
                if (t != null) n++;
            }
            return n;
        }

        private static Talk CreateBid(GameWorld w, Club club, Player p, bool unsolicited, GameRandom rng, GameData d)
        {
            var b = d.Transfers.Bids; var a = d.Transfers.Ai;
            double budget = Budget(club, d);
            double bid, max;
            if (unsolicited)
            {   // an unlisted player: the club values him like the seller does (value × importance × form)
                double ask = Pricing.AskingPrice(w, p, d);
                bid = ask * rng.Uniform(a.OpeningBidMin, a.OpeningBidMax);
                max = ask * a.MaxOverAsking;
            }
            else
            {
                bid = Pricing.Value(w, p, d) * rng.Uniform(b.BidMin, b.BidMax);
                max = bid * rng.Uniform(b.BidderMaxMin, b.BidderMaxMax);
            }
            max = Math.Min(max, budget); bid = Math.Min(bid, max);
            if (bid <= 0) return null;
            var t = new Talk
            {
                Id = ++w.Market.Seq, Kind = TalkKind.Bid, PlayerId = p.Id, BuyerClubId = club.Id, SellerClubId = p.ClubId,
                Status = TalkStatus.Pending, OpenedDay = w.Market.Day, ArrivesDay = w.Market.Day + rng.NextInt(1, b.ArrivalDaysMax),
                Bid = (long)Math.Round(bid), BidderMax = Math.Round(max),
            };
            w.Market.Talks.Add(t);
            return t;
        }

        private static void UnsolicitedBid(GameWorld w, GameRandom rng, GameData d)
        {
            var mine = w.Players.Where(p => p.ClubId == w.UserClubId && p.LoanFromClubId == null && !w.Market.IsListed(p.Id)).ToList();
            if (mine.Count == 0) return;
            var p = mine[rng.NextInt(0, mine.Count - 1)];
            string group = Season.SeasonCycle.CoachGroup(p.MainPosition);
            double rating = R(w, p, d);
            var clubs = w.Clubs.Where(c => c.Id != w.UserClubId && Market.CanApproach(w, c, p, d) == Blocked.None).ToList();
            Shuffle(clubs, rng);
            foreach (var club in clubs.Take(20))
            {
                var need = Needs(w, club, d).FirstOrDefault(n => n.Group == group);
                if (need == null || rating < need.MinRating) continue;
                if (Pricing.AskingPrice(w, p, d) * d.Transfers.Ai.OpeningBidMin > Budget(club, d)) continue;
                if (Pricing.WageDemand(w, club, p, false, d) > Market.WageRoom(w, club, d)) continue;
                CreateBid(w, club, p, unsolicited: true, rng, d);
                return;
            }
        }

        // ------------------------------------------------------------------ AI loans

        /// <summary>AI clubs loan young fringe players (21 and under, outside their best 18) to clubs one division lower in the same country.</summary>
        private static void LoanOut(GameWorld w, Club owner, GameRandom rng, GameData d)
        {
            var lc = d.Transfers.Loans;
            if (owner.Division < 1 || !Squads.CanSell(w, owner.Id, d)) return;
            var young = w.Players.Where(p => p.ClubId == owner.Id && p.LoanFromClubId == null && Pricing.Age(w, p) <= lc.AiLoanMaxAge
                                             && Pricing.SquadRank(w, p, d) > lc.AiLoanOutsideBest).ToList();
            if (young.Count == 0) return;
            var p = young[rng.NextInt(0, young.Count - 1)];
            var borrowers = w.Clubs.Where(c => c.Id != w.UserClubId && c.CountryId == owner.CountryId && c.Division == owner.Division + 1
                                               && Squads.Count(w, c.Id) < d.Development.MaxSquadSize && Market.WageRoom(w, c, d) >= p.Wage).ToList();
            if (borrowers.Count == 0) return;
            var borrower = borrowers[rng.NextInt(0, borrowers.Count - 1)];
            var talk = Market.OpenLoan(w, borrower, p, d, out _);
            if (talk == null) return;
            var reply = Market.OfferLoan(w, talk, 0, 1.0, rng, d, out _);
            if (reply != Reply.Accepted && talk.Status == TalkStatus.Open) talk.Status = TalkStatus.Cancelled;
        }

        private static void Ms(GameWorld w, string key, System.Diagnostics.Stopwatch sw)
        {
            w.Market.Stats.TryGetValue(key, out int n); w.Market.Stats[key] = n + (int)sw.ElapsedMilliseconds;
        }

        private static void Shuffle<T>(List<T> list, GameRandom rng)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.NextInt(0, i); var tmp = list[i]; list[i] = list[j]; list[j] = tmp; }
        }
    }
}
