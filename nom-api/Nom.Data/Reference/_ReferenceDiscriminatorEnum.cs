// File: Nom.Data/Reference/_ReferenceDiscriminatorEnum.cs

namespace Nom.Data.Reference
{
    public enum ReferenceDiscriminatorEnum : long
    {
        Unknown = 0,

        // Core System Reference Groups (1-999)
        MealType = 1,
        MeasurementType = 2,
        RecipeType = 3,
        ShoppingStatusType = 4,
        ItemStatusType = 5,

        // Core Application Feature Reference Groups (1000-1999)
        QuestionCategory = 1000,
        AnswerType = 1001,
        CurationStatusType = 1002,      // NEW
        FeedbackEntityType = 1003,      // NEW
        FeedbackType = 1004,            // NEW

        // Dietary & Health Related Reference Groups (2000-2999)
        RestrictionType = 2000,
        GoalType = 2001,

        // Nutritional & Ingredient Reference Groups (3000-3999)
        NutrientType = 3000,
        CuisineType = 3001,

        // Plan Management & User Roles (4000-4999)
        PlanInvitationRole = 4000,

        // Privacy & Compliance (5000-5999)
        PrivacyConsentType = 5000
    }
}