using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>An alias with no context that a deterministic rule gives to the food it is on.</summary>
    public class AliasClassifySample
    {
        public long IngredientId { get; set; }
        public string FoodName { get; set; } = string.Empty;
        public string AliasName { get; set; } = string.Empty;
        /// <summary>"name-key", "staple" or "name-match".</summary>
        public string Rule { get; set; } = string.Empty;
    }

    public class AliasClassifyResult
    {
        public bool Applied { get; set; }
        /// <summary>Live aliases with no context on live foods.</summary>
        public int Examined { get; set; }
        /// <summary>The alias is the food's name, in the singular or plural or without its preparation words.</summary>
        public int NameKey { get; set; }
        /// <summary>The alias is a bare staple name and the food is that staple's USDA entry.</summary>
        public int Staple { get; set; }
        /// <summary>The name matcher gives the alias to the food.</summary>
        public int NameMatch { get; set; }
        /// <summary>Aliases a rule gives to another food; they stay without a context.</summary>
        public int Conflicts { get; set; }
        /// <summary>Aliases no rule covers; they stay without a context.</summary>
        public int Unchanged { get; set; }
        /// <summary>Up to the sample size for each rule, in food order.</summary>
        public List<AliasClassifySample> Samples { get; set; } = new();
    }

    public class AliasClassifyUndoResult
    {
        public int Restored { get; set; }
        /// <summary>Journal entries whose alias has since gone or been given another context.</summary>
        public int Skipped { get; set; }
    }
}
