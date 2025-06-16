namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents the discriminator values (Group IDs) used for Table-Per-Hierarchy
    /// mapping of GroupedReferenceViewEntity and its derived types.
    /// </summary>
    public enum ReferenceDiscriminatorEnum : long // Using long to match the GroupId type
    {
        /// <summary>
        /// Corresponds to the GroupId for Meal Types (e.g., Breakfast, Lunch, Dinner).
        /// </summary>
        MealType = 1,

        /// <summary>
        /// Corresponds to the GroupId for Measurement Types (e.g., grams, cups, units).
        /// </summary>
        MeasurementType = 2,

        /// <summary>
        /// Corresponds to the GroupId for Recipe Types (e.g., Appetizer, Main Course, Dessert).
        /// </summary>
        RecipeType = 3,

        /// <summary>
        /// Corresponds to the GroupId for Shopping Trip Status Types (e.g., Planned, Completed, Canceled).
        /// </summary>
        ShoppingStatusType = 4,

        /// <summary>
        /// Corresponds to the GroupId for Pantry Item Status Types (e.g., OnList, Acquired, InPantry, Used, Expired).
        /// </summary>
        ItemStatusType = 5,

        /// <summary>
        /// Corresponds to the GroupId for Restriction Types (e.g., Gluten-Free, Vegan, Allergy).
        /// </summary>
        RestrictionType = 6,

        /// <summary>
        /// Corresponds to the GroupId for Goal Types (e.g., Weight Loss, Muscle Gain, Maintenance).
        /// </summary>
        GoalType = 7,

        /// <summary>
        /// Corresponds to the GroupId for Nutrient Types (e.g., Macronutrient, Micronutrient, Vitamin, Mineral).
        /// </summary>
        NutrientType = 8,

        /// <summary>
        /// Corresponds to the GroupId for Cuisine Types (e.g., Italian, Mexican, Asian).
        /// </summary>
        CuisineType = 9
    }
}