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
        public int Retired, AcademyGraduates, Released;
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
            foreach (var club in w.Clubs)
            {
                gen.AddAcademyIntake(w, club, rng, _d.Development.AcademyIntakePerSeason);
                report.AcademyGraduates += _d.Development.AcademyIntakePerSeason;
                var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
                int excess = squad.Count - _d.Development.MaxSquadSize;
                if (excess <= 0) continue;
                // Release the least valuable (rating, plus half the remaining potential for players 21 and under),
                // but never below 3 goalkeepers. PROPOSAL until contracts and free agents exist.
                int year = w.SeasonStartYear;
                var releasable = squad.OrderBy(p => Rating(p) + (year - p.BirthYear <= 21 ? 0.5 * System.Math.Max(0, p.Potential - Rating(p)) : 0)).ToList();
                foreach (var p in releasable)
                {
                    if (excess == 0) break;
                    if (p.MainPosition == Position.GK && squad.Count(x => x.ClubId == club.Id && x.MainPosition == Position.GK) <= 3) continue;
                    p.ClubId = null; excess--; report.Released++;
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
