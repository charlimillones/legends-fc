using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Util;

namespace LegendsFC.Core.Season
{
    public struct Fixture
    {
        public string Home, Away;
        public Fixture(string home, string away) { Home = home; Away = away; }
    }

    public static class Fixtures
    {
        /// <summary>Round-robin by the circle method. Every club plays once per round (a bye if the count is odd).
        /// The second leg mirrors the first with home and away swapped.</summary>
        public static List<List<Fixture>> RoundRobin(IList<string> clubIds, int legs, GameRandom rng)
        {
            var teams = clubIds.ToList();
            for (int i = teams.Count - 1; i > 0; i--) { int j = rng.NextInt(0, i); (teams[i], teams[j]) = (teams[j], teams[i]); }
            var index = new Dictionary<string, int>();
            for (int i = 0; i < teams.Count; i++) index[teams[i]] = i;
            if (teams.Count % 2 == 1) teams.Add(null); // bye
            int n = teams.Count, rounds = n - 1;
            var first = new List<List<Fixture>>();
            for (int r = 0; r < rounds; r++)
            {
                var round = new List<Fixture>();
                for (int i = 0; i < n / 2; i++)
                {
                    string a = teams[i], b = teams[n - 1 - i];
                    if (a == null || b == null) continue;
                    // Parity rule: every club gets (n-1)/2 home games, give or take one.
                    int ia = index[a], ib = index[b];
                    string lo = ia < ib ? a : b, hi = ia < ib ? b : a;
                    round.Add((ia + ib) % 2 == 0 ? new Fixture(lo, hi) : new Fixture(hi, lo));
                }
                first.Add(round);
                var last = teams[n - 1];                    // rotate all but the first
                teams.RemoveAt(n - 1);
                teams.Insert(1, last);
            }
            var all = new List<List<Fixture>>(first);
            for (int leg = 1; leg < legs; leg++)
                all.AddRange(first.Select(round => round.Select(f => leg % 2 == 1 ? new Fixture(f.Away, f.Home) : f).ToList()));
            return all;
        }
    }
}
