using System;
using LegendsFC.Core.Rules;

namespace LegendsFC.Core.Money
{
    /// <summary>
    /// Signing and renewal chances (approved Oct 8, v1 with the +0.5 base): logistic of
    /// 0.5 + 2.5 × (wage / expected − 1) + fee fairness + 0.05 × years + bonuses, clamped 5-95%.
    /// </summary>
    public static class Contracts
    {
        public const double AcademyOrExClubBonus = 0.6, DivaRenewalPenalty = -0.5, LoyalRenewalBonus = 0.8, BusinessmanWageFactor = 1.2;

        public static double AcceptChance(double offeredWage, double expectedWage, double feeFairness, int years, double bonus, ProbabilityConfig p)
        {
            double z = 0.5 + 2.5 * (offeredWage / Math.Max(1, expectedWage) - 1) + feeFairness + 0.05 * years + bonus;
            return Probability.Clamp(1 / (1 + Math.Exp(-z)), p);
        }

        /// <summary>Personality effects on a renewal (decisions doc): Loyal easy to renew, Diva harder, Businessman wants more.</summary>
        public static double RenewalChance(string personalityId, double offeredWage, double expectedWage, int years, ProbabilityConfig p)
        {
            double bonus = personalityId == "PER-LOYAL" ? LoyalRenewalBonus : personalityId == "PER-DIVA" ? DivaRenewalPenalty : 0;
            double expected = personalityId == "PER-BUSINESSMAN" ? expectedWage * BusinessmanWageFactor : expectedWage;
            return AcceptChance(offeredWage, expected, 0, years, bonus, p);
        }
    }
}
