using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Season
{
    public sealed class SeasonReport
    {
        public int SeasonStartYear;
        public List<CompetitionOutcome> Outcomes = new List<CompetitionOutcome>();
        public int Retired, AcademyGraduates, Released, FreeAgentSignings;
    }

    /// <summary>
    /// One full season and the move to the next: matches (sim mode), weekly training, promotion and
    /// relegation, ageing, retirements and the academy intake. Endless career = call Advance again.
    /// Interim until those systems exist: no transfers, contracts or per-player form yet (form = neutral).
    /// </summary>
    public sealed class SeasonCycle
    {
        private readonly GameData _d;
        public SeasonCycle(GameData d) { _d = d; }

        public SeasonReport Advance(GameWorld w, GameRandom rng)
        {
            var report = new SeasonReport { SeasonStartYear = w.SeasonStartYear };
            report.Outcomes = new SeasonSimulator(_d).PlaySeason(w, rng);
            Train(w);
            ApplyPromotionAndRelegation(w, report.Outcomes);

            w.SeasonStartYear++;
            foreach (var p in w.Players.Where(p => !p.Retired && w.SeasonStartYear - p.BirthYear >= p.RetireAge))
            {
                p.Retired = true; p.ClubId = null; report.Retired++;
            }

            var gen = new WorldGenerator(_d);
            var c = _d.Development;
            foreach (var club in w.Clubs)
            {
                int intake = rng.NextInt(c.AcademyIntakeMin, c.AcademyIntakeMax);
                gen.AddAcademyIntake(w, club, rng, intake);
                report.AcademyGraduates += intake;
                if (club.Id == w.UserClubId) continue;   // the user's squad is never trimmed or topped up for him
                var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
                int excess = squad.Count - c.MaxSquadSize;
                if (excess <= 0) continue;
                // AI: release the least valuable (rating + half the remaining potential for players 21 and under),
                // keeping at least the AI goalkeeper minimum. PROPOSAL until contracts and transfers exist.
                int year = w.SeasonStartYear;
                foreach (var p in squad.OrderBy(p => Rating(p) + (year - p.BirthYear <= 21 ? 0.5 * System.Math.Max(0, p.Potential - Rating(p)) : 0)))
                {
                    if (excess == 0) break;
                    if (p.MainPosition == Position.GK && squad.Count(x => x.ClubId == club.Id && x.MainPosition == Position.GK) <= c.AiMinGoalkeepers) continue;
                    p.ClubId = null; excess--; report.Released++;
                }
            }
            // Minimum squad of 16 at all times (confirmed Oct 9): clubs below it sign the best free agents.
            // AI clubs sign a goalkeeper first if they are under their goalkeeper minimum.
            var freeAgents = w.Players.Where(p => !p.Retired && p.ClubId == null).OrderByDescending(Rating).ToList();
            foreach (var club in w.Clubs)
            {
                bool ai = club.Id != w.UserClubId;
                // AI balance: below the goalkeeper minimum, sign the best free-agent goalkeeper.
                while (ai && w.Players.Count(p => p.ClubId == club.Id && p.MainPosition == Position.GK) < c.AiMinGoalkeepers)
                {
                    var gk = freeAgents.FirstOrDefault(p => p.MainPosition == Position.GK);
                    if (gk == null) break;
                    if (w.Players.Count(p => p.ClubId == club.Id) >= c.MaxSquadSize)
                    {   // make room: release the weakest outfield player
                        var weakest = w.Players.Where(p => p.ClubId == club.Id && p.MainPosition != Position.GK).OrderBy(Rating).First();
                        weakest.ClubId = null; report.Released++;
                    }
                    gk.ClubId = club.Id; freeAgents.Remove(gk); report.FreeAgentSignings++;
                }
                while (w.Players.Count(p => p.ClubId == club.Id) < c.MinSquadSize && freeAgents.Count > 0)
                {
                    bool needGk = ai && w.Players.Count(p => p.ClubId == club.Id && p.MainPosition == Position.GK) < c.AiMinGoalkeepers;
                    var pick = (needGk ? freeAgents.FirstOrDefault(p => p.MainPosition == Position.GK) : null) ?? freeAgents[0];
                    pick.ClubId = club.Id; freeAgents.Remove(pick); report.FreeAgentSignings++;
                }
            }
            return report;
        }

        private double Rating(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, _d.PositionRatings);

        /// <summary>AI default until coaches exist: one coach per group (GK / defence / midfield / attack), moderate regime.</summary>
        private void Train(GameWorld w)
        {
            var c = _d.Development;
            var arch = _d.Archetypes.ToDictionary(a => a.Id);
            foreach (var club in w.Clubs)
            {
                var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
                var fac = club.Facilities[Facility.TrainingGround];
                int effective = (int)System.Math.Round(fac.Level * (0.6 + 0.4 * fac.Condition / 100));
                var groups = squad.GroupBy(p => CoachGroup(p.MainPosition)).ToDictionary(g => g.Key, g => g.Count());
                for (int week = 0; week < c.TrainingWeeksPerSeason; week++)
                    foreach (var p in squad)
                        Development.TrainWeek(p, w.SeasonStartYear - p.BirthYear, arch[p.ArchetypeId], c.DefaultCoachQuality, c.DefaultRegime,
                            groups[CoachGroup(p.MainPosition)], effective, c.FormNeutral, _d);
            }
        }

        public static string CoachGroup(Position p)
        {
            switch (p)
            {
                case Position.GK: return "GK";
                case Position.CB: case Position.LB: case Position.RB: return "DEF";
                case Position.DM: case Position.CM: case Position.LM: case Position.RM: return "MID";
                default: return "FWD";
            }
        }

        private static void ApplyPromotionAndRelegation(GameWorld w, List<CompetitionOutcome> outcomes)
        {
            var byId = w.Clubs.ToDictionary(c => c.Id);
            foreach (var o in outcomes)
            {
                var comp = w.Competitions.First(c => c.Id == o.CompetitionId);
                foreach (var id in o.Relegated) Move(w, byId[id], comp.CountryId, comp.Level + 1);
                foreach (var id in o.Promoted) Move(w, byId[id], comp.CountryId, comp.Level - 1);
            }
        }

        private static void Move(GameWorld w, Club club, string countryId, int level)
        {
            var target = w.Competitions.First(c => c.CountryId == countryId && c.Level == level && c.Type == CompetitionType.League);
            w.ClubLeague[club.Id] = target.Id;
            club.Division = level;
        }
    }
}
