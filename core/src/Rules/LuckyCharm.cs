using System;

namespace LegendsFC.Core.Rules
{
    /// <summary>Team luck from Lucky Charm players. Each extra one adds 20% less: 10%, 18%, 24.4% ... never reaches 50% (confirmed Oct 7).</summary>
    public static class LuckyCharm
    {
        public static double TeamLuck(int charms, LuckyCharmConfig c)
        {
            if (charms <= 0) return 0;
            return c.First / (1 - c.Decay) * (1 - Math.Pow(c.Decay, charms));
        }
    }
}
