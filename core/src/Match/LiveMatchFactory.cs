using System;
using System.Collections.Generic;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Squad;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Match
{
    /// <summary>Builds match setups from the real world (team sheets) or test teams of a given rating.</summary>
    public static class LiveMatchFactory
    {
        /// <summary>
        /// A setup from the two team sheets the season would use: each starter's attributes with the out-of-position drop
        /// for his slot applied (OutOfPosition.Evaluate, Oct 8) and his current energy. An empty slot gets a stand-in with
        /// every attribute 30 (as sim mode counts it) and no player id.
        /// </summary>
        public static MatchSetup FromSheets(GameWorld w, GameData d, string competitionId, TeamSheet home, TeamSheet away, MatchSettings settings, ulong seed)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (d == null) throw new ArgumentNullException(nameof(d));
            return new MatchSetup
            {
                CompetitionId = competitionId,
                Home = FromSheet(w, d, home),
                Away = FromSheet(w, d, away),
                Seed = seed,
                Settings = settings ?? new MatchSettings()
            };
        }

        private static TeamSetup FromSheet(GameWorld w, GameData d, TeamSheet sheet)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            var club = w.Clubs.Find(c => c.Id == sheet.ClubId);
            var team = new TeamSetup
            {
                ClubId = sheet.ClubId,
                Name = club?.Name ?? sheet.ClubId,
                Formation = sheet.Formation,
                Mentality = sheet.Mentality,
                Starters = new PlayerSetup[sheet.Slots.Count]
            };
            for (int s = 0; s < sheet.Slots.Count; s++)
            {
                var label = sheet.Slots[s];
                var pos = s < sheet.SlotPositions.Count ? sheet.SlotPositions[s] : Lineups.SlotPosition(label);
                var player = s < sheet.Starters.Count ? sheet.Starters[s] : null;
                if (player == null)
                {
                    var empty = Uniform(30);
                    empty.SlotLabel = label; empty.Position = pos; empty.IsKeeper = pos == Position.GK; empty.Name = "-"; empty.ShirtNumber = s + 1;
                    team.Starters[s] = empty;
                    continue;
                }
                double drop = 0;
                var arch = d.Archetypes.Find(a => a.Id == player.ArchetypeId);
                if (arch != null) drop = OutOfPosition.Evaluate(arch, player.MainPosition, player.Foot, pos, d.PositionRules, d.OutOfPosition).Drop;
                var v = player.Attributes.Values;
                var ps = new PlayerSetup
                {
                    PlayerId = player.Id, Name = player.Name, SlotLabel = label, Position = pos, IsKeeper = pos == Position.GK,
                    Energy = player.Energy, ShirtNumber = s + 1
                };
                SetAttributes(ps, v, drop);
                team.Starters[s] = ps;
            }
            return team;
        }

        private static int Reduce(int v, double drop) => Math.Max(AttributeSet.Min, Math.Min(AttributeSet.Max, (int)Math.Round(v * (1 - drop))));

        private static void SetAttributes(PlayerSetup ps, int[] v, double drop)
        {
            ps.Pace = Reduce(v[(int)Attr.Pace], drop); ps.Acceleration = Reduce(v[(int)Attr.Acceleration], drop);
            ps.Stamina = Reduce(v[(int)Attr.Stamina], drop); ps.Strength = Reduce(v[(int)Attr.Strength], drop);
            ps.Dribbling = Reduce(v[(int)Attr.Dribbling], drop); ps.Passing = Reduce(v[(int)Attr.Passing], drop);
            ps.Crossing = Reduce(v[(int)Attr.Crossing], drop); ps.Shooting = Reduce(v[(int)Attr.Shooting], drop);
            ps.Heading = Reduce(v[(int)Attr.Heading], drop); ps.Tackling = Reduce(v[(int)Attr.Tackling], drop);
            ps.Positioning = Reduce(v[(int)Attr.Positioning], drop); ps.Reflexes = Reduce(v[(int)Attr.Reflexes], drop);
            ps.Handling = Reduce(v[(int)Attr.Handling], drop); ps.RushingOut = Reduce(v[(int)Attr.RushingOut], drop);
            ps.Distribution = Reduce(v[(int)Attr.Distribution], drop);
        }

        private static PlayerSetup Uniform(int x)
        {
            return new PlayerSetup
            {
                Pace = x, Acceleration = x, Stamina = x, Strength = x, Dribbling = x, Passing = x, Crossing = x, Shooting = x,
                Heading = x, Tackling = x, Positioning = x, Reflexes = x, Handling = x, RushingOut = x, Distribution = x, Energy = 100
            };
        }

        /// <summary>
        /// A test team of a given average rating, for headless tests and the quick-match button. Attributes are the rating
        /// plus the position profile in match.json (testTeams.profiles) plus noise; keepers' outfield values and outfield
        /// players' GK values are hidden and low (rating - hiddenDrop). Natural positions only, full energy.
        /// The formation's slots come from match.json (they match squad.json; a test checks it).
        /// </summary>
        public static TeamSetup TestTeam(MatchConfig config, string clubId, string formation, int rating, ulong seed)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!config.Formations.TryGetValue(formation, out var slots)) throw new ArgumentException("Unknown formation " + formation);
            var rng = new GameRandom(seed);
            var tt = config.TestTeams;
            var team = new TeamSetup { ClubId = clubId, Name = clubId, Formation = formation, Mentality = 0, Starters = new PlayerSetup[slots.Count] };
            for (int s = 0; s < slots.Count; s++)
            {
                string label = slots[s].Slot;
                var pos = Lineups.SlotPosition(label);
                bool gk = pos == Position.GK;
                tt.Profiles.TryGetValue(pos.ToString(), out var profile);
                var vals = new int[AttributeSet.Count];
                for (int a = 0; a < AttributeSet.Count; a++)
                {
                    var attr = (Attr)a;
                    bool gkAttr = attr >= Attr.Reflexes;
                    double v = rating + rng.Gaussian(0, tt.Noise);
                    if (gk != gkAttr) v = rating - tt.HiddenDrop + rng.Gaussian(0, tt.Noise);
                    if (profile != null && profile.TryGetValue(attr.ToString(), out var off)) v += off;
                    vals[a] = Math.Max(AttributeSet.Min, Math.Min(AttributeSet.Max, (int)Math.Round(v)));
                }
                var ps = new PlayerSetup
                {
                    PlayerId = clubId + "-P" + (s + 1).ToString("00"), Name = clubId + " " + (s + 1), SlotLabel = label, Position = pos,
                    IsKeeper = gk, Energy = 100, ShirtNumber = s + 1
                };
                SetAttributes(ps, vals, 0);
                team.Starters[s] = ps;
            }
            return team;
        }
    }
}
