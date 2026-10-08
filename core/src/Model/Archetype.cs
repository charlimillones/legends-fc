using System.Collections.Generic;

namespace LegendsFC.Core.Model
{
    /// <summary>A hidden, fixed-for-life archetype (37 total). Loaded from data/rules/archetypes.json.</summary>
    public sealed class Archetype
    {
        public string Id;
        public string Name;
        /// <summary>A PositionGroup name, or "ANY_OUTFIELD" (A Keeper).</summary>
        public string Group;
        public string Description;
        public List<string> Plus = new List<string>();
        public List<string> Minus = new List<string>();
        public bool Balanced;
        /// <summary>Position tokens (see Positions.Resolve) where the drop is 0%.</summary>
        public List<string> Comfortable = new List<string>();
        /// <summary>Position tokens where the drop is the cover drop (5%).</summary>
        public List<string> Cover = new List<string>();
        public bool WrongFootExempt;
        public double SpawnWeight = 1.0;
        /// <summary>A Keeper only: chance per generated outfield player (1 in 1,000).</summary>
        public double? SpawnPerOutfieldPlayer;
        /// <summary>A Keeper only: the personality that always comes with this archetype.</summary>
        public string LinkedPersonalityId;

        public bool IsAnyOutfield => Group == "ANY_OUTFIELD";
    }

    /// <summary>A visible personality (13 incl. the special A Keeper). Loaded from data/rules/personalities.json.</summary>
    public sealed class Personality
    {
        public string Id;
        public string Name;
        public string Effect;
        public bool ArchetypeOnly;
        public double SpawnWeight = 1.0;
    }
}
