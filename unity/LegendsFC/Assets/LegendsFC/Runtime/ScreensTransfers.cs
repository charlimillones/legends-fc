// Transfers (Oct 10 placeholder): search what your scouting covers, negotiate with a live probability, answer bids.
using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Transfers;
using UnityEngine;
using UnityEngine.UI;

namespace LegendsFC.App
{
    public sealed class TransfersScreen : UIScreen
    {
        private static Position? _pos;
        private static int _age, _rating;
        private static bool _free;
        private static readonly int[] Ages = { 0, 19, 21, 23, 26, 29 }, Ratings = { 0, 55, 60, 65, 70, 75, 80 };

        public override void Build(RectTransform body)
        {
            var w = S.World; var club = S.UserClub;
            var list = UIKit.Scroll(body, 4);
            UIKit.Title(list, "Transfers");
            UIKit.Note(list, w.Market.Window != null ? $"The {w.Market.Window} window is open (day {w.Market.WindowDay} of {w.Market.WindowLength * 7})." : "The transfer window is closed: you can still sign free agents and renew contracts.", w.Market.Window != null ? UIKit.Accent : UIKit.Muted, 24);
            UIKit.Note(list, $"Money {Fmt.Money(club.Balance)}   |   Wage room {Fmt.Money(Market.WageRoom(w, club, D))}", UIKit.Ink, 24);

            // Bids for your players
            var bids = Market.BidsFor(w, club.Id).ToList();
            if (bids.Count > 0)
            {
                UIKit.Title(list, "Bids for your players");
                foreach (var b in bids)
                {
                    var row = UIKit.Row(list, 60, 10, UIKit.Panel2, 20);
                    UIKit.Cell(row, $"{Fmt.Club(b.BuyerClubId)} bid {Fmt.Money(b.Bid)} for {Fmt.Player(b.PlayerId)}", 0, 24);
                    var bid = b;
                    UIKit.Button(row, "Accept", () => { var r = Market.RespondToBid(w, bid, Market.SellerAction.Accept, bid.Bid, S.Rng, D); A.Toast(r == Core.Transfers.Reply.Accepted ? "Sold." : "Couldn't complete the sale."); A.Refresh(); }, UIKit.Accent, 22, 150, 50);
                    UIKit.Button(row, "Ask +20%", () => { var r = Market.RespondToBid(w, bid, Market.SellerAction.Counter, (long)(bid.Bid * 1.2), S.Rng, D); A.Toast(Answer(r)); A.Refresh(); }, UIKit.Panel2, 22, 170, 50);
                    UIKit.Button(row, "Reject", () => { Market.RespondToBid(w, bid, Market.SellerAction.Reject, bid.Bid, S.Rng, D); A.Toast("Rejected: they answer tomorrow."); A.Refresh(); }, UIKit.Panel2, 22, 150, 50);
                }
            }

            // Your talks
            var talks = Market.OpenTalks(w, club.Id).Where(t => t.BuyerClubId == club.Id && t.Kind != TalkKind.Bid).ToList();
            if (talks.Count > 0)
            {
                UIKit.Title(list, "Your talks");
                foreach (var t in talks)
                {
                    int id = t.Id;
                    var row = Ui2.ClickRow(list, 54, () => A.Show(new OfferScreen(id)), UIKit.Panel2);
                    UIKit.Cell(row, $"{Fmt.Player(t.PlayerId)} ({t.Kind})", 0, 24);
                    UIKit.Cell(row, t.Status == TalkStatus.Pending ? "Agreed, arriving soon" : "Open", 300, 22, UIKit.Muted);
                }
            }

            // Search
            UIKit.Title(list, "Search");
            var filters = UIKit.Row(list, 60, 8);
            var positions = new List<Position?> { null }; positions.AddRange(Enum.GetValues(typeof(Position)).Cast<Position>().Select(p => (Position?)p));
            UIKit.Button(filters, "Position: " + (_pos?.ToString() ?? "any"), () => { _pos = positions[(positions.IndexOf(_pos) + 1) % positions.Count]; A.Refresh(); }, UIKit.Panel2, 22, 240, 60);
            UIKit.Button(filters, "Max age: " + (_age == 0 ? "any" : _age.ToString()), () => { _age = Ages[(Array.IndexOf(Ages, _age) + 1) % Ages.Length]; A.Refresh(); }, UIKit.Panel2, 22, 220, 60);
            UIKit.Button(filters, "Min rating: " + (_rating == 0 ? "any" : _rating.ToString()), () => { _rating = Ratings[(Array.IndexOf(Ratings, _rating) + 1) % Ratings.Length]; A.Refresh(); }, UIKit.Panel2, 22, 240, 60);
            UIKit.Button(filters, _free ? "Free agents only" : "All players", () => { _free = !_free; A.Refresh(); }, _free ? UIKit.AccentDark : UIKit.Panel2, 22, 250, 60);

            var found = S.SearchPlayers(_pos, _age == 0 ? (int?)null : _age, _rating == 0 ? (int?)null : _rating, _free)
                .Where(v => v.ClubId != club.Id).OrderByDescending(v => v.RatingHigh).ThenBy(v => v.Age).Take(80).ToList();
            UIKit.Note(list, $"{found.Count} players shown (your Scouting Centre sets which leagues you can see).");
            var head = UIKit.Row(list, 34, 12, null, 20);
            foreach (var (t, wd) in new[] { ("Name", 0f), ("Pos", 80f), ("Age", 70f), ("Club", 300f), ("Rating", 120f), ("Potential", 130f), ("Wage", 130f) })
                UIKit.Cell(head, t, wd, 20, UIKit.Muted);
            foreach (var v in found)
            {
                string id = v.Id;
                var row = Ui2.ClickRow(list, 52, () => A.Show(new PlayerScreen(id)));
                UIKit.Cell(row, v.Name, 0, 22);
                UIKit.Cell(row, v.MainPosition.ToString(), 80, 22, UIKit.Muted);
                UIKit.Cell(row, v.Age.ToString(), 70, 22);
                UIKit.Cell(row, v.ClubId == null ? "Free agent" : Fmt.Club(v.ClubId), 300, 20, v.ClubId == null ? UIKit.Accent : UIKit.Muted);
                UIKit.Cell(row, v.RatingLow == v.RatingHigh ? v.RatingLow.ToString() : $"{v.RatingLow}-{v.RatingHigh}", 120, 22, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Cell(row, v.PotentialLow.HasValue ? $"{v.PotentialLow}-{v.PotentialHigh}" : "?", 130, 22, UIKit.Muted);
                UIKit.Cell(row, Fmt.Money(v.Wage), 130, 20);
            }
        }

        public static string Answer(Core.Transfers.Reply r) => r switch
        {
            Core.Transfers.Reply.Accepted => "Accepted.",
            Core.Transfers.Reply.Countered => "They countered.",
            Core.Transfers.Reply.KeepTalking => "No, but they're still talking.",
            Core.Transfers.Reply.WalkedAway => "They walked away.",
            _ => "Not possible.",
        };
    }

