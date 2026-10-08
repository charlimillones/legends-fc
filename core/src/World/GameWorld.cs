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
    }
}
