using System.Collections.Generic;
using LegendsFC.Core.Squad;

namespace LegendsFC.Core.Match
{
    /// <summary>
    /// Turns a finished live match into the season's PlayedMatch (Oct 11), so a match the user played replaces the sim result for
    /// that fixture through the same code path as every other result. Players who didn't play (0 minutes) are left out.
    /// </summary>
    public static class LiveMatchToSeason
    {
        public static PlayedMatch ToPlayedMatch(this LiveMatchResult r)
        {
            var m = new PlayedMatch
            {
                CompetitionId = r.CompetitionId, HomeClubId = r.HomeClubId, AwayClubId = r.AwayClubId,
                HomeGoals = r.HomeGoals, AwayGoals = r.AwayGoals, ExtraTimePlayed = r.ExtraTimePlayed,
                Goals = new List<PlayedGoal>(), Players = new List<PlayedPlayer>()
            };
            foreach (var g in r.Goals)
                m.Goals.Add(new PlayedGoal { Minute = g.Minute, ClubId = g.Team == 0 ? r.HomeClubId : r.AwayClubId, ScorerId = g.ScorerId, AssistId = g.AssistId, OwnGoal = g.OwnGoal });
            foreach (var p in r.Players)
                if (p.Minutes > 0)
                    m.Players.Add(new PlayedPlayer { PlayerId = p.PlayerId, ClubId = p.Team == 0 ? r.HomeClubId : r.AwayClubId, Minutes = p.Minutes, Passes = p.Passes, Shots = p.Shots, Km = p.Km });
            return m;
        }
    }
}
