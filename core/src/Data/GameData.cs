using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LegendsFC.Core.Data
{
    /// <summary>
    /// All rule data and tuning constants. Built from JSON text so Unity (TextAssets/StreamingAssets)
    /// and dotnet (files) share the same loader.
    /// </summary>
    public sealed class GameData
    {
        public List<Archetype> Archetypes = new List<Archetype>();
        public List<Personality> Personalities = new List<Personality>();
        public PositionRules PositionRules = new PositionRules();
        public PositionRatingConfig PositionRatings = new PositionRatingConfig();
        public OutOfPositionConfig OutOfPosition = new OutOfPositionConfig();
        public ProbabilityConfig Probability = new ProbabilityConfig();
        public LuckyCharmConfig LuckyCharm = new LuckyCharmConfig();
        public PersonalityConfig PersonalitySettings = new PersonalityConfig();
        public List<Country> Countries = new List<Country>();
        public List<Competition> Competitions = new List<Competition>();
        public World.WorldGenConfig WorldGen = new World.WorldGenConfig();
        public World.NameBank Names = new World.NameBank();
        public Season.MatchSimConfig MatchSim = new Season.MatchSimConfig();
        public Season.LeagueFormatConfig LeagueFormats = new Season.LeagueFormatConfig();
        public Season.DevelopmentConfig Development = new Season.DevelopmentConfig();
        public Rules.MarketValueConfig MarketValue = new Rules.MarketValueConfig();
        public World.ProtegeConfig Protege = new World.ProtegeConfig();
        public Money.FinanceConfig Finance = new Money.FinanceConfig();
        public Money.CurrencyConfig Currencies = new Money.CurrencyConfig();
        public Transfers.TransferConfig Transfers = new Transfers.TransferConfig();
        public List<FacilityInfo> Facilities = new List<FacilityInfo>();
        public Season.CalendarConfig Calendar = new Season.CalendarConfig();
        public LegendsFC.Core.Facilities.FacilityConfig FacilityRules = new LegendsFC.Core.Facilities.FacilityConfig();
        /// <summary>data/world/cups.json: domestic, super and continental cups (Oct 9).</summary>
        public Season.CupData Cups = new Season.CupData();
        public Saves.SaveConfig Saves = new Saves.SaveConfig();
        public Inbox.MessageBank Messages = new Inbox.MessageBank();
        public Inbox.InboxConfig InboxRules = new Inbox.InboxConfig();

        public Archetype Archetype(string id) => Archetypes.First(a => a.Id == id);
        public Personality Personality(string id) => Personalities.First(p => p.Id == id);
        /// <summary>The name players see for a facility (e.g. TrainingGround → "Training Grounds").</summary>
        public string FacilityName(Facility f) => Facilities.First(x => x.Id == f).Name;

        /// <summary>Files the loader expects, relative to the data root.</summary>
        public static readonly string[] Files =
        {
            "rules/archetypes.json", "rules/personalities.json", "rules/positions.json", "rules/facilities.json",
            "config/position-ratings.json", "config/out-of-position.json", "config/probability.json",
            "config/lucky-charm.json", "config/personalities.json",
            "world/countries.json", "world/competitions.json", "config/world-generation.json", "names/names.json",
            "config/match-sim.json", "config/league-formats.json", "config/development.json", "config/market-value.json", "config/protege.json", "config/finance.json", "config/currencies.json", "config/transfers.json", "config/calendar.json", "config/facilities.json", "world/cups.json", "config/saves.json", "text/manager-messages.json", "config/inbox.json",
        };

        /// <param name="read">Returns the JSON text for a relative path from <see cref="Files"/>.</param>
        public static GameData Load(Func<string, string> read)
        {
            var d = new GameData();
            d.Archetypes = Section<List<Archetype>>(read("rules/archetypes.json"), "archetypes");
            d.Personalities = Section<List<Personality>>(read("rules/personalities.json"), "personalities");
            d.PositionRules = Parse<PositionRules>(read("rules/positions.json"));
            d.Facilities = Section<List<FacilityInfo>>(read("rules/facilities.json"), "facilities");
            d.PositionRatings = Parse<PositionRatingConfig>(read("config/position-ratings.json"));
            d.OutOfPosition = Parse<OutOfPositionConfig>(read("config/out-of-position.json"));
            d.Probability = Parse<ProbabilityConfig>(read("config/probability.json"));
            d.LuckyCharm = Parse<LuckyCharmConfig>(read("config/lucky-charm.json"));
            d.PersonalitySettings = Parse<PersonalityConfig>(read("config/personalities.json"));
            d.Countries = Section<List<Country>>(read("world/countries.json"), "countries");
            d.Competitions = Section<List<Competition>>(read("world/competitions.json"), "competitions");
            d.WorldGen = Parse<World.WorldGenConfig>(read("config/world-generation.json"));
            d.Names = Parse<World.NameBank>(read("names/names.json"));
            d.MatchSim = Parse<Season.MatchSimConfig>(read("config/match-sim.json"));
            d.LeagueFormats = Parse<Season.LeagueFormatConfig>(read("config/league-formats.json"));
            d.Development = Parse<Season.DevelopmentConfig>(read("config/development.json"));
            d.MarketValue = Parse<Rules.MarketValueConfig>(read("config/market-value.json"));
            d.Protege = Parse<World.ProtegeConfig>(read("config/protege.json"));
            d.Finance = Parse<Money.FinanceConfig>(read("config/finance.json"));
            d.Currencies = Parse<Money.CurrencyConfig>(read("config/currencies.json"));
            d.Transfers = Parse<Transfers.TransferConfig>(read("config/transfers.json"));
            d.Calendar = Parse<Season.CalendarConfig>(read("config/calendar.json"));
            d.FacilityRules = Parse<LegendsFC.Core.Facilities.FacilityConfig>(read("config/facilities.json"));
            d.Cups = Parse<Season.CupData>(read("world/cups.json"));
            d.Saves = Parse<Saves.SaveConfig>(read("config/saves.json"));
            d.Messages = Parse<Inbox.MessageBank>(read("text/manager-messages.json"));
            d.InboxRules = Parse<Inbox.InboxConfig>(read("config/inbox.json"));
            return d;
        }

        public static GameData LoadFromDirectory(string dataRoot)
            => Load(rel => File.ReadAllText(Path.Combine(dataRoot, rel)));

        private static T Parse<T>(string json) => JsonConvert.DeserializeObject<T>(json);

        private static T Section<T>(string json, string key) => JObject.Parse(json)[key].ToObject<T>();
    }

    /// <summary>data/config/personalities.json.</summary>
    public sealed class PersonalityConfig
    {
        /// <summary>Share of players with a personality (confirmed range 40-50%).</summary>
        public double ShareWithPersonality = 0.45;
    }
}
