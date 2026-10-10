// Sim-mode match (Oct 10): replays the user's simulated match minute by minute from its events (goals, cards, injuries,
// substitutions), with the other results of the round at half time and full time only (decided Oct 8).
// The 3D match will replace this screen when the art and animations are ready.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Season;
using LegendsFC.Core.Squad;
using EventType = LegendsFC.Core.Squad.EventType;
using UnityEngine;
using UnityEngine.UI;

namespace LegendsFC.App
{
    public sealed class MatchScreen : UIScreen
    {
        private readonly List<MatchResult> _matches;
        private readonly int? _endedSeason;
        private int _index;
        private Coroutine _play;
        private float _speed = 1f;
        private bool _skip;

        public MatchScreen(List<MatchResult> matches, int? endedSeason) { _matches = matches; _endedSeason = endedSeason; }

        private Text _score, _clock, _ht;
        private RectTransform _events, _others, _controls;

        public override void Build(RectTransform body)
        {
            var m = _matches[_index];
            var w = S.World;
            string comp = FindCompetition(m, out var round);
            var top = UIKit.Column(body, 6);
            var tr = top; tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1); tr.sizeDelta = new Vector2(0, 230); tr.anchoredPosition = Vector2.zero;
            var compLabel = UIKit.Label(top, comp == null ? "" : Fmt.Competition(comp), 24, UIKit.Muted, TextAnchor.MiddleCenter); UIKit.Size(compLabel, -1, 34);
            _score = UIKit.Label(top, "", 54, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold); UIKit.Size(_score, -1, 90);
            _clock = UIKit.Label(top, "0'", 34, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold); UIKit.Size(_clock, -1, 50);
            var controls = _controls = UIKit.Row(top, 50, 10);
            controls.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            UIKit.Button(controls, "Faster", () => _speed = Mathf.Min(_speed * 2, 8), UIKit.Panel2, 22, 180, 50);
            UIKit.Button(controls, "Skip to full time", () => _skip = true, UIKit.Panel2, 22, 260, 50);

            var left = UIKit.Node("Events", body); left.anchorMin = new Vector2(0, 0); left.anchorMax = new Vector2(0.58f, 1); left.offsetMin = new Vector2(0, 0); left.offsetMax = new Vector2(-10, -246);
            _events = UIKit.Scroll(left, 4);
            var right = UIKit.Node("Others", body); right.anchorMin = new Vector2(0.58f, 0); right.anchorMax = new Vector2(1, 1); right.offsetMin = new Vector2(10, 0); right.offsetMax = new Vector2(0, -246);
            _others = UIKit.Scroll(right, 4);
            _ht = UIKit.Title(_others, "Other results");
            UIKit.Note(_others, "Shown at half time and full time.");
            _play = A.StartCoroutine(Play(m, round));
        }

        public override void OnHide() { if (_play != null) A.StopCoroutine(_play); }

        private string FindCompetition(MatchResult m, out List<MatchResult> round)
        {
            round = null;
            foreach (var run in S.World.Calendar.Runs)
                for (int i = run.PlayedRounds.Count - 1; i >= 0; i--)
                    if (run.PlayedRounds[i].Any(x => x.Home == m.Home && x.Away == m.Away && x.HomeGoals == m.HomeGoals && x.AwayGoals == m.AwayGoals))
                    { round = run.PlayedRounds[i]; return run.CompetitionId; }
            return null;
        }

