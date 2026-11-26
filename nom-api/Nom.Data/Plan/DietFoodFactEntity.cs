namespace Nom.Data.Plan
{
    /// <summary>What the diet gate stores for a food or a recipe under a restriction set: a row exists only for these; a decided food or recipe without one is free.</summary>
    public enum DietVerdict : short
    {
        /// <summary>Contains what the set restricts.</summary>
        Contains = 1,

        /// <summary>Cannot be decided: held for a severe set.</summary>
        Unknown = 2,

        /// <summary>A food a pattern of the set matches only through an alias that does not count: free itself, but a recipe on it is flagged when a line of it names a pattern of the set.</summary>
        AliasOnly = 3,
    }

    /// <summary>Why a recipe carries its flag.</summary>
    public enum DietFlagReason : short
    {
        /// <summary>A line is on a food the set counts.</summary>
        Food = 1,

        /// <summary>A line's own text names a pattern the set reads in every line.</summary>
        Line = 2,

        /// <summary>A line is on a food matched only through an alias that does not count, and a line names a pattern of the set.</summary>
        AliasLine = 3,

        /// <summary>A line's own text reads as a drink while its food is not counted.</summary>
        Drink = 4,

        /// <summary>More drink alcohol stays per serving than the set's cap, or a drink line cannot be counted.</summary>
        Retained = 5,

        /// <summary>The recipe has no live ingredient line.</summary>
        NoLine = 6,

        /// <summary>A line is on a food the build has not decided yet.</summary>
        UnknownFood = 7,

        /// <summary>The title of a recipe whose lines were read out of a book's text names what the set restricts, and no line does.</summary>
        Title = 8,

        /// <summary>The method of a recipe whose lines were read out of a book's text names what the set restricts, and no line does.</summary>
        Method = 9,

        /// <summary>A line no curated food decides names a word the set does not understand: no curated food knows it, or the set flags some of the foods that know it and leaves others clear (a severe set only).</summary>
        UnknownWord = 10,

        /// <summary>A line no curated food decides names a word the curated catalog knows only on foods the set flags, or a line holds a processing kind of the set (the dried form of a fruit for Sulfite Sensitivity).</summary>
        FoodWord = 11,
    }

    /// <summary>
    /// What one restriction set decided for one food, computed by the diet gate's matcher in the build job: it
    /// contains what the set restricts (by name, alias, USDA category, ingredient statement, nutrient or drink name), or
    /// a pattern of the set matches it only through an alias that does not count. A food the build decided without a
    /// row is free; a food the build has not decided (<see cref="DietFoodStateEntity"/>) is unknown.
    /// </summary>
    public class DietFoodFactEntity
    {
        public long SetId { get; set; }

        public long IngredientId { get; set; }

        public DietVerdict Verdict { get; set; }

        /// <summary>The criterion that decided it: the highest-severity criterion of the set that counts the food, the lowest id among equals.</summary>
        public long? CriterionId { get; set; }

        /// <summary>Every criterion of the set that counts the food, comma-separated ids in ascending order.</summary>
        public string? CriterionIds { get; set; }

        /// <summary>How the deciding criterion counts the food: <c>name</c>, <c>alias</c>, <c>category</c>, <c>contents</c>, <c>nutrient</c> or <c>drink-name</c>.</summary>
        public string? Source { get; set; }
    }
}
