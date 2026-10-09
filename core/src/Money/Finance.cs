using System;
using System.Collections.Generic;
using LegendsFC.Core.Model;

namespace LegendsFC.Core.Money
{
    public sealed class LeagueMoney { public double TvPerClubEur, WageBarBaseEur; }
    public sealed class RenewalConfig { public int AiOfferMaxAge = 32, AiOfferYoungAge = 23, YearsMin = 1, YearsMax = 5; }
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
        public double SponsorShareOfTv = 0.4, StoreShareOfTv = 0.15;
        public Dictionary<string, double> ObjectiveMultipliers = new Dictionary<string, double>();
        public double WageBarRepGrowth = 1.025, ExpectedWageShareOfValue = 0.12, UpkeepShareOfIncome = 0.03, StartingBalanceSeasons = 0.5;
        public RenewalConfig Renewal = new RenewalConfig();
        public FanMoodConfig FanMood = new FanMoodConfig();
        public Transfers.FreeAgentConfig FreeAgents = new Transfers.FreeAgentConfig();

        /// <summary>League id for league clubs, country id for cup-only clubs.</summary>
        public LeagueMoney For(string leagueOrCountry) => Leagues.TryGetValue(leagueOrCountry, out var l) ? l : CupOnly[leagueOrCountry];
    }

    public sealed class IncomeBreakdown
    {
        public double Tv, Prize, Gate, Sponsors, Store;
        public double Total => Tv + Prize + Gate + Sponsors + Store;
    }

    /// <summary>Season income and the wage bar. Same rules for every club.</summary>
    public static class Finance
    {
        /// <param name="position">Final league position (1 = champion). Cup-only clubs: pass position 1 of 1.</param>
        public static IncomeBreakdown SeasonIncome(Club club, string leagueOrCountry, int position, int teams, int homeMatches, FinanceConfig c)
        {
            var m = c.For(leagueOrCountry);
            double merit = teams <= 1 ? 1.0 : 2.0 * (teams - position + 1) / (teams + 1); // averages to 1 across the league
            var inc = new IncomeBreakdown
            {
                Tv = m.TvPerClubEur * (c.TvEqualShare + (1 - c.TvEqualShare) * merit),
                Prize = m.TvPerClubEur * c.PrizePotShareOfTv * merit,
                Sponsors = m.TvPerClubEur * c.SponsorShareOfTv * (0.5 + club.Reputation / 100.0),
                Store = m.TvPerClubEur * c.StoreShareOfTv * (club.Reputation / 50.0) * (0.5 + club.FanMood / 100.0),
            };
            double fill = Math.Min(1.0, c.FillBase + c.FillMood * club.FanMood / 100.0);
            double ticket = c.TicketBase + c.TicketRange * Math.Pow(club.Reputation / 100.0, c.TicketExponent);
            inc.Gate = homeMatches * club.StadiumCapacity * fill * ticket;
            return inc;
        }

        /// <summary>Wage bar (approved): league base × 1.025^(reputation − 50) × objective.</summary>
        public static double WageBar(Club club, string leagueOrCountry, string objective, FinanceConfig c)
            => c.For(leagueOrCountry).WageBarBaseEur * Math.Pow(c.WageBarRepGrowth, club.Reputation - 50) * c.ObjectiveMultipliers[objective];

        public static double ExpectedWage(double marketValueEur, FinanceConfig c) => marketValueEur * c.ExpectedWageShareOfValue;

        /// <summary>Normal fan mood a club drifts back to, from its reputation.</summary>
        public static double NormalFanMood(Club club, FinanceConfig c) => Math.Min(100, c.FanMood.NormalBase + c.FanMood.NormalPerReputation * club.Reputation);
    }
}
