using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Season;

namespace LegendsFC.Core.Squad
{
    /// <summary>
    /// A match the user played live. Handed to GameSession.AdvanceWeek, it replaces the sim for that one fixture: the score,
    /// scorers and each player's minutes, distance, passes and shots come from here; energy, ratings, form, clean sheets, stats
    /// and bans served follow the same rules as every sim result.
    /// </summary>
    public sealed class PlayedMatch
    {
        public string CompetitionId, HomeClubId, AwayClubId;
        /// <summary>Full score, including any extra time played live.</summary>
        public int HomeGoals, AwayGoals;
        /// <summary>True if the live match already played extra time.</summary>
        public bool ExtraTimePlayed;
        public List<PlayedGoal> Goals = new List<PlayedGoal>();
        /// <summary>Everyone who played, both teams (players from the two team sheets of the fixture).</summary>
        public List<PlayedPlayer> Players = new List<PlayedPlayer>();
    }

    /// <summary>One goal. ClubId is the club the goal counts for; for an own goal the scorer is a player of the other club.</summary>
    public sealed class PlayedGoal
    {
        public int Minute;
        public string ClubId, ScorerId, AssistId;
        public bool OwnGoal;
    }

    /// <summary>One player's live match.</summary>
    public sealed class PlayedPlayer
    {
        public string PlayerId, ClubId;
        public int Minutes, Passes, Shots;
        public double Km;
    }

    /// <summary>
    /// The user's next match (GameSession.NextUserFixture): the fixture, how a draw is settled, and the two team sheets the
    /// match uses, built by the same code as the sim (MatchEngine.TeamSheets), mentality included.
    /// </summary>
    public sealed class UserFixture
    {
        public int Week;
        public string CompetitionId, Stage;
        public Fixture Fixture;
        /// <summary>Knockout: a level result (or level aggregate with FirstLeg) is settled by extra time if ExtraTime, then penalties.</summary>
        public bool Decide, ExtraTime;
        /// <summary>Second leg of a two-legged tie: the first-leg result (its home club is this match's away club).</summary>
        public MatchResult? FirstLeg;
        public TeamSheet HomeSheet, AwaySheet;
        public bool UserIsHome;
        public TeamSheet UserSheet => UserIsHome ? HomeSheet : AwaySheet;
        public TeamSheet OpponentSheet => UserIsHome ? AwaySheet : HomeSheet;
    }

    /// <summary>Checks a played match against the fixture it claims to be. Throws ArgumentException; changes nothing.</summary>
    public static class PlayedMatchCheck
    {
        public static void Validate(PlayedMatch m, UserFixture fx)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (fx == null) throw new ArgumentException("The user's club has no match in the coming week.");
            if (m.CompetitionId != fx.CompetitionId || m.HomeClubId != fx.Fixture.Home || m.AwayClubId != fx.Fixture.Away)
                throw new ArgumentException($"The played match ({m.CompetitionId}: {m.HomeClubId} v {m.AwayClubId}) is not the user's next match ({fx.CompetitionId}: {fx.Fixture.Home} v {fx.Fixture.Away}).");
            if (m.HomeGoals < 0 || m.AwayGoals < 0) throw new ArgumentException("Goals can't be negative.");
            if (m.ExtraTimePlayed && !(fx.Decide && fx.ExtraTime)) throw new ArgumentException("This match has no extra time.");
            if (m.Goals == null || m.Players == null) throw new ArgumentException("Goals and Players are required.");

            var sheetIds = new Dictionary<string, HashSet<string>>
            {
                [fx.Fixture.Home] = new HashSet<string>(fx.HomeSheet.Starters.Concat(fx.HomeSheet.Bench).Where(p => p != null).Select(p => p.Id)),
                [fx.Fixture.Away] = new HashSet<string>(fx.AwaySheet.Starters.Concat(fx.AwaySheet.Bench).Where(p => p != null).Select(p => p.Id)),
            };
            var players = new Dictionary<string, PlayedPlayer>();
            foreach (var p in m.Players)
            {
                if (p == null || p.PlayerId == null) throw new ArgumentException("A player line has no player.");
                if (p.ClubId == null || !sheetIds.TryGetValue(p.ClubId, out var ids)) throw new ArgumentException($"{p.PlayerId}: club {p.ClubId} is not in this match.");
                if (!ids.Contains(p.PlayerId)) throw new ArgumentException($"{p.PlayerId} is not on {p.ClubId}'s team sheet for this match.");
                if (p.Minutes < 0 || p.Passes < 0 || p.Shots < 0 || p.Km < 0 || double.IsNaN(p.Km) || double.IsInfinity(p.Km))
                    throw new ArgumentException($"{p.PlayerId}: minutes, passes, shots and distance can't be negative.");
                if (players.ContainsKey(p.PlayerId)) throw new ArgumentException($"{p.PlayerId} is listed twice.");
                players[p.PlayerId] = p;
            }

            int home = 0, away = 0;
            foreach (var g in m.Goals)
            {
                if (g == null) throw new ArgumentException("Empty goal.");
                if (g.Minute < 0) throw new ArgumentException("A goal minute can't be negative.");
                if (g.ClubId == fx.Fixture.Home) home++;
                else if (g.ClubId == fx.Fixture.Away) away++;
                else throw new ArgumentException($"Goal for {g.ClubId}, who is not in this match.");
                string other = g.ClubId == fx.Fixture.Home ? fx.Fixture.Away : fx.Fixture.Home;
                if (g.ScorerId == null || !players.TryGetValue(g.ScorerId, out var scorer) || scorer.Minutes <= 0)
                    throw new ArgumentException($"Scorer {g.ScorerId} is not in the list of players who played.");
                if (scorer.ClubId != (g.OwnGoal ? other : g.ClubId)) throw new ArgumentException($"Scorer {g.ScorerId} plays for the wrong club for this goal.");
                if (g.AssistId != null)
                {
                    if (g.OwnGoal) throw new ArgumentException("An own goal has no assist.");
                    if (!players.TryGetValue(g.AssistId, out var a) || a.Minutes <= 0) throw new ArgumentException($"Assist {g.AssistId} is not in the list of players who played.");
                    if (a.ClubId != g.ClubId || g.AssistId == g.ScorerId) throw new ArgumentException($"Assist {g.AssistId} is not valid for this goal.");
                }
            }
            if (home != m.HomeGoals || away != m.AwayGoals) throw new ArgumentException($"The goals listed ({home}-{away}) don't match the score ({m.HomeGoals}-{m.AwayGoals}).");
        }
    }
}
