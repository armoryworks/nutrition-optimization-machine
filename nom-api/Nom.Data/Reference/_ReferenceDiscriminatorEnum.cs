namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents the discriminator values (Group IDs) used for Table-Per-Hierarchy
    /// mapping of GroupedReferenceViewEntity and its derived types.
    /// </summary>
    public enum ReferenceDiscriminatorEnum : long
    {
        MealType = 1,
        MeasurementType = 2,
        RecipeType = 3,
        ShoppingStatusType = 4,
        ItemStatusType = 5,
        RestrictionType = 6,
        GoalType = 7,
        NutrientType = 8,
        CuisineType = 9,

        // --- THESE ARE THE MISSING ENTRIES ---
        QuestionCategory = 10, // For groups of questions (e.g., Societal, Medical, Preference)
        AnswerType = 11        // For types of answers (e.g., Yes/No, Text Input, Multi-Select)
    }
}