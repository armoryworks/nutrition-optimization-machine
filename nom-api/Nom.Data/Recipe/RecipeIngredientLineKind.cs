namespace Nom.Data.Recipe
{
    /// <summary>
    /// Known values for <see cref="RecipeIngredientEntity.LineKind"/>. Only <see cref="Ingredient"/>
    /// is a real ingredient whose amount is missing; every other kind is a line that never had an
    /// amount to give, so vetting and auto-approval do not count it as unquantified.
    /// </summary>
    public static class RecipeIngredientLineKind
    {
        /// <summary>A real ingredient whose amount is missing.</summary>
        public const string Ingredient = "ingredient";

        /// <summary>A component made by another recipe or prepared separately: a sauce, gravy, stock, forcemeat.</summary>
        public const string Reference = "reference";

        public const string Equipment = "equipment";

        /// <summary>A yield or serving note, or a heading.</summary>
        public const string Note = "note";

        /// <summary>Method text stored as an ingredient line.</summary>
        public const string Instruction = "instruction";

        /// <summary>A merge leftover such as "butter + butter", or an empty or garbled line.</summary>
        public const string Artefact = "artefact";

        public static readonly string[] All = { Ingredient, Reference, Equipment, Note, Instruction, Artefact };

        public static bool IsExempt(string? kind) =>
            kind is Reference or Equipment or Note or Instruction or Artefact;
    }
}