    /// <summary>A negotiation (Oct 9 rules): fee, wage and years set one live probability; meeting the demands closes the deal.</summary>
    public sealed class OfferScreen : UIScreen
    {
        private readonly int _talkId;
        private long _fee, _wage;
        private int _years;
        private bool _init;
        private string _last;

        public OfferScreen(int talkId) { _talkId = talkId; }

        public override void Build(RectTransform body)
        {
            var w = S.World;
            var t = w.Market.Talks.FirstOrDefault(x => x.Id == _talkId);
            var list = UIKit.Scroll(body, 8);
            if (t == null) { UIKit.Note(list, "These talks are over."); return; }
            var p = w.Players.First(x => x.Id == t.PlayerId);
            if (!_init)
            {
                _fee = (long)t.ClubDemand; _wage = (long)t.WageDemand; _years = Math.Max(1, t.PreferredYears); _init = true;
            }
            bool fee = t.Kind == TalkKind.Transfer;
            string what = t.Kind == TalkKind.Renewal ? "New contract" : t.Kind == TalkKind.FreeAgent ? "Free agent" : "Transfer";
            UIKit.Title(list, $"{what}: {p.Name} ({p.MainPosition}, {w.SeasonStartYear - p.BirthYear})");
            if (fee) UIKit.Note(list, $"{Fmt.Club(t.SellerClubId)} ask {Fmt.Money(t.ClubDemand)}{(t.ClubAgreed ? $" - fee agreed at {Fmt.Money(t.AgreedFee)}" : "")}", UIKit.Ink, 26);
            UIKit.Note(list, $"He asks {Fmt.Money(t.WageDemand)} a season for {t.PreferredYears} year{(t.PreferredYears > 1 ? "s" : "")}{(t.PlayerAgreed ? " - terms agreed" : "")}", UIKit.Ink, 26);
            if (t.Status == TalkStatus.Pending || t.Status == TalkStatus.Done) { UIKit.Note(list, "Deal agreed.", UIKit.Accent, 30); Back(list); return; }
            if (t.Status != TalkStatus.Open) { UIKit.Note(list, "These talks are over.", UIKit.Bad, 28); Back(list); return; }

            if (fee) Stepper(list, "Fee", Fmt.Money(_fee), () => _fee = Math.Max(0, _fee - Step(t.ClubDemand)), () => _fee += Step(t.ClubDemand));
            Stepper(list, "Wage a season", Fmt.Money(_wage), () => _wage = Math.Max(1000, _wage - Step(t.WageDemand)), () => _wage += Step(t.WageDemand));
            Stepper(list, "Years", _years.ToString(), () => _years = Math.Max(1, _years - 1), () => _years = Math.Min(5, _years + 1));
            double chance = Market.Chance(t, fee ? _fee : 0, _wage, _years, D);
            UIKit.Note(list, $"Chance they accept: {Math.Round(chance * 100):0}%", chance >= 0.67 ? UIKit.Accent : chance >= 0.33 ? UIKit.Warn : UIKit.Bad, 34);
            if (_last != null) UIKit.Note(list, _last, UIKit.Ink, 26);

            var row = UIKit.Row(list, 70, 10);
            UIKit.Button(row, "Make the offer", () => Offer(t), UIKit.Accent, 28, 320, 70);
            UIKit.Button(row, "End talks", () => { Market.EndTalks(w, t, D); A.Toast("Talks ended."); A.Show(new TransfersScreen()); }, UIKit.Panel2, 24, 220, 70);
            UIKit.Button(row, "Back", () => A.Show(t.Kind == TalkKind.Renewal ? (UIScreen)new SquadScreen() : new TransfersScreen()), UIKit.Panel2, 24, 160, 70);
        }

