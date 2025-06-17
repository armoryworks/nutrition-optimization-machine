namespace Nom.Data.Reference
{
    /// <summary>
    /// Serves as a discriminator for the 'Group' table in the 'reference' schema.
    /// Each enum member represents a distinct category or type of reference data.
    /// These values are used as primary keys in the 'Group' table and foreign keys
    /// in the 'ReferenceIndex' table to categorize 'Reference' entries.
    /// </summary>
    public enum ReferenceDiscriminatorEnum : long
    {
        Unknown = 0,

        // Core System Reference Groups (1-999)
        MealType = 1,           // e.g., Breakfast, Lunch, Dinner, Snack
        MeasurementType = 2,    // e.g., grams, ml, cup, tbsp, each
        RecipeType = 3,         // e.g., Appetizer, Main Course, Dessert, Soup
        ShoppingStatusType = 4, // e.g., Planned, Active, Completed, Canceled
        ItemStatusType = 5,     // e.g., On Hand, Low, Out of Stock, Expired

        // Core Application Feature Reference Groups (1000-1999)
        QuestionCategory = 1000, // e.g., Onboarding, Preferences, Health
        AnswerType = 1001,       // e.g., Yes/No, Text Input, Multi-Select, Single-Select

        // Dietary & Health Related Reference Groups (2000-2999)
        RestrictionType = 2000, // e.g., Gluten-Free, Vegan, Allergy, Lactose-Intolerant
        GoalType = 2001,        // e.g., Weight Loss, Muscle Gain, Maintenance

        // Nutritional & Ingredient Reference Groups (3000-3999)
        NutrientType = 3000,     // e.g., Macronutrient, Vitamin, Mineral
        CuisineType = 3001,      // e.g., Italian, Mexican, Asian

        // Plan Management & User Roles (4000-4999)
        PlanInvitationRole = 4000 // NEW: e.g., Plan Admin, Plan Member
    }
}
