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

        public Archetype Archetype(string id) => Archetypes.First(a => a.Id == id);
        public Personality Personality(string id) => Personalities.First(p => p.Id == id);

        /// <summary>Files the loader expects, relative to the data root.</summary>
        public static readonly string[] Files =
        {
            "rules/archetypes.json", "rules/personalities.json", "rules/positions.json",
            "config/position-ratings.json", "config/out-of-position.json", "config/probability.json",
            "config/lucky-charm.json", "config/personalities.json",
            "world/countries.json", "world/competitions.json", "config/world-generation.json", "names/names.json",
            "config/match-sim.json", "config/league-formats.json",
        };

        /// <param name="read">Returns the JSON text for a relative path from <see cref="Files"/>.</param>
        public static GameData Load(Func<string, string> read)
        {
            var d = new GameData();
            d.Archetypes = Section<List<Archetype>>(read("rules/archetypes.json"), "archetypes");
            d.Personalities = Section<List<Personality>>(read("rules/personalities.json"), "personalities");
            d.PositionRules = Parse<PositionRules>(read("rules/positions.json"));
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