        private static long Step(double demand) => Math.Max(1000, (long)Math.Round(Math.Max(demand, 20000) * 0.05 / 1000) * 1000);

        private void Stepper(RectTransform list, string label, string value, Action down, Action up)
        {
            var row = UIKit.Row(list, 64, 12, UIKit.Panel, 20);
            UIKit.Cell(row, label, 260, 26, UIKit.Muted);
            UIKit.Button(row, "-", () => { down(); A.Refresh(); }, UIKit.Panel2, 34, 90, 54);
            UIKit.Cell(row, value, 260, 30, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Button(row, "+", () => { up(); A.Refresh(); }, UIKit.Panel2, 34, 90, 54);
        }

        private void Offer(Talk t)
        {
            var w = S.World;
            var r = Market.Offer(w, t, t.Kind == TalkKind.Transfer ? _fee : 0, _wage, _years, S.Rng, D, out var blocked);
            if (r == Core.Transfers.Reply.Invalid) { _last = PlayerScreen.Why(blocked); A.Refresh(); return; }
            _last = TransfersScreen.Answer(r);
            if (r == Core.Transfers.Reply.Countered)
            {
                if (t.CounterFee > 0) _fee = t.CounterFee;
                if (t.CounterWage > 0) _wage = t.CounterWage;
                if (t.CounterYears > 0) _years = t.CounterYears;
                _last += " Their counter is filled in: offer it to close the deal.";
            }
            if (r == Core.Transfers.Reply.Accepted) A.Toast(t.Kind == TalkKind.Renewal ? "Contract renewed." : "Deal agreed.");
            A.Refresh();
        }

        private void Back(RectTransform list) => UIKit.Button(list, "Back", () => A.Show(new TransfersScreen()), UIKit.Panel2, 24, -1, 64);
    }
}
