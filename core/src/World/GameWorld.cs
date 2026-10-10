using System.Linq;
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
        /// <summary>Players retired for more than a season (history only).</summary>
        public List<ArchivedPlayer> RetiredPlayers = new List<ArchivedPlayer>();

        /// <summary>A player's name, playing or archived (for awards and history screens).</summary>
        public string PlayerName(string id) => Players.FirstOrDefault(p => p.Id == id)?.Name ?? RetiredPlayers.FirstOrDefault(p => p.Id == id)?.Name;
        /// <summary>Every coach: at clubs and in the free pool (Oct 9).</summary>
        public List<Coach> Coaches = new List<Coach>();
        public int CoachSeq;
        /// <summary>Scouts (at the user's club and in the free pool) and the user's reports (Oct 9 night).</summary>
        public List<Scout> Scouts = new List<Scout>();
        public List<ScoutReport> ScoutReports = new List<ScoutReport>();
        public int ScoutSeq, ReportSeq;
        /// <summary>Club id → competition id of its league (null for cup-only clubs).</summary>
        public Dictionary<string, string> ClubLeague = new Dictionary<string, string>();
        /// <summary>The user's club (null until one is picked). Rules never treat it differently, except that the AI's squad management doesn't touch it.</summary>
        public string UserClubId;
        /// <summary>Display currency picked at world creation (Oct 9). Amounts are stored in EUR.</summary>
        public string CurrencyCode = "EUR";
        /// <summary>Transfer market: calendar day, window, negotiations, cooldowns, listings, history.</summary>
        public Transfers.MarketState Market = new Transfers.MarketState();
        /// <summary>The season in progress, week by week.</summary>
        public Season.CalendarState Calendar = new Season.CalendarState();

        /// <summary>Last season's results (tables and titles): continental places and super cups come from them.</summary>
        public List<Season.CompetitionOutcome> LastOutcomes = new List<Season.CompetitionOutcome>();
        /// <summary>Club coefficient: continental points per season, newest last (up to 5 seasons).</summary>
        public Dictionary<string, List<double>> Coefficients = new Dictionary<string, List<double>>();
        /// <summary>Titles won, per club: "Champions Cup 2027/28" style entries (competition id, season start year).</summary>
        public List<Season.TitleRecord> Honours = new List<Season.TitleRecord>();

        /// <summary>The user's career: board, objectives, jobs, reputation, season summaries, decisions (Oct 9 night).</summary>
        public Career.CareerState Career = new Career.CareerState();
        public List<Career.AwardRecord> Awards = new List<Career.AwardRecord>();
        /// <summary>Random events per club (balance and no-repeats).</summary>
        public Dictionary<string, List<Career.EventRecord>> ClubEvents = new Dictionary<string, List<Career.EventRecord>>();

        /// <summary>The user's inbox (manager messages) and what its triggers remember.</summary>
        public Inbox.InboxState Inbox = new Inbox.InboxState();

        /// <summary>League id for league clubs, country id for cup-only clubs (keys of the finance config).</summary>
        public string MoneyKey(Model.Club club) => ClubLeague.TryGetValue(club.Id, out var l) && l != null ? l : club.CountryId;
    }
}
