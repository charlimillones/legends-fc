using System;
using System.Collections.Generic;
using LegendsFC.Core.Model;

namespace LegendsFC.Core.Money
{
    public sealed class LeagueMoney { public double TvPerClubEur, WageBarBaseEur, WageLevel = 1.0; }

    /// <summary>Expected wage curve: share × reference × (value / reference)^exponent × league wage level (Oct 9).</summary>
    public sealed class WageCurveConfig { public double ShareAtReference = 0.12, ReferenceValueEur = 50_000_000, Exponent = 0.75; }
    public sealed class RenewalConfig { public int AiOfferMaxAge = 32, AiOfferYoungAge = 23, YearsMin = 1, YearsMax = 5; public double AiQualityPercentile = 0.5; }
    public sealed class FanMoodConfig
    {
        public double Win = 2, Draw = 0, Loss = -2, Title = 15, Promotion = 10, Relegation = -20, StarSigning = 5, ImportantSale = -8, AcademyDebut = 1;
        public double MonthlyDriftShare = 0.1, NormalBase = 30, NormalPerReputation = 0.5;
    }

    /// <summary>data/config/finance.json (model agreed Oct 9; numbers are PROPOSALS for balancing).</summary>
    public sealed class FinanceConfig
    {
        public Dictionary<string, LeagueMoney> Leagues = new Dictionary<string, LeagueMoney>();
        public Dictionary<string, LeagueMoney> CupOnly = new Dictionary<string, LeagueMoney>();
        public double TvEqualShare = 0.5, PrizePotShareOfTv = 0.2;
        public double TicketBase = 15, TicketRange = 65, TicketExponent = 1.5, FillBase = 0.6, FillMood = 0.4;
        public double SponsorShareOfTv = 0.4, StoreShareOfTv = 0.15, CommercialRepGrowth = 1.06;
        public Dictionary<string, double> ObjectiveMultipliers = new Dictionary<string, double>();
        public double WageBarRepGrowth = 1.025, UpkeepShareOfIncome = 0.03, StartingBalanceSeasons = 0.5;
        public WageCurveConfig WageCurve = new WageCurveConfig();
        public RenewalConfig Renewal = new RenewalConfig();
        public FanMoodConfig FanMood = new FanMoodConfig();
        public Transfers.FreeAgentConfig FreeAgents = new Transfers.FreeAgentConfig();

        /// <summary>League id for league clubs, country id for cup-only clubs.</summary>
        public LeagueMoney For(string leagueOrCountry) => Leagues.TryGetValue(leagueOrCountry, out var l) ? l : CupOnly[leagueOrCountry];
    }

    public sealed class IncomeBreakdown
    {
        public double Tv, Prize, Gate, Sponsors, Store;
        /// <summary>Cup and continental prize money (Oct 9).</summary>
        public double Cups;
        public double Total => Tv + Prize + Gate + Sponsors + Store + Cups;
    }

    /// <summary>Season income and the wage bar. Same rules for every club.</summary>
    public static class Finance
    {
        /// <param name="position">Final league position (1 = champion). Cup-only clubs: pass position 1 of 1.</param>
        public static IncomeBreakdown SeasonIncome(Club club, string leagueOrCountry, int position, int teams, int homeMatches, FinanceConfig c, Facilities.FacilityConfig fc = null)
        {
            var m = c.For(leagueOrCountry);
            double merit = teams <= 1 ? 1.0 : 2.0 * (teams - position + 1) / (teams + 1); // averages to 1 across the league
            var inc = new IncomeBreakdown
            {
                Tv = m.TvPerClubEur * (c.TvEqualShare + (1 - c.TvEqualShare) * merit),
                Prize = m.TvPerClubEur * c.PrizePotShareOfTv * merit,
                // Commercial money grows faster with reputation (Oct 9): × growth^(reputation − 50).
                Sponsors = m.TvPerClubEur * c.SponsorShareOfTv * Math.Pow(c.CommercialRepGrowth, club.Reputation - 50),
                Store = m.TvPerClubEur * c.StoreShareOfTv * Math.Pow(c.CommercialRepGrowth, club.Reputation - 50) * (0.5 + club.FanMood / 100.0),
            };
            double fill = Math.Min(1.0, c.FillBase + c.FillMood * club.FanMood / 100.0);
            double ticket = c.TicketBase + c.TicketRange * Math.Pow(club.Reputation / 100.0, c.TicketExponent);
            // Facilities (Oct 9): the Stadium level sets the seats, the Club Store level the store income (level 5 in full condition = ×1).
            int seats = fc == null ? club.StadiumCapacity : Facilities.FacilityRules.StadiumCapacity(club, fc);
            if (fc != null) inc.Store *= Facilities.FacilityRules.Effect(club.Facilities[Facility.ClubStore], fc);
            inc.Gate = homeMatches * seats * fill * ticket;
            return inc;
        }

        /// <summary>Wage bar (approved): league base × 1.025^(reputation − 50) × objective.</summary>
        public static double WageBar(Club club, string leagueOrCountry, string objective, FinanceConfig c)
            => c.For(leagueOrCountry).WageBarBaseEur * Math.Pow(c.WageBarRepGrowth, club.Reputation - 50) * c.ObjectiveMultipliers[objective];

        /// <summary>
        /// What a player expects per season (agreed Oct 9): stars ask a smaller share of their value, cheap players a bigger one,
        /// and the same player earns less in a poorer league (league wage level).
        /// </summary>
        public static double ExpectedWage(double marketValueEur, string leagueOrCountry, FinanceConfig c)
        {
            var w = c.WageCurve;
            return w.ShareAtReference * w.ReferenceValueEur * Math.Pow(Math.Max(0, marketValueEur) / w.ReferenceValueEur, w.Exponent) * c.For(leagueOrCountry).WageLevel;
        }

        /// <summary>Money never goes below zero (Carlos, Oct 9). Spending more than the balance is refused.</summary>
        public static bool CanAfford(Club club, double amountEur) => amountEur <= club.Balance;

        /// <summary>Pays an amount if the club can afford it. Returns false (and pays nothing) otherwise.</summary>
        public static bool TrySpend(Club club, double amountEur)
        {
            if (amountEur < 0 || !CanAfford(club, amountEur)) return false;
            club.Balance -= (long)Math.Round(amountEur);
            return true;
        }

        /// <summary>Normal fan mood a club drifts back to, from its reputation.</summary>
        public static double NormalFanMood(Club club, FinanceConfig c) => Math.Min(100, c.FanMood.NormalBase + c.FanMood.NormalPerReputation * club.Reputation);
    }
}