        private IEnumerator Play(MatchResult m, List<MatchResult> round)
        {
            var events = (m.Events ?? new List<MatchEvent>()).OrderBy(e => e.Minute).ToList();
            int end = m.AfterExtraTime ? 120 : 90;
            int hg = 0, ag = 0, next = 0;
            SetScore(m, hg, ag);
            for (int minute = 0; minute <= end; minute++)
            {
                _clock.text = minute + "'";
                while (next < events.Count && events[next].Minute <= minute)
                {
                    var e = events[next++];
                    if (e.Type == EventType.Goal) { if (e.ClubId == m.Home) hg++; else ag++; SetScore(m, hg, ag); }
                    AddEvent(m, e);
                }
                if (minute == 45 && !_skip)
                {
                    _clock.text = "Half time";
                    ShowOthers(m, round, 45);
                    yield return new WaitForSecondsRealtime(1.2f / _speed);
                }
                if (!_skip) yield return new WaitForSecondsRealtime(0.12f / _speed);
            }
            // Goals in a level knockout tie decided on penalties don't change the score.
            hg = m.HomeGoals; ag = m.AwayGoals; SetScore(m, hg, ag);
            _clock.text = "Full time" + (m.AfterExtraTime ? " (after extra time)" : "") + (m.PenaltyWinner != null ? $" - {Fmt.Club(m.PenaltyWinner)} win on penalties" : "");
            ShowOthers(m, round, 999);
            if (m.PlayerOfTheMatch != null) AddLine($"Player of the match: {Fmt.Player(m.PlayerOfTheMatch)}", UIKit.Accent);
            UIKit.Clear(_controls);
            if (_index + 1 < _matches.Count) UIKit.Button(_controls, "Next match", () => { _index++; _skip = false; A.Refresh(); }, UIKit.Accent, 24, 300, 50);
            else UIKit.Button(_controls, "Done", Done, UIKit.Accent, 24, 300, 50);
        }

        private void Done()
        {
            if (_endedSeason.HasValue) A.Show(new SeasonSummaryScreen());
            else A.Show(new HomeScreen());
        }

        private void SetScore(MatchResult m, int hg, int ag) => _score.text = $"{Fmt.Club(m.Home)}  {hg} - {ag}  {Fmt.Club(m.Away)}";

        private void AddEvent(MatchResult m, MatchEvent e)
        {
            bool mine = e.ClubId == S.World.UserClubId;
            string team = Fmt.Club(e.ClubId);
            string text = e.Type switch
            {
                EventType.Goal => $"GOAL  {Fmt.Player(e.PlayerId)} ({team}){(e.OtherPlayerId != null ? ", assist " + Fmt.Player(e.OtherPlayerId) : "")}",
                EventType.Yellow => $"Yellow card  {Fmt.Player(e.PlayerId)} ({team})",
                EventType.SecondYellow => $"Second yellow, sent off  {Fmt.Player(e.PlayerId)} ({team})",
                EventType.Red => $"Red card  {Fmt.Player(e.PlayerId)} ({team})",
                EventType.Injury => $"Injury  {Fmt.Player(e.PlayerId)} ({team})",
                EventType.Substitution => $"Substitution ({team})  {Fmt.Player(e.OtherPlayerId)} on for {Fmt.Player(e.PlayerId)}",
                _ => e.Type.ToString(),
            };
            Color c = e.Type == EventType.Goal ? (mine ? UIKit.Accent : UIKit.Bad) : e.Type == EventType.Substitution ? UIKit.Muted : e.Type == EventType.Yellow ? UIKit.Warn : UIKit.Ink;
            AddLine($"{e.Minute}'  {text}", c);
        }

        private void AddLine(string text, Color c)
        {
            var row = UIKit.Row(_events, 46, 10, UIKit.Panel, 16);
            UIKit.Cell(row, text, 0, 22, c);
            row.SetAsFirstSibling();   // newest first
        }

        private void ShowOthers(MatchResult mine, List<MatchResult> round, int upTo)
        {
            if (round == null) return;
            for (int i = _others.childCount - 1; i >= 2; i--) Object.Destroy(_others.GetChild(i).gameObject);
            _ht.text = upTo < 999 ? "Other results (half time)" : "Other results (full time)";
            foreach (var r in round.Where(r => !(r.Home == mine.Home && r.Away == mine.Away)))
            {
                int hg = upTo >= 999 ? r.HomeGoals : (r.Events ?? new List<MatchEvent>()).Count(e => e.Type == EventType.Goal && e.Minute <= upTo && e.ClubId == r.Home);
                int ag = upTo >= 999 ? r.AwayGoals : (r.Events ?? new List<MatchEvent>()).Count(e => e.Type == EventType.Goal && e.Minute <= upTo && e.ClubId == r.Away);
                var row = UIKit.Row(_others, 40, 8, UIKit.Panel, 12);
                UIKit.Cell(row, Fmt.Club(r.Home), 0, 20);
                UIKit.Cell(row, $"{hg} - {ag}", 90, 22, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Cell(row, Fmt.Club(r.Away), 0, 20);
            }
        }
    }
}
