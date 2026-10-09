using System.Collections.Generic;
using LegendsFC.Core.Model;

namespace LegendsFC.Core.World
{
    /// <summary>A whole save world: everything generated from one seed at world creation.</summary>
    public sealed class GameWorld
    {
        public ulong Seed;
        public int SeasonStartYear = GameInfo.StartSeasonYear;
        public List<Country> Countries = new List<Country>();
        public List<Competition> Competitions = new List<Competition>();
        public List<Club> Clubs = new List<Club>();
        public List<Player> Players = new List<Player>();
        /// <summary>Club id → competition id of its league (null for cup-only clubs).</summary>
        public Dictionary<string, string> ClubLeague = new Dictionary<string, string>();
        /// <summary>The user's club (null until one is picked). Rules never treat it differently, except that the AI's squad management doesn't touch it.</summary>
        public string UserClubId;
        /// <summary>Display currency picked at world creation (Oct 9). Amounts are stored in EUR.</summary>
        public string CurrencyCode = "EUR";
        /// <summary>Transfer market: calendar day, window, negotiations, cooldowns, listings, history.</summary>
        public Transfers.MarketState Market = new Transfers.MarketState();

        /// <summary>League id for league clubs, country id for cup-only clubs (keys of the finance config).</summary>
        public string MoneyKey(Model.Club club) => ClubLeague.TryGetValue(club.Id, out var l) && l != null ? l : club.CountryId;
    }
}
