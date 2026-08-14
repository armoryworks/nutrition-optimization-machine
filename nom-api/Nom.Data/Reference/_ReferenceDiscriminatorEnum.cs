// File: Nom.Data/Reference/_ReferenceDiscriminatorEnum.cs

namespace Nom.Data.Reference
{
    public enum ReferenceDiscriminatorEnum : long
    {
        Unknown = 0,

        // Core System Reference Groups (1-999)
        MealType = 1,
        RecipeType = 3,
        ShoppingStatusType = 4,
        ItemStatusType = 5,

        // Core Application Feature Reference Groups (1000-1999)
        QuestionCategory = 1000,
        AnswerType = 1001,
        CurationStatusType = 1002,      // NEW
        FeedbackEntityType = 1003,      // NEW
        FeedbackType = 1004,            // NEW
        RecipeEventType = 1005,         // NEW - Recipe timeline events
        RecipeStatusType = 1006,        // NEW - Recipe status (draft, published, etc.)
        RecipeShareTokenType = 1007,    // NEW - Recipe share token types
        RecipeCommentType = 1008,       // NEW - Recipe comment types
        RecipeNoteType = 1009,          // NEW - Recipe note types

        // Dietary & Health Related Reference Groups (2000-2999)
        RestrictionType = 2000,
        GoalType = 2001,

        // Nutritional & Ingredient Reference Groups (3000-3999)
        NutrientType = 3000,
        CuisineType = 3001,
        FoodGroupType = 3002,           // Nutritional food groups (Vegetables, Fruits, Grains, etc.)

        // Plan Management & User Roles (4000-4999)
        PlanInvitationRole = 4000,

        // Privacy & Compliance (5000-5999)
        PrivacyConsentType = 5000,

        // UI Data Conversion Reference Groups (6000-6999)
        ShoppingPriorityType = 6000,           // For shopping priority levels (Low, Medium, High)
        ShoppingCategoryType = 6001,           // For shopping categories (Produce, Dairy, Meat, etc.)
        RecipeDifficultyType = 6003,           // For recipe difficulty levels (Easy, Medium, Hard)
        PersonActivityLevelType = 6004,        // For person activity levels (Sedentary, Active, etc.)
        PersonDietaryRestrictionType = 6005,   // For dietary restrictions (None, Vegetarian, Vegan, etc.)
        PersonHealthGoalType = 6006,           // For health goals (Weight Loss, Maintenance, etc.)
        AllergyType = 6007,                    // For allergy types (Peanuts, Tree Nuts, etc.)
        MedicalConditionType = 6008,           // For medical conditions (Celiac Disease, Diabetes, etc.)
        SocietalRestrictionType = 6009,        // For religious/ethical restrictions (Kosher, Halal, etc.)
        PersonalPreferenceType = 6010,         // For personal preferences (Spice levels, textures, etc.)
        PersonAttributeType = 6011,            // For person attribute types (Height, Weight, Gender, DOB)
        SortOptionType = 6013,                 // For search/sort options (relevance, rating, name, etc.)
        SortDirectionType = 6014,              // For sort directions (asc, desc)
        DayOfWeekType = 6015,                  // For days of week (Monday, Tuesday, etc.)
        RecipeDietaryOptionType = 6016         // For recipe dietary options (Vegetarian, Vegan, etc.)
    }
}