// File: Nom.Data/_CustomMigration.cs

using Microsoft.EntityFrameworkCore.Migrations;
using Nom.Data.Reference; // For ReferenceDiscriminatorEnum
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic; // Required for List
using System.Text.Json;

namespace Nom.Data
{
#pragma warning disable CS8625 // Disable warnings for nullable reference type assignments
    public static class CustomMigration
    {
        // --- System Person ID ---
        private const long SystemPersonId = 1L;

        // --- Meal Type IDs (1100L series to align with GroupId 1 = MealType) ---
        private const long MealTypeBreakfastId = 1100L;
        private const long MealTypeLunchId = 1101L;
        private const long MealTypeDinnerId = 1102L;
        private const long MealTypeSnacksId = 1103L;

        // --- Recipe Type IDs (3100L series to align with GroupId 3 = RecipeType) ---
        private const long RecipeTypeAppetizerId = 3100L;
        private const long RecipeTypeEntreeId = 3101L;
        private const long RecipeTypeStarchId = 3102L;
        private const long RecipeTypeVegetableId = 3103L;
        private const long RecipeTypeSnackId = 3104L;
        private const long RecipeTypeDessertId = 3105L;

        // --- CORRECTED: Plan Invitation Role IDs (41xxL series to align with GroupId 4000) ---
        private const long PlanInvitationRoleAdminId = 4100L;
        private const long PlanInvitationRoleMemberId = 4101L;



        // --- Nutrient IDs (Derived from DRVs and RDIs) ---
        private const long NutrientFatId = 5000L;
        private const long NutrientSaturatedFatId = 5001L;
        private const long NutrientCholesterolId = 5002L;
        private const long NutrientTotalCarbohydratesId = 5003L;
        private const long NutrientSodiumId = 5004L;
        private const long NutrientDietaryFiberId = 5005L;
        private const long NutrientProteinId = 5006L;
        private const long NutrientAddedSugarsId = 5007L;
        private const long NutrientVitaminAId = 5008L;
        private const long NutrientVitaminCId = 5009L;
        private const long NutrientVitaminDId = 5010L;
        private const long NutrientVitaminEId = 5011L;
        private const long NutrientVitaminKId = 5012L;
        private const long NutrientThiaminId = 5013L; // B1
        private const long NutrientRiboflavinId = 5014L; // B2
        private const long NutrientNiacinId = 5015L; // B3
        private const long NutrientVitaminB6Id = 5016L;
        private const long NutrientFolateId = 5017L;
        private const long NutrientVitaminB12Id = 5018L;
        private const long NutrientBiotinId = 5019L;
        private const long NutrientPantothenicAcidId = 5020L;
        private const long NutrientCholineId = 5021L;
        private const long NutrientCalciumId = 5022L;
        private const long NutrientIronId = 5023L;
        private const long NutrientPhosphorusId = 5024L;
        private const long NutrientIodineId = 5025L;
        private const long NutrientMagnesiumId = 5026L;
        private const long NutrientZincId = 5027L;
        private const long NutrientSeleniumId = 5028L;
        private const long NutrientCopperId = 5029L;
        private const long NutrientManganeseId = 5030L;
        private const long NutrientChromiumId = 5031L;
        private const long NutrientMolybdenumId = 5032L;
        private const long NutrientChlorideId = 5033L;
        private const long NutrientPotassiumId = 5034L;
        private const long NutrientCaloriesFDCId = 5035L;

        // --- Reference Data IDs (Goal Types - for Nutrient Guidelines, matching PDF demographics) ---
        private const long GoalTypeAdultsAndChildren4PlusId = 6000L;
        private const long GoalTypeInfantsThrough12MonthsId = 6001L;
        private const long GoalTypeChildren1Through3YearsId = 6002L;
        private const long GoalTypePregnantAndLactatingWomenId = 6003L;
        private const long GoalTypeGeneralAdultId = 6004L;

        // --- Domain Group IDs ---
        private const long DefaultHouseholdGroupId = 1L;
        private const long DefaultShoppingListGroupId = 1L;

        // --- Privacy Consent Type IDs (8xxxL series) ---
        private const long PrivacyConsentTypeAnalyticsId = 8000L;
        private const long PrivacyConsentTypeMarketingId = 8001L;
        private const long PrivacyConsentTypePersonalizationId = 8002L;

        // --- Curation and Feedback Reference IDs (9000-9299 series) ---
        private const long CurationStatusTypeNonCuratedId = 9000L;
        private const long CurationStatusTypePendingCurationId = 9001L;
        private const long CurationStatusTypeRequiresRevisionId = 9002L;
        private const long CurationStatusTypeCuratedId = 9003L;
        private const long CurationStatusTypeRejectedId = 9004L;

        private const long FeedbackEntityTypeRecipeId = 9100L;
        private const long FeedbackEntityTypeIngredientId = 9101L;

        private const long FeedbackTypeApprovalId = 9199L; // Generic "Approval" reference for backward compatibility
        private const long FeedbackTypeApprovalPrivateId = 9200L;
        private const long FeedbackTypeApprovalPublicId = 9201L;
        private const long FeedbackTypeRevisionRequestId = 9202L;
        private const long FeedbackTypeRejectionId = 9203L;

        // --- Recipe Reference IDs (10000-10999 series) ---
        // Recipe Event Types (10000-10099)
        private const long RecipeEventTypeCreatedId = 10001L;
        private const long RecipeEventTypeUpdatedId = 10002L;
        private const long RecipeEventTypePublishedId = 10003L;
        private const long RecipeEventTypeRatedId = 10004L;
        private const long RecipeEventTypeCommentedId = 10005L;
        private const long RecipeEventTypeMadeId = 10006L;
        private const long RecipeEventTypeSharedId = 10007L;
        private const long RecipeEventTypeFavoritedId = 10008L;
        private const long RecipeEventTypeAddedToPlanId = 10009L;
        private const long RecipeEventTypeExportedId = 10010L;

        // --- UI Data Conversion Reference IDs (11000-11999 series) ---
        // Shopping Priority Types
        private const long ShoppingPriorityLowId = 11000L;
        private const long ShoppingPriorityMediumId = 11001L;
        private const long ShoppingPriorityHighId = 11002L;

        // Shopping Category Types
        private const long ShoppingCategoryProduceId = 11010L;
        private const long ShoppingCategoryDairyId = 11011L;
        private const long ShoppingCategoryMeatId = 11012L;
        private const long ShoppingCategoryPantryId = 11013L;
        private const long ShoppingCategoryFrozenId = 11014L;
        private const long ShoppingCategoryBeveragesId = 11015L;
        private const long ShoppingCategorySnacksId = 11016L;
        private const long ShoppingCategoryHouseholdId = 11017L;
        private const long ShoppingCategoryOtherId = 11018L;

        // Recipe Difficulty Types
        private const long RecipeDifficultyEasyId = 11020L;
        private const long RecipeDifficultyMediumId = 11021L;
        private const long RecipeDifficultyHardId = 11022L;

        // Person Activity Level Types
        private const long PersonActivityLevelSedentaryId = 11030L;
        private const long PersonActivityLevelLightlyActiveId = 11031L;
        private const long PersonActivityLevelModeratelyActiveId = 11032L;
        private const long PersonActivityLevelVeryActiveId = 11033L;
        private const long PersonActivityLevelExtremelyActiveId = 11034L;

        // Person Dietary Restriction Types
        private const long PersonDietaryRestrictionNoneId = 11040L;
        private const long PersonDietaryRestrictionVegetarianId = 11041L;
        private const long PersonDietaryRestrictionVeganId = 11042L;
        private const long PersonDietaryRestrictionGlutenFreeId = 11043L;
        private const long PersonDietaryRestrictionDairyFreeId = 11044L;
        private const long PersonDietaryRestrictionKetoId = 11045L;
        private const long PersonDietaryRestrictionPaleoId = 11046L;

        // Person Health Goal Types
        private const long PersonHealthGoalWeightLossId = 11050L;
        private const long PersonHealthGoalWeightGainId = 11051L;
        private const long PersonHealthGoalMaintenanceId = 11052L;
        private const long PersonHealthGoalMuscleGainId = 11053L;
        private const long PersonHealthGoalGeneralHealthId = 11054L;

        // Person Attribute Types
        private const long PersonAttributeTypeHeightId = 11070L;
        private const long PersonAttributeTypeWeightId = 11071L;
        private const long PersonAttributeTypeGenderId = 11072L;
        private const long PersonAttributeTypeDateOfBirthId = 11073L;
        private const long PersonAttributeTypeActivityLevelId = 11074L;
        private const long PersonAttributeTypeHealthGoalId = 11075L;
        private const long PersonAttributeTypeRMRId = 11076L;
        private const long PersonAttributeTypeAMRId = 11077L;

        // Day of Week Types
        private const long DayOfWeekMondayId = 11060L;
        private const long DayOfWeekTuesdayId = 11061L;
        private const long DayOfWeekWednesdayId = 11062L;
        private const long DayOfWeekThursdayId = 11063L;
        private const long DayOfWeekFridayId = 11064L;
        private const long DayOfWeekSaturdayId = 11065L;
        private const long DayOfWeekSundayId = 11066L;

        // Recipe Status Types (10100-10199)
        private const long RecipeStatusTypeDraftId = 10101L;
        private const long RecipeStatusTypePublishedId = 10102L;
        private const long RecipeStatusTypeArchivedId = 10103L;
        private const long RecipeStatusTypeDeletedId = 10104L;

        // Recipe Share Token Types (10200-10299)
        private const long RecipeShareTokenTypePublicId = 10201L;
        private const long RecipeShareTokenTypePrivateId = 10202L;
        private const long RecipeShareTokenTypeTemporaryId = 10203L;

        // Recipe Comment Types (10300-10399)
        private const long RecipeCommentTypeGeneralId = 10301L;
        private const long RecipeCommentTypeReviewId = 10302L;
        private const long RecipeCommentTypeSuggestionId = 10303L;
        private const long RecipeCommentTypeQuestionId = 10304L;

        // Recipe Note Types (10400-10499)
        private const long RecipeNoteTypePrivateId = 10401L;
        private const long RecipeNoteTypePublicId = 10402L;
        private const long RecipeNoteTypeCookingTipId = 10403L;
        private const long RecipeNoteTypeVariationId = 10404L;
        private const long RecipeNoteTypeSubstitutionId = 10405L;

        // --- Nutrient Guideline IDs (7xxxL series) ---
        private static long nextGuidelineId = 7000L;

        public static void ApplyCustomUpOperations(this MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "person");
            migrationBuilder.EnsureSchema(name: "audit");
            migrationBuilder.EnsureSchema(name: "plan");
            migrationBuilder.EnsureSchema(name: "recipe");
            migrationBuilder.EnsureSchema(name: "nutrient");
            migrationBuilder.EnsureSchema(name: "shopping");
            migrationBuilder.EnsureSchema(name: "reference");
            migrationBuilder.EnsureSchema(name: "privacy");
            migrationBuilder.EnsureSchema(name: "curation");
            migrationBuilder.EnsureSchema(name: "communication");

            SeedInitialSystemPerson(migrationBuilder);

            AddReferenceGroups(migrationBuilder);
            AddHouseholdGroups(migrationBuilder);
            AddShoppingListGroups(migrationBuilder);

            AddMealTypes(migrationBuilder);
            AddRestrictionTypes(migrationBuilder);
            AddPlanInvitationRoles(migrationBuilder);
            AddMeasurementTypes(migrationBuilder);
            AddGoalTypes(migrationBuilder);
            AddPrivacyConsentTypes(migrationBuilder);

            AddCurationStatusTypes(migrationBuilder);
            AddFeedbackEntityTypes(migrationBuilder);
            AddFeedbackTypes(migrationBuilder);

            AddRecipeEventTypes(migrationBuilder);
            AddRecipeStatusTypes(migrationBuilder);
            AddRecipeShareTokenTypes(migrationBuilder);
            AddRecipeCommentTypes(migrationBuilder);
            AddRecipeNoteTypes(migrationBuilder);

            // UI Data Conversion Seeding
            AddShoppingPriorityTypes(migrationBuilder);
            AddShoppingCategoryTypes(migrationBuilder);
            AddRecipeDifficultyTypes(migrationBuilder);
            AddPersonActivityLevelTypes(migrationBuilder);
            AddPersonHealthGoalTypes(migrationBuilder);
            AddPersonAttributeTypes(migrationBuilder);
            AddDayOfWeekTypes(migrationBuilder);

            // Expanded restriction/preference type seeding
            AddPersonDietaryRestrictionTypes(migrationBuilder);
            AddAllergyTypes(migrationBuilder);
            AddMedicalConditionTypes(migrationBuilder);
            AddSocietalRestrictionTypes(migrationBuilder);
            AddPersonalPreferenceTypes(migrationBuilder);

            AddNutrientTypes(migrationBuilder);
            AddNutrientGuidelines(migrationBuilder);

            CreateReferenceGroupView(migrationBuilder);

            SeedSampleRecipes(migrationBuilder);

            AddRecipeTypes(migrationBuilder);
            AssignRecipeTypesToSeedRecipes(migrationBuilder);

            SeedExtendedRecipes(migrationBuilder);
        }

        public static void ApplyCustomDownOperations(this MigrationBuilder migrationBuilder)
        {
            RemoveExtendedRecipes(migrationBuilder);

            RemoveRecipeTypeAssignments(migrationBuilder);
            RemoveRecipeTypes(migrationBuilder);

            RemoveSampleRecipes(migrationBuilder);

            DropReferenceGroupView(migrationBuilder);

            RemoveNutrientGuidelines(migrationBuilder);
            RemoveNutrientTypes(migrationBuilder);

            RemoveRecipeNoteTypes(migrationBuilder);
            RemoveRecipeCommentTypes(migrationBuilder);
            RemoveRecipeShareTokenTypes(migrationBuilder);
            RemoveRecipeStatusTypes(migrationBuilder);
            RemoveRecipeEventTypes(migrationBuilder);

            // UI Data Conversion Removal
            RemoveDayOfWeekTypes(migrationBuilder);
            RemovePersonAttributeTypes(migrationBuilder);
            RemovePersonHealthGoalTypes(migrationBuilder);
            RemovePersonActivityLevelTypes(migrationBuilder);
            RemoveRecipeDifficultyTypes(migrationBuilder);
            RemoveShoppingCategoryTypes(migrationBuilder);
            RemoveShoppingPriorityTypes(migrationBuilder);

            // Expanded restriction/preference type removal
            RemovePersonalPreferenceTypes(migrationBuilder);
            RemoveSocietalRestrictionTypes(migrationBuilder);
            RemoveMedicalConditionTypes(migrationBuilder);
            RemoveAllergyTypes(migrationBuilder);
            RemovePersonDietaryRestrictionTypes(migrationBuilder);

            RemoveFeedbackTypes(migrationBuilder);
            RemoveFeedbackEntityTypes(migrationBuilder);
            RemoveCurationStatusTypes(migrationBuilder);

            RemovePrivacyConsentTypes(migrationBuilder);
            RemoveGoalTypes(migrationBuilder);
            RemoveMeasurementTypes(migrationBuilder);
            RemovePlanInvitationRoles(migrationBuilder);
            RemoveMealTypes(migrationBuilder);
            RemoveRestrictionTypes(migrationBuilder);
            RemoveShoppingListGroups(migrationBuilder);
            RemoveHouseholdGroups(migrationBuilder);
            RemoveReferenceGroups(migrationBuilder);
            RemoveInitialSystemPerson(migrationBuilder);

            migrationBuilder.DropSchema(name: "privacy");
            migrationBuilder.DropSchema(name: "person");
            migrationBuilder.DropSchema(name: "audit");
            migrationBuilder.DropSchema(name: "plan");
            migrationBuilder.DropSchema(name: "recipe");
            migrationBuilder.DropSchema(name: "nutrient");
            migrationBuilder.DropSchema(name: "shopping");
            migrationBuilder.DropSchema(name: "reference");
            migrationBuilder.DropSchema(name: "curation");
            migrationBuilder.DropSchema(name: "communication");
        }

        public static void SeedInitialSystemPerson(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "person",
                table: "Person",
                columns: new[] { "Id", "Name", "UserId", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { SystemPersonId, "System", null, DateTime.UtcNow, SystemPersonId }
                });
        }

        public static void RemoveInitialSystemPerson(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "person",
                table: "Person",
                keyColumn: "Id",
                keyValues: new object[] { SystemPersonId });
        }

        public static void AddReferenceGroups(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Group",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { (long)ReferenceDiscriminatorEnum.MealType, "Meal Types", "Categories for meals like breakfast, lunch, dinner.", DateTime.UtcNow, SystemPersonId },

                    { (long)ReferenceDiscriminatorEnum.RecipeType, "Recipe Types", "Categorization of recipes (e.g., appetizer, main course, dessert).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.ShoppingStatusType, "Shopping Status Types", "Statuses for shopping trips (e.g., planned, completed, canceled).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.ItemStatusType, "Item Status Types", "Statuses for pantry items (e.g., on list, in pantry, used, expired).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RestrictionType, "Restriction Types", "Dietary restrictions (e.g., gluten-free, vegan).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.GoalType, "Goal Types", "Nutritional goals or demographic categories for guidelines (e.g., 'Adults 4+ years').", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.NutrientType, "Nutrient Types", "Categories of nutrients (e.g., macronutrients, vitamins, minerals).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.CuisineType, "Cuisine Types", "Types of culinary styles (e.g., Italian, Mexican, Asian).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PlanInvitationRole, "Plan Invitation Roles", "Roles for invited participants in a plan (e.g., Admin, Member)", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PrivacyConsentType, "Privacy Consent Types", "Types of user consent for data processing under GDPR.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.CurationStatusType, "Curation Status Types", "Statuses for the content curation lifecycle.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.FeedbackEntityType, "Feedback Entity Types", "The types of entities that can receive curation feedback.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.FeedbackType, "Feedback Types", "The classification of feedback provided during curation.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeEventType, "Recipe Event Types", "Types of events that can occur in a recipe's timeline.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeStatusType, "Recipe Status Types", "Statuses for recipe lifecycle (draft, published, archived).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeShareTokenType, "Recipe Share Token Types", "Types of sharing tokens for recipes.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeCommentType, "Recipe Comment Types", "Types of comments that can be made on recipes.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeNoteType, "Recipe Note Types", "Types of notes that can be added to recipes.", DateTime.UtcNow, SystemPersonId },

                    // UI Data Conversion Reference Groups (6000-6999)
                    { (long)ReferenceDiscriminatorEnum.ShoppingPriorityType, "Shopping Priority Types", "Priority levels for shopping items (Low, Medium, High).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.ShoppingCategoryType, "Shopping Category Types", "Categories for organizing shopping items (Produce, Dairy, Meat, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeDifficultyType, "Recipe Difficulty Types", "Difficulty levels for recipes (Easy, Medium, Hard).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PersonActivityLevelType, "Person Activity Level Types", "Activity levels for persons (Sedentary, Lightly Active, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PersonDietaryRestrictionType, "Diets & Eating Patterns", "Voluntary dietary frameworks and eating styles (Vegan, Keto, Paleo, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PersonHealthGoalType, "Person Health Goal Types", "Health goals for persons (Weight Loss, Maintenance, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.AllergyType, "Allergies & Intolerances", "Immune-mediated allergies and digestive intolerances (Peanuts, Lactose, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.MedicalConditionType, "Medical Conditions", "Diagnosed conditions that affect dietary needs (Celiac, Diabetes, CKD, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.SocietalRestrictionType, "Religious & Cultural", "Faith-based and cultural dietary practices (Kosher, Halal, Jain, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PersonalPreferenceType, "Personal Preferences", "Individual taste and lifestyle preferences (No Spicy Food, Budget-Friendly, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PersonAttributeType, "Person Attribute Types", "Types of person attributes (Height, Weight, Gender, Date of Birth).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.SortOptionType, "Sort Option Types", "Search/sort options (relevance, rating, name, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.SortDirectionType, "Sort Direction Types", "Sort directions (ascending, descending).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.DayOfWeekType, "Day of Week Types", "Days of the week (Monday, Tuesday, etc.).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeDietaryOptionType, "Recipe Dietary Option Types", "Dietary options for recipes (Vegetarian, Vegan, etc.).", DateTime.UtcNow, SystemPersonId }
                });
        }

        public static void RemoveReferenceGroups(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Group",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    (long)ReferenceDiscriminatorEnum.MealType,
    
                    (long)ReferenceDiscriminatorEnum.RecipeType,
                    (long)ReferenceDiscriminatorEnum.ShoppingStatusType,
                    (long)ReferenceDiscriminatorEnum.ItemStatusType,
                    (long)ReferenceDiscriminatorEnum.RestrictionType,
                    (long)ReferenceDiscriminatorEnum.GoalType,
                    (long)ReferenceDiscriminatorEnum.NutrientType,
                    (long)ReferenceDiscriminatorEnum.CuisineType,
                    (long)ReferenceDiscriminatorEnum.PlanInvitationRole,
                    (long)ReferenceDiscriminatorEnum.PrivacyConsentType,
                    (long)ReferenceDiscriminatorEnum.CurationStatusType,
                    (long)ReferenceDiscriminatorEnum.FeedbackEntityType,
                    (long)ReferenceDiscriminatorEnum.FeedbackType,
                    (long)ReferenceDiscriminatorEnum.RecipeEventType,
                    (long)ReferenceDiscriminatorEnum.RecipeStatusType,
                    (long)ReferenceDiscriminatorEnum.RecipeShareTokenType,
                    (long)ReferenceDiscriminatorEnum.RecipeCommentType,
                    (long)ReferenceDiscriminatorEnum.RecipeNoteType,

                    // UI Data Conversion Reference Groups (6000-6999)
                    (long)ReferenceDiscriminatorEnum.ShoppingPriorityType,
                    (long)ReferenceDiscriminatorEnum.ShoppingCategoryType,
                    (long)ReferenceDiscriminatorEnum.RecipeDifficultyType,
                    (long)ReferenceDiscriminatorEnum.PersonActivityLevelType,
                    (long)ReferenceDiscriminatorEnum.PersonDietaryRestrictionType,
                    (long)ReferenceDiscriminatorEnum.PersonHealthGoalType,
                    (long)ReferenceDiscriminatorEnum.AllergyType,
                    (long)ReferenceDiscriminatorEnum.MedicalConditionType,
                    (long)ReferenceDiscriminatorEnum.SocietalRestrictionType,
                    (long)ReferenceDiscriminatorEnum.PersonalPreferenceType,
                    (long)ReferenceDiscriminatorEnum.PersonAttributeType,
                    (long)ReferenceDiscriminatorEnum.SortOptionType,
                    (long)ReferenceDiscriminatorEnum.SortDirectionType,
                    (long)ReferenceDiscriminatorEnum.DayOfWeekType,
                    (long)ReferenceDiscriminatorEnum.RecipeDietaryOptionType
                });
        }

        public static void AddMealTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.MealType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { MealTypeBreakfastId, "Breakfast", "Morning meal", DateTime.UtcNow, SystemPersonId },
                    { MealTypeLunchId, "Lunch", "Midday meal", DateTime.UtcNow, SystemPersonId },
                    { MealTypeDinnerId, "Dinner", "Evening meal", DateTime.UtcNow, SystemPersonId },
                    { MealTypeSnacksId, "Snacks", "Between-meal snacks", DateTime.UtcNow, SystemPersonId }
                });

            long[] mealTypeIds = new long[] {
                MealTypeBreakfastId, MealTypeLunchId, MealTypeDinnerId, MealTypeSnacksId
            };
            foreach (long id in mealTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveMealTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.MealType;
            long[] mealTypeIds = new long[] {
                MealTypeBreakfastId, MealTypeLunchId, MealTypeDinnerId, MealTypeSnacksId
            };
            foreach (long id in mealTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: new object[] { MealTypeBreakfastId, MealTypeLunchId, MealTypeDinnerId, MealTypeSnacksId });
        }

        public static void AddRecipeTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { RecipeTypeAppetizerId, "Appetizer/Starter", "Light opening dish (salad, soup, appetizer)", DateTime.UtcNow, SystemPersonId },
                    { RecipeTypeEntreeId, "Entree", "Protein-focused main dish", DateTime.UtcNow, SystemPersonId },
                    { RecipeTypeStarchId, "Starch/Carbohydrate", "Carbohydrate-based dish (rice, pasta, bread, grains)", DateTime.UtcNow, SystemPersonId },
                    { RecipeTypeVegetableId, "Vegetable/Salad", "Vegetable-focused or salad dish", DateTime.UtcNow, SystemPersonId },
                    { RecipeTypeSnackId, "Snack", "Small between-meal item", DateTime.UtcNow, SystemPersonId },
                    { RecipeTypeDessertId, "Dessert", "Sweet course", DateTime.UtcNow, SystemPersonId }
                });

            long[] recipeTypeIds = new long[] {
                RecipeTypeAppetizerId, RecipeTypeEntreeId, RecipeTypeStarchId,
                RecipeTypeVegetableId, RecipeTypeSnackId, RecipeTypeDessertId
            };
            foreach (long id in recipeTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveRecipeTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeType;
            long[] recipeTypeIds = new long[] {
                RecipeTypeAppetizerId, RecipeTypeEntreeId, RecipeTypeStarchId,
                RecipeTypeVegetableId, RecipeTypeSnackId, RecipeTypeDessertId
            };
            foreach (long id in recipeTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: new object[] {
                    RecipeTypeAppetizerId, RecipeTypeEntreeId, RecipeTypeStarchId,
                    RecipeTypeVegetableId, RecipeTypeSnackId, RecipeTypeDessertId });
        }

        public static void AssignRecipeTypesToSeedRecipes(MigrationBuilder migrationBuilder)
        {
            // Assign recipe types to the 24 seed recipes via the junction table
            var assignments = new (long RecipeId, long RecipeTypeId)[]
            {
                // Breakfast recipes
                (100, RecipeTypeStarchId),           // Classic Buttermilk Pancakes
                (101, RecipeTypeEntreeId),            // Veggie Scrambled Eggs
                (102, RecipeTypeVegetableId),         // Banana Smoothie Bowl
                (103, RecipeTypeStarchId),            // Classic French Toast
                (104, RecipeTypeEntreeId),            // Spinach and Feta Omelette
                (105, RecipeTypeEntreeId),            // Smoked Salmon Bagel

                // Lunch recipes
                (106, RecipeTypeEntreeId),            // Grilled Chicken Caesar Wrap
                (107, RecipeTypeAppetizerId),         // Black Bean and Corn Salad
                (107, RecipeTypeVegetableId),         // Black Bean and Corn Salad (also vegetable)
                (108, RecipeTypeEntreeId),            // Turkey Avocado Sandwich
                (109, RecipeTypeAppetizerId),         // Creamy Tomato Basil Soup
                (110, RecipeTypeEntreeId),            // Greek Quinoa Bowl
                (110, RecipeTypeStarchId),            // Greek Quinoa Bowl (also starch)
                (111, RecipeTypeEntreeId),            // Chicken Teriyaki Stir-Fry

                // Dinner recipes
                (112, RecipeTypeEntreeId),            // Spaghetti Bolognese
                (112, RecipeTypeStarchId),            // Spaghetti Bolognese (also starch)
                (113, RecipeTypeEntreeId),            // Lemon Herb Grilled Chicken
                (114, RecipeTypeEntreeId),            // Coconut Shrimp Curry
                (115, RecipeTypeEntreeId),            // Baked Salmon with Roasted Asparagus
                (115, RecipeTypeVegetableId),         // Baked Salmon with Roasted Asparagus (also vegetable)
                (116, RecipeTypeEntreeId),            // Beef Tacos with Fresh Salsa
                (117, RecipeTypeStarchId),            // Creamy Mushroom Risotto
                (117, RecipeTypeVegetableId),         // Creamy Mushroom Risotto (also vegetable)

                // Snack recipes
                (118, RecipeTypeSnackId),             // Hummus and Veggie Sticks
                (119, RecipeTypeSnackId),             // Apple Peanut Butter Bites
                (120, RecipeTypeSnackId),             // Cheese and Crackers Board
                (121, RecipeTypeSnackId),             // Trail Mix Energy Balls
                (122, RecipeTypeSnackId),             // Caprese Skewers
                (122, RecipeTypeAppetizerId),         // Caprese Skewers (also appetizer)
                (123, RecipeTypeSnackId),             // Guacamole with Tortilla Chips
            };

            foreach (var (recipeId, recipeTypeId) in assignments)
            {
                migrationBuilder.InsertData(
                    schema: "recipe",
                    table: "recipe_type_index",
                    columns: new[] { "RecipeId", "RecipeTypeId" },
                    values: new object[] { recipeId, recipeTypeId });
            }
        }

        public static void RemoveRecipeTypeAssignments(MigrationBuilder migrationBuilder)
        {
            // Remove all recipe type assignments for seed recipes (IDs 100-123)
            for (long recipeId = 100; recipeId <= 123; recipeId++)
            {
                long[] allTypeIds = new long[] {
                    RecipeTypeAppetizerId, RecipeTypeEntreeId, RecipeTypeStarchId,
                    RecipeTypeVegetableId, RecipeTypeSnackId, RecipeTypeDessertId
                };
                foreach (long typeId in allTypeIds)
                {
                    // Use Sql to avoid errors when row doesn't exist
                    migrationBuilder.Sql(
                        $"DELETE FROM recipe.\"recipe_type_index\" WHERE \"RecipeId\" = {recipeId} AND \"RecipeTypeId\" = {typeId};");
                }
            }
        }

        public static void AddHouseholdGroups(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "plan",
                table: "HouseholdGroup",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { DefaultHouseholdGroupId, "Default", "Default household group.", DateTime.UtcNow, SystemPersonId }
                });
        }

        public static void RemoveHouseholdGroups(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "plan",
                table: "HouseholdGroup",
                keyColumn: "Id",
                keyValues: new object[] { DefaultHouseholdGroupId });
        }

        public static void AddShoppingListGroups(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "shopping",
                table: "ShoppingListGroup",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { DefaultShoppingListGroupId, "Default", "Default shopping list group.", DateTime.UtcNow, SystemPersonId }
                });
        }

        public static void RemoveShoppingListGroups(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "shopping",
                table: "ShoppingListGroup",
                keyColumn: "Id",
                keyValues: new object[] { DefaultShoppingListGroupId });
        }

        public static void AddRestrictionTypes(MigrationBuilder migrationBuilder)
        {
            long restrictionGroupId = (long)ReferenceDiscriminatorEnum.RestrictionType;

            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 2000L, "Gluten-Free", "Excludes all gluten-containing grains (wheat, barley, rye).", DateTime.UtcNow, SystemPersonId },
                    { 2001L, "Dairy-Free", "Excludes all dairy products (milk, cheese, yogurt).", DateTime.UtcNow, SystemPersonId },
                    { 2002L, "Lactose-Intolerant", "Excludes lactose, common in dairy.", DateTime.UtcNow, SystemPersonId },
                    { 2003L, "Vegan", "Excludes all animal products (meat, dairy, eggs, honey).", DateTime.UtcNow, SystemPersonId },
                    { 2004L, "Vegetarian", "Excludes meat, poultry, and fish.", DateTime.UtcNow, SystemPersonId },
                    { 2005L, "Pescatarian", "Excludes meat and poultry, but includes fish and seafood.", DateTime.UtcNow, SystemPersonId },
                    { 2006L, "Keto", "Very low-carb, high-fat diet.", DateTime.UtcNow, SystemPersonId },
                    { 2007L, "Paleo", "Focuses on whole, unprocessed foods, mimicking ancestral diets.", DateTime.UtcNow, SystemPersonId },
                    { 2008L, "Mediterranean", "Emphasizes fruits, vegetables, whole grains, olive oil, lean proteins.", DateTime.UtcNow, SystemPersonId },
                    { 2009L, "Dash Diet", "Dietary Approaches to Stop Hypertension.", DateTime.UtcNow, SystemPersonId },
                    { 2010L, "Kosher", "Adheres to Jewish dietary laws.", DateTime.UtcNow, SystemPersonId },
                    { 2011L, "Halal", "Adheres to Islamic dietary laws.", DateTime.UtcNow, SystemPersonId },
                    { 2012L, "Nut Allergy", "Avoidance of nuts (peanuts, tree nuts).", DateTime.UtcNow, SystemPersonId },
                    { 2013L, "Egg Allergy", "Avoidance of eggs.", DateTime.UtcNow, SystemPersonId },
                    { 2014L, "Soy Allergy", "Avoidance of soy products.", DateTime.UtcNow, SystemPersonId },
                    { 2015L, "Fish Allergy", "Avoidance of fish.", DateTime.UtcNow, SystemPersonId },
                    { 2016L, "Shellfish Allergy", "Avoidance of shellfish.", DateTime.UtcNow, SystemPersonId },
                    { 2017L, "Sesame Allergy", "Avoidance of sesame.", DateTime.UtcNow, SystemPersonId },
                    { 2018L, "Corn Allergy", "Avoidance of corn.", DateTime.UtcNow, SystemPersonId },
                    { 2019L, "Sulfites Sensitivity", "Avoidance of sulfites.", DateTime.UtcNow, SystemPersonId }
                });

            foreach (long id in new long[] { 2000L, 2001L, 2002L, 2003L, 2004L, 2005L, 2006L, 2007L, 2008L, 2009L, 2010L, 2011L, 2012L, 2013L, 2014L, 2015L, 2016L, 2017L, 2018L, 2019L })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, restrictionGroupId });
            }
        }

        public static void RemoveRestrictionTypes(MigrationBuilder migrationBuilder)
        {
            long restrictionGroupId = (long)ReferenceDiscriminatorEnum.RestrictionType;
            foreach (long id in new long[] { 2000L, 2001L, 2002L, 2003L, 2004L, 2005L, 2006L, 2007L, 2008L, 2009L, 2010L, 2011L, 2012L, 2013L, 2014L, 2015L, 2016L, 2017L, 2018L, 2019L })
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, restrictionGroupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: new object[] { 2000L, 2001L, 2002L, 2003L, 2004L, 2005L, 2006L, 2007L, 2008L, 2009L, 2010L, 2011L, 2012L, 2013L, 2014L, 2015L, 2016L, 2017L, 2018L, 2019L });
        }

        public static void AddPlanInvitationRoles(MigrationBuilder migrationBuilder)
        {
            long planInvitationRoleGroupId = (long)ReferenceDiscriminatorEnum.PlanInvitationRole;

            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { PlanInvitationRoleAdminId, "Plan Admin", "A person who can manage plan settings, participants, and overall plan details.", DateTime.UtcNow, SystemPersonId },
                    { PlanInvitationRoleMemberId, "Plan Member", "A person who participates in the plan and has individual settings.", DateTime.UtcNow, SystemPersonId }
                });

            foreach (long id in new long[] { PlanInvitationRoleAdminId, PlanInvitationRoleMemberId })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, planInvitationRoleGroupId });
            }
        }

        public static void RemovePlanInvitationRoles(MigrationBuilder migrationBuilder)
        {
            long planInvitationRoleGroupId = (long)ReferenceDiscriminatorEnum.PlanInvitationRole;
            foreach (long id in new long[] { PlanInvitationRoleAdminId, PlanInvitationRoleMemberId })
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, planInvitationRoleGroupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: new object[] { PlanInvitationRoleAdminId, PlanInvitationRoleMemberId });
        }

        public static void AddMeasurementTypes(MigrationBuilder migrationBuilder)
        {
            // Ensure measurement schema exists
            migrationBuilder.EnsureSchema(name: "measurement");

            // Create measurement categories
            migrationBuilder.InsertData(
                schema: "measurement",
                table: "MeasurementCategory",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 1L, "Mass", "Units of mass/weight measurement", DateTime.UtcNow, SystemPersonId },
                    { 2L, "Volume", "Units of volume/capacity measurement", DateTime.UtcNow, SystemPersonId },
                    { 3L, "Count", "Units for counting items", DateTime.UtcNow, SystemPersonId },
                    { 4L, "Temperature", "Units of temperature measurement", DateTime.UtcNow, SystemPersonId },
                    { 5L, "Energy", "Units of energy measurement", DateTime.UtcNow, SystemPersonId }
                });

            // Create base measurements for each category
            migrationBuilder.InsertData(
                schema: "measurement",
                table: "Measurement",
                columns: new[] { "Id", "Name", "Description", "Symbol", "MeasurementCategoryId", "IsBaseUnit", "BaseUnitConversionFactor", "MeasurementType", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 1L, "Gram", "Base unit of mass in metric system", "g", 1L, true, 1.0m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 2L, "Milliliter", "Base unit of volume in metric system", "ml", 2L, true, 1.0m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 3L, "Piece", "Base unit for counting items", "pc", 3L, true, 1.0m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 4L, "Celsius", "Base unit of temperature in metric system", "°C", 4L, true, 1.0m, "Base", DateTime.UtcNow, SystemPersonId }
                });

            // Update categories with base unit references
            migrationBuilder.Sql(@"
                UPDATE measurement.""MeasurementCategory"" 
                SET ""BaseUnitId"" = 1 
                WHERE ""Id"" = 1;
                
                UPDATE measurement.""MeasurementCategory"" 
                SET ""BaseUnitId"" = 2 
                WHERE ""Id"" = 2;
                
                UPDATE measurement.""MeasurementCategory"" 
                SET ""BaseUnitId"" = 3 
                WHERE ""Id"" = 3;
                
                UPDATE measurement.""MeasurementCategory"" 
                SET ""BaseUnitId"" = 4 
                WHERE ""Id"" = 4;
            ");

            // Create common measurement units
            migrationBuilder.InsertData(
                schema: "measurement",
                table: "Measurement",
                columns: new[] { "Id", "Name", "Description", "Symbol", "MeasurementCategoryId", "IsBaseUnit", "BaseUnitConversionFactor", "MeasurementType", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    // Mass units
                    { 5L, "Kilogram", "1000 grams", "kg", 1L, false, 1000.0m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 6L, "Pound", "Imperial unit of mass", "lb", 1L, false, 453.592m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 7L, "Ounce", "Imperial unit of mass", "oz", 1L, false, 28.3495m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 8L, "Milligram", "0.001 grams", "mg", 1L, false, 0.001m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 9L, "Microgram", "0.000001 grams", "µg", 1L, false, 0.000001m, "Base", DateTime.UtcNow, SystemPersonId },

                    // Volume units
                    { 10L, "Liter", "1000 milliliters", "L", 2L, false, 1000.0m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 11L, "Cup", "US customary unit of volume", "cup", 2L, false, 236.588m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 12L, "Tablespoon", "US customary unit of volume", "tbsp", 2L, false, 14.7868m, "Base", DateTime.UtcNow, SystemPersonId },
                    { 13L, "Teaspoon", "US customary unit of volume", "tsp", 2L, false, 4.92892m, "Base", DateTime.UtcNow, SystemPersonId },

                    // Count units
                    { 14L, "Dozen", "12 pieces", "doz", 3L, false, 12.0m, "Base", DateTime.UtcNow, SystemPersonId },

                    // Temperature units
                    { 15L, "Fahrenheit", "Imperial unit of temperature", "°F", 4L, false, 1.0m, "Base", DateTime.UtcNow, SystemPersonId },

                    // Energy units (new category)
                    { 16L, "Kilocalorie", "Unit of energy", "kcal", 5L, false, 1.0m, "Base", DateTime.UtcNow, SystemPersonId }
                });

            // Create conversion rules
            migrationBuilder.InsertData(
                schema: "measurement",
                table: "MeasurementConversion",
                columns: new[] { "Id", "FromMeasurementId", "ToMeasurementId", "ConversionFactor", "IsDirectConversion", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    // Mass conversions
                    { 1L, 1L, 5L, 0.001m, true, DateTime.UtcNow, SystemPersonId }, // Gram to Kilogram
                    { 2L, 1L, 6L, 0.00220462m, true, DateTime.UtcNow, SystemPersonId }, // Gram to Pound
                    { 3L, 1L, 7L, 0.035274m, true, DateTime.UtcNow, SystemPersonId }, // Gram to Ounce

                    // Volume conversions
                    { 4L, 2L, 8L, 0.001m, true, DateTime.UtcNow, SystemPersonId }, // Milliliter to Liter
                    { 5L, 2L, 9L, 0.00422675m, true, DateTime.UtcNow, SystemPersonId }, // Milliliter to Cup
                    { 6L, 2L, 10L, 0.067628m, true, DateTime.UtcNow, SystemPersonId }, // Milliliter to Tablespoon
                    { 7L, 2L, 11L, 0.202884m, true, DateTime.UtcNow, SystemPersonId }, // Milliliter to Teaspoon

                    // Count conversions
                    { 8L, 3L, 12L, 0.0833333m, true, DateTime.UtcNow, SystemPersonId }, // Piece to Dozen

                    // Temperature conversions (Fahrenheit to Celsius)
                    { 9L, 13L, 4L, 0.555556m, true, DateTime.UtcNow, SystemPersonId }, // Fahrenheit to Celsius

                    // Energy conversions
                    { 16L, 16L, 16L, 1.0m, true, DateTime.UtcNow, SystemPersonId } // Kilocalorie to Kilocalorie
                });

            // Add offset for temperature conversion
            migrationBuilder.Sql(@"
                UPDATE measurement.""MeasurementConversion"" 
                SET ""Offset"" = -17.7778, ""Formula"" = '(°F - 32) × 5/9'
                WHERE ""Id"" = 9;
            ");
        }

        public static void RemoveMeasurementTypes(MigrationBuilder migrationBuilder)
        {
            // Remove conversion rules first (due to foreign key constraints)
            migrationBuilder.DeleteData(
                schema: "measurement",
                table: "MeasurementConversion",
                keyColumn: "Id",
                keyValues: new object[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L, 11L, 12L, 13L, 14L, 15L, 16L });

            // Remove measurements (both base and common units)
            migrationBuilder.DeleteData(
                schema: "measurement",
                table: "Measurement",
                keyColumn: "Id",
                keyValues: new object[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L, 11L, 12L, 13L, 14L, 15L, 16L });

            // Remove measurement categories
            migrationBuilder.DeleteData(
                schema: "measurement",
                table: "MeasurementCategory",
                keyColumn: "Id",
                keyValues: new object[] { 1L, 2L, 3L, 4L, 5L });
        }

        public static void AddGoalTypes(MigrationBuilder migrationBuilder)
        {
            long goalGroupId = (long)ReferenceDiscriminatorEnum.GoalType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { GoalTypeAdultsAndChildren4PlusId, "Adults and Children >= 4 years", "Dietary guidelines for general population as per FDA Nutrition Facts Label.", DateTime.UtcNow, SystemPersonId },
                    { GoalTypeInfantsThrough12MonthsId, "Infants through 12 months", "Dietary guidelines for infants.", DateTime.UtcNow, SystemPersonId },
                    { GoalTypeChildren1Through3YearsId, "Children 1 through 3 years", "Dietary guidelines for young children.", DateTime.UtcNow, SystemPersonId },
                    { GoalTypePregnantAndLactatingWomenId, "Pregnant and Lactating Women", "Dietary guidelines for pregnant and lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GoalTypeGeneralAdultId, "General Adult", "Broader dietary guidelines for typical healthy adults (Dietary Reference Intakes)).", DateTime.UtcNow, SystemPersonId }
                });
            long[] goalTypeIds = new long[] {
                GoalTypeAdultsAndChildren4PlusId, GoalTypeInfantsThrough12MonthsId, GoalTypeChildren1Through3YearsId,
                GoalTypePregnantAndLactatingWomenId, GoalTypeGeneralAdultId
            };
            foreach (long id in goalTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, goalGroupId });
            }
        }

        public static void RemoveGoalTypes(MigrationBuilder migrationBuilder)
        {
            long goalGroupId = (long)ReferenceDiscriminatorEnum.GoalType;
            long[] goalTypeIds = new long[] {
                GoalTypeAdultsAndChildren4PlusId, GoalTypeInfantsThrough12MonthsId, GoalTypeChildren1Through3YearsId,
                GoalTypePregnantAndLactatingWomenId, GoalTypeGeneralAdultId
            };
            foreach (long id in goalTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, goalGroupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: goalTypeIds.Cast<object>().ToArray());
        }

        public static void AddPrivacyConsentTypes(MigrationBuilder migrationBuilder)
        {
            long privacyConsentGroupId = (long)ReferenceDiscriminatorEnum.PrivacyConsentType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { PrivacyConsentTypeAnalyticsId, "Analytics", "Consent to use data for internal analytics to improve the service.", DateTime.UtcNow, SystemPersonId },
                    { PrivacyConsentTypeMarketingId, "Marketing", "Consent to receive marketing communications and offers.", DateTime.UtcNow, SystemPersonId },
                    { PrivacyConsentTypePersonalizationId, "Personalization", "Consent to use data to personalize content and recommendations.", DateTime.UtcNow, SystemPersonId }
                });
            long[] privacyConsentTypeIds = new long[] {
                PrivacyConsentTypeAnalyticsId, PrivacyConsentTypeMarketingId, PrivacyConsentTypePersonalizationId
            };
            foreach (long id in privacyConsentTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, privacyConsentGroupId });
            }
        }

        public static void RemovePrivacyConsentTypes(MigrationBuilder migrationBuilder)
        {
            long privacyConsentGroupId = (long)ReferenceDiscriminatorEnum.PrivacyConsentType;
            long[] privacyConsentTypeIds = new long[] {
                PrivacyConsentTypeAnalyticsId, PrivacyConsentTypeMarketingId, PrivacyConsentTypePersonalizationId
            };
            foreach (long id in privacyConsentTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, privacyConsentGroupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: privacyConsentTypeIds.Cast<object>().ToArray());
        }

        public static void AddCurationStatusTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.CurationStatusType;
            migrationBuilder.InsertData(
                schema: "reference", table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { CurationStatusTypeNonCuratedId, "Non-Curated", "Content has not been submitted for review.", DateTime.UtcNow, SystemPersonId },
                    { CurationStatusTypePendingCurationId, "Pending Curation", "Content is awaiting admin review.", DateTime.UtcNow, SystemPersonId },
                    { CurationStatusTypeRequiresRevisionId, "Requires Revision", "Admin has requested changes from the author.", DateTime.UtcNow, SystemPersonId },
                    { CurationStatusTypeCuratedId, "Approved", "Content has been approved and is publicly visible.", DateTime.UtcNow, SystemPersonId },
                    { CurationStatusTypeRejectedId, "Rejected", "Content was reviewed and not approved.", DateTime.UtcNow, SystemPersonId }
                });
            foreach (long id in new[] { CurationStatusTypeNonCuratedId, CurationStatusTypePendingCurationId, CurationStatusTypeRequiresRevisionId, CurationStatusTypeCuratedId, CurationStatusTypeRejectedId })
            {
                migrationBuilder.InsertData(schema: "reference", table: "ReferenceIndex", columns: new[] { "ReferenceId", "GroupId" }, values: new object[] { id, groupId });
            }
        }

        public static void RemoveCurationStatusTypes(MigrationBuilder migrationBuilder)
        {
            long[] ids = new[] { CurationStatusTypeNonCuratedId, CurationStatusTypePendingCurationId, CurationStatusTypeRequiresRevisionId, CurationStatusTypeCuratedId, CurationStatusTypeRejectedId };
            foreach (long id in ids) { migrationBuilder.DeleteData(schema: "reference", table: "ReferenceIndex", keyColumns: new[] { "ReferenceId", "GroupId" }, keyValues: new object[] { id, (long)ReferenceDiscriminatorEnum.CurationStatusType }); }
            migrationBuilder.DeleteData(schema: "reference", table: "Reference", keyColumn: "Id", keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddFeedbackEntityTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.FeedbackEntityType;
            migrationBuilder.InsertData(
                schema: "reference", table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { FeedbackEntityTypeRecipeId, "Recipe", "Feedback is related to a Recipe.", DateTime.UtcNow, SystemPersonId },
                    { FeedbackEntityTypeIngredientId, "Ingredient", "Feedback is related to an Ingredient.", DateTime.UtcNow, SystemPersonId }
                });
            foreach (long id in new[] { FeedbackEntityTypeRecipeId, FeedbackEntityTypeIngredientId })
            {
                migrationBuilder.InsertData(schema: "reference", table: "ReferenceIndex", columns: new[] { "ReferenceId", "GroupId" }, values: new object[] { id, groupId });
            }
        }

        public static void RemoveFeedbackEntityTypes(MigrationBuilder migrationBuilder)
        {
            long[] ids = new[] { FeedbackEntityTypeRecipeId, FeedbackEntityTypeIngredientId };
            foreach (long id in ids) { migrationBuilder.DeleteData(schema: "reference", table: "ReferenceIndex", keyColumns: new[] { "ReferenceId", "GroupId" }, keyValues: new object[] { id, (long)ReferenceDiscriminatorEnum.FeedbackEntityType }); }
            migrationBuilder.DeleteData(schema: "reference", table: "Reference", keyColumn: "Id", keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddFeedbackTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.FeedbackType;
            migrationBuilder.InsertData(
                schema: "reference", table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { FeedbackTypeApprovalId, "Approval", "Generic approval feedback for backward compatibility.", DateTime.UtcNow, SystemPersonId },
                    { FeedbackTypeApprovalPrivateId, "Approval (Private)", "Notes for the author upon approval, not publicly visible.", DateTime.UtcNow, SystemPersonId },
                    { FeedbackTypeApprovalPublicId, "Approval (Public)", "Notes that are publicly visible on the curated content.", DateTime.UtcNow, SystemPersonId },
                    { FeedbackTypeRevisionRequestId, "Revision Request", "Feedback asking the author to make changes.", DateTime.UtcNow, SystemPersonId },
                    { FeedbackTypeRejectionId, "Rejection", "Notes explaining why the content was rejected.", DateTime.UtcNow, SystemPersonId }
                });
            foreach (long id in new[] { FeedbackTypeApprovalId, FeedbackTypeApprovalPrivateId, FeedbackTypeApprovalPublicId, FeedbackTypeRevisionRequestId, FeedbackTypeRejectionId })
            {
                migrationBuilder.InsertData(schema: "reference", table: "ReferenceIndex", columns: new[] { "ReferenceId", "GroupId" }, values: new object[] { id, groupId });
            }
        }

        public static void RemoveFeedbackTypes(MigrationBuilder migrationBuilder)
        {
            long[] ids = new[] { FeedbackTypeApprovalId, FeedbackTypeApprovalPrivateId, FeedbackTypeApprovalPublicId, FeedbackTypeRevisionRequestId, FeedbackTypeRejectionId };
            foreach (long id in ids) { migrationBuilder.DeleteData(schema: "reference", table: "ReferenceIndex", keyColumns: new[] { "ReferenceId", "GroupId" }, keyValues: new object[] { id, (long)ReferenceDiscriminatorEnum.FeedbackType }); }
            migrationBuilder.DeleteData(schema: "reference", table: "Reference", keyColumn: "Id", keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddNutrientTypes(MigrationBuilder migrationBuilder)
        {
            // First, insert all parent nutrients
            migrationBuilder.InsertData(
                schema: "nutrient",
                table: "Nutrient",
                columns: new[] { "Id", "Name", "Description", "DefaultMeasurementId", "CreatedDate", "CreatedByPersonId", "ParentNutrientId" },
                values: new object[,]
                {
                    { NutrientFatId, "Fat", "Total fat content.", 1L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientTotalCarbohydratesId, "Total Carbohydrates", "Total carbohydrate content.", 1L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientProteinId, "Protein", "Protein content.", 1L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCholesterolId, "Cholesterol", "Cholesterol content.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientSodiumId, "Sodium", "Sodium content.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCaloriesFDCId, "Calories", "Energy content of food. Often referred to as 'Energy' in FDC API.", 16L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminAId, "Vitamin A", "Vitamin A, Retinol Activity Equivalents (RAE).", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminCId, "Vitamin C", "Ascorbic acid.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminDId, "Vitamin D", "Vitamin D.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminEId, "Vitamin E", "Vitamin E, alpha-tocopherol equivalents.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminKId, "Vitamin K", "Vitamin K.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientThiaminId, "Thiamin", "Vitamin B1.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientRiboflavinId, "Riboflavin", "Vitamin B2.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientNiacinId, "Niacin", "Vitamin B3, Niacin Equivalents (NE).", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminB6Id, "Vitamin B6", "Pyridoxine.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientFolateId, "Folate", "Folate, Dietary Folate Equivalents (DFE).", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminB12Id, "Vitamin B12", "Cobalamin.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientBiotinId, "Biotin", "Vitamin B7.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientPantothenicAcidId, "Pantothenic Acid", "Vitamin B5.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCholineId, "Choline", "Choline.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCalciumId, "Calcium", "Calcium.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientIronId, "Iron", "Iron.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientPhosphorusId, "Phosphorus", "Phosphorus.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientIodineId, "Iodine", "Iodine.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientMagnesiumId, "Magnesium", "Magnesium.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientZincId, "Zinc", "Zinc.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientSeleniumId, "Selenium", "Selenium.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCopperId, "Copper", "Copper.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientManganeseId, "Manganese", "Manganese.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientChromiumId, "Chromium", "Chromium.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientMolybdenumId, "Molybdenum", "Molybdenum.", 9L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientChlorideId, "Chloride", "Chloride.", 8L, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientPotassiumId, "Potassium", "Potassium.", 8L, DateTime.UtcNow, SystemPersonId, null }
                });

            // Now, insert child nutrients, linking them to their parents
            migrationBuilder.InsertData(
                schema: "nutrient",
                table: "Nutrient",
                columns: new[] { "Id", "Name", "Description", "DefaultMeasurementId", "CreatedDate", "CreatedByPersonId", "ParentNutrientId" },
                values: new object[,]
                {
                    { NutrientSaturatedFatId, "Saturated Fat", "Saturated fatty acids.", 1L, DateTime.UtcNow, SystemPersonId, NutrientFatId },
                    { NutrientDietaryFiberId, "Dietary Fiber", "Dietary fiber content.", 1L, DateTime.UtcNow, SystemPersonId, NutrientTotalCarbohydratesId },
                    { NutrientAddedSugarsId, "Added Sugars", "Added sugars content.", 1L, DateTime.UtcNow, SystemPersonId, NutrientTotalCarbohydratesId }
                });
        }

        public static void RemoveNutrientTypes(MigrationBuilder migrationBuilder)
        {
            long[] nutrientIds = new long[] {
                NutrientFatId, NutrientSaturatedFatId, NutrientCholesterolId, NutrientTotalCarbohydratesId,
                NutrientSodiumId, NutrientDietaryFiberId, NutrientProteinId, NutrientAddedSugarsId, NutrientCaloriesFDCId,
                NutrientVitaminAId, NutrientVitaminCId, NutrientVitaminDId, NutrientVitaminEId, NutrientVitaminKId,
                NutrientThiaminId, NutrientRiboflavinId, NutrientNiacinId, NutrientVitaminB6Id, NutrientFolateId,
                NutrientVitaminB12Id, NutrientBiotinId, NutrientPantothenicAcidId, NutrientCholineId,
                NutrientCalciumId, NutrientIronId, NutrientPhosphorusId, NutrientIodineId, NutrientMagnesiumId,
                NutrientZincId, NutrientSeleniumId, NutrientCopperId, NutrientManganeseId, NutrientChromiumId,
                NutrientMolybdenumId, NutrientChlorideId, NutrientPotassiumId
            };

            migrationBuilder.DeleteData(
                schema: "nutrient",
                table: "Nutrient",
                keyColumn: "Id",
                keyValues: nutrientIds.Cast<object>().ToArray());
        }

        public static void AddNutrientGuidelines(MigrationBuilder migrationBuilder)
        {
            long GetNextGuidelineId() => nextGuidelineId++;

            migrationBuilder.InsertData(
                schema: "nutrient",
                table: "NutrientGuideline",
                columns: new[] { "Id", "NutrientId", "GoalTypeId", "MeasurementId", "MinAmount", "MaxAmount", "RecommendedAmount", "Notes", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { GetNextGuidelineId(), NutrientFatId, GoalTypeAdultsAndChildren4PlusId, 1L, null, null, 78.0m, "DRV for Fat based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSaturatedFatId, GoalTypeAdultsAndChildren4PlusId, 1L, null, null, 20.0m, "DRV for Saturated Fat based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypeAdultsAndChildren4PlusId, 1L, null, null, 275.0m, "DRV for Total Carbohydrates based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSodiumId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 2300.0m, "DRV for Sodium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientDietaryFiberId, GoalTypeAdultsAndChildren4PlusId, 1L, null, null, 28.0m, "DRV for Dietary Fiber based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypeAdultsAndChildren4PlusId, 1L, null, null, 50.0m, "DRV for Protein based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientAddedSugarsId, GoalTypeAdultsAndChildren4PlusId, 1L, null, null, 50.0m, "DRV for Added Sugars based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFatId, GoalTypeInfantsThrough12MonthsId, 1L, null, null, 30.0m, "DRV for Fat.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypeInfantsThrough12MonthsId, 1L, null, null, 95.0m, "DRV for Total Carbohydrates.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFatId, GoalTypeChildren1Through3YearsId, 1L, null, null, 39.0m, "DRV for Fat based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSaturatedFatId, GoalTypeChildren1Through3YearsId, 1L, null, null, 10.0m, "DRV for Saturated Fat based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypeChildren1Through3YearsId, 8L, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypeChildren1Through3YearsId, 1L, null, null, 150.0m, "DRV for Total Carbohydrates based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSodiumId, GoalTypeChildren1Through3YearsId, 8L, null, null, 1500.0m, "DRV for Sodium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientDietaryFiberId, GoalTypeChildren1Through3YearsId, 1L, null, null, 14.0m, "DRV for Dietary Fiber based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypeChildren1Through3YearsId, 1L, null, null, 13.0m, "DRV for Protein based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientAddedSugarsId, GoalTypeChildren1Through3YearsId, 1L, null, null, 25.0m, "DRV for Added Sugars based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFatId, GoalTypePregnantAndLactatingWomenId, 1L, null, null, 78.0m, "DRV for Fat based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypePregnantAndLactatingWomenId, 1L, null, null, 275.0m, "DRV for Total Carbohydrates based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSodiumId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 2300.0m, "DRV for Sodium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientDietaryFiberId, GoalTypePregnantAndLactatingWomenId, 1L, null, null, 28.0m, "DRV for Dietary Fiber based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientAddedSugarsId, GoalTypePregnantAndLactatingWomenId, 1L, null, null, 50.0m, "DRV for Added Sugars based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 900.0m, "RDI for Vitamin A (RAE).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 90.0m, "RDI for Vitamin C.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 1300.0m, "RDI for Calcium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 18.0m, "RDI for Iron.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 20.0m, "RDI for Vitamin D.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 15.0m, "RDI for Vitamin E (alpha-tocopherol).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 120.0m, "RDI for Vitamin K.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 1.2m, "RDI for Thiamin (Vitamin B1).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 1.3m, "RDI for Riboflavin (Vitamin B2).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 16.0m, "RDI for Niacin (NE).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 1.7m, "RDI for Vitamin B6.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 400.0m, "RDI for Folate (DFE).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 2.4m, "RDI for Vitamin B12.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 30.0m, "RDI for Biotin.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 5.0m, "RDI for Pantothenic Acid.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 1250.0m, "RDI for Phosphorus.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 150.0m, "RDI for Iodine.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 420.0m, "RDI for Magnesium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 11.0m, "RDI for Zinc.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 55.0m, "RDI for Selenium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 0.9m, "RDI for Copper.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 2.3m, "RDI for Manganese.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 35.0m, "RDI for Chromium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypeAdultsAndChildren4PlusId, 9L, null, null, 45.0m, "RDI for Molybdenum.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 2300.0m, "RDI for Chloride.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 4700.0m, "RDI for Potassium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypeAdultsAndChildren4PlusId, 8L, null, null, 550.0m, "RDI for Choline.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 500.0m, "RDI for Vitamin A (RAE) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 50.0m, "RDI for Vitamin C for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 260.0m, "RDI for Calcium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 11.0m, "RDI for Iron for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 10.0m, "RDI for Vitamin D for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 5.0m, "RDI for Vitamin C for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 2.5m, "RDI for Vitamin K for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 0.3m, "RDI for Thiamin (Vitamin B1) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 0.4m, "RDI for Riboflavin (Vitamin B2) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 4.0m, "RDI for Niacin (NE) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 0.3m, "RDI for Vitamin B6 for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 80.0m, "RDI for Folate (DFE) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 0.5m, "RDI for Vitamin B12 for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 6.0m, "RDI for Biotin for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 1.8m, "RDI for Pantothenic Acid for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 275.0m, "RDI for Phosphorus for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 130.0m, "RDI for Iodine for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 75.0m, "RDI for Magnesium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 3.0m, "RDI for Zinc for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 20.0m, "RDI for Selenium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 0.2m, "RDI for Copper for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 0.6m, "RDI for Manganese for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 5.5m, "RDI for Chromium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypeInfantsThrough12MonthsId, 9L, null, null, 3.0m, "RDI for Molybdenum for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 570.0m, "RDI for Chloride for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 700.0m, "RDI for Potassium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypeInfantsThrough12MonthsId, 8L, null, null, 150.0m, "RDI for Choline for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypeInfantsThrough12MonthsId, 1L, null, null, 11.0m, "RDI for Protein for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypeChildren1Through3YearsId, 9L, null, null, 300.0m, "RDI for Vitamin A (RAE) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypeChildren1Through3YearsId, 8L, null, null, 15.0m, "RDI for Vitamin C for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypeChildren1Through3YearsId, 8L, null, null, 700.0m, "RDI for Calcium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypeChildren1Through3YearsId, 8L, null, null, 7.0m, "RDI for Iron for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypeChildren1Through3YearsId, 9L, null, null, 15.0m, "RDI for Vitamin D for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypeChildren1Through3YearsId, 8L, null, null, 6.0m, "RDI for Vitamin E for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypeChildren1Through3YearsId, 9L, null, null, 30.0m, "RDI for Vitamin K for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypeChildren1Through3YearsId, 8L, null, null, 0.5m, "RDI for Thiamin (Vitamin B1) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypeChildren1Through3YearsId, 8L, null, null, 0.5m, "RDI for Riboflavin (Vitamin B2) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypeChildren1Through3YearsId, 8L, null, null, 6.0m, "RDI for Niacin (NE) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypeChildren1Through3YearsId, 8L, null, null, 0.5m, "RDI for Vitamin B6 for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypeChildren1Through3YearsId, 9L, null, null, 150.0m, "RDI for Folate (DFE) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypeChildren1Through3YearsId, 9L, null, null, 0.9m, "RDI for Vitamin B12 for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypeChildren1Through3YearsId, 9L, null, null, 8.0m, "RDI for Biotin for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypeChildren1Through3YearsId, 8L, null, null, 2.0m, "RDI for Pantothenic Acid for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypeChildren1Through3YearsId, 8L, null, null, 460.0m, "RDI for Phosphorus for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypeChildren1Through3YearsId, 9L, null, null, 90.0m, "RDI for Iodine for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypeChildren1Through3YearsId, 8L, null, null, 80.0m, "RDI for Magnesium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypeChildren1Through3YearsId, 8L, null, null, 3.0m, "RDI for Zinc for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypeChildren1Through3YearsId, 9L, null, null, 20.0m, "RDI for Selenium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypeChildren1Through3YearsId, 8L, null, null, 0.3m, "RDI for Copper for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypeChildren1Through3YearsId, 8L, null, null, 1.2m, "RDI for Manganese for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypeChildren1Through3YearsId, 9L, null, null, 11.0m, "RDI for Chromium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypeChildren1Through3YearsId, 9L, null, null, 17.0m, "RDI for Molybdenum for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypeChildren1Through3YearsId, 8L, null, null, 1500.0m, "RDI for Chloride for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypeChildren1Through3YearsId, 8L, null, null, 3000.0m, "RDI for Potassium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypeChildren1Through3YearsId, 8L, null, null, 200.0m, "RDI for Choline for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 1300.0m, "RDI for Vitamin A (RAE) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 120.0m, "RDI for Vitamin C for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 1300.0m, "RDI for Calcium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 27.0m, "RDI for Iron for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 15.0m, "RDI for Vitamin D for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 19.0m, "RDI for Vitamin E for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 90.0m, "RDI for Vitamin K for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 1.4m, "RDI for Thiamin (Vitamin B1) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 1.6m, "RDI for Riboflavin (Vitamin B2) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 18.0m, "RDI for Niacin (NE) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 2.0m, "RDI for Vitamin B6 for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 600.0m, "RDI for Folate (DFE) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 2.8m, "RDI for Vitamin B12 for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 35.0m, "RDI for Biotin for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 7.0m, "RDI for Pantothenic Acid for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 1250.0m, "RDI for Phosphorus for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 290.0m, "RDI for Iodine for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 400.0m, "RDI for Magnesium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 13.0m, "RDI for Zinc for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 70.0m, "RDI for Selenium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 1.3m, "RDI for Copper for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 2.6m, "RDI for Manganese for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 45.0m, "RDI for Chromium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypePregnantAndLactatingWomenId, 9L, null, null, 50.0m, "RDI for Molybdenum for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 2300.0m, "RDI for Chloride for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 5100.0m, "RDI for Potassium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypePregnantAndLactatingWomenId, 8L, null, null, 550.0m, "RDI for Choline for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypePregnantAndLactatingWomenId, 1L, null, null, 71.0m, "RDI for Protein for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId }
                });
        }

        public static void RemoveNutrientGuidelines(MigrationBuilder migrationBuilder)
        {
            var guidelineIdsToRemove = Enumerable.Range(0, (int)(nextGuidelineId - 7000L))
                                                 .Select(i => 7000L + i)
                                                 .Cast<object>()
                                                 .ToArray();

            migrationBuilder.DeleteData(
                schema: "nutrient",
                table: "NutrientGuideline",
                keyColumn: "Id",
                keyValues: guidelineIdsToRemove);
        }

        // Recipe Event Types
        public static void AddRecipeEventTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeEventType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { RecipeEventTypeCreatedId, "Recipe Created", "Recipe was initially created", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeUpdatedId, "Recipe Updated", "Recipe was modified", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypePublishedId, "Recipe Published", "Recipe was made public", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeRatedId, "Recipe Rated", "Recipe received a rating", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeCommentedId, "Recipe Commented", "Recipe received a comment", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeMadeId, "Recipe Made", "Recipe was prepared/cooked", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeSharedId, "Recipe Shared", "Recipe was shared with others", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeFavoritedId, "Recipe Favorited", "Recipe was added to favorites", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeAddedToPlanId, "Recipe Added to Plan", "Recipe was added to meal plan", DateTime.UtcNow, SystemPersonId },
                    { RecipeEventTypeExportedId, "Recipe Exported", "Recipe was exported to external format", DateTime.UtcNow, SystemPersonId }
                });

            long[] eventTypeIds = new long[] {
                RecipeEventTypeCreatedId, RecipeEventTypeUpdatedId, RecipeEventTypePublishedId,
                RecipeEventTypeRatedId, RecipeEventTypeCommentedId, RecipeEventTypeMadeId,
                RecipeEventTypeSharedId, RecipeEventTypeFavoritedId, RecipeEventTypeAddedToPlanId,
                RecipeEventTypeExportedId
            };

            foreach (long id in eventTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveRecipeEventTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeEventType;
            long[] eventTypeIds = new long[] {
                RecipeEventTypeCreatedId, RecipeEventTypeUpdatedId, RecipeEventTypePublishedId,
                RecipeEventTypeRatedId, RecipeEventTypeCommentedId, RecipeEventTypeMadeId,
                RecipeEventTypeSharedId, RecipeEventTypeFavoritedId, RecipeEventTypeAddedToPlanId,
                RecipeEventTypeExportedId
            };

            foreach (long id in eventTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: eventTypeIds.Cast<object>().ToArray());
        }

        // Recipe Status Types
        public static void AddRecipeStatusTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeStatusType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { RecipeStatusTypeDraftId, "Draft", "Recipe is in draft mode and not publicly visible", DateTime.UtcNow, SystemPersonId },
                    { RecipeStatusTypePublishedId, "Published", "Recipe is published and publicly visible", DateTime.UtcNow, SystemPersonId },
                    { RecipeStatusTypeArchivedId, "Archived", "Recipe is archived and no longer actively maintained", DateTime.UtcNow, SystemPersonId },
                    { RecipeStatusTypeDeletedId, "Deleted", "Recipe has been marked for deletion", DateTime.UtcNow, SystemPersonId }
                });

            long[] statusTypeIds = new long[] {
                RecipeStatusTypeDraftId, RecipeStatusTypePublishedId, RecipeStatusTypeArchivedId, RecipeStatusTypeDeletedId
            };

            foreach (long id in statusTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveRecipeStatusTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeStatusType;
            long[] statusTypeIds = new long[] {
                RecipeStatusTypeDraftId, RecipeStatusTypePublishedId, RecipeStatusTypeArchivedId, RecipeStatusTypeDeletedId
            };

            foreach (long id in statusTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: statusTypeIds.Cast<object>().ToArray());
        }

        // Recipe Share Token Types
        public static void AddRecipeShareTokenTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeShareTokenType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { RecipeShareTokenTypePublicId, "Public", "Recipe can be accessed by anyone with the share token", DateTime.UtcNow, SystemPersonId },
                    { RecipeShareTokenTypePrivateId, "Private", "Recipe can only be accessed by specific users", DateTime.UtcNow, SystemPersonId },
                    { RecipeShareTokenTypeTemporaryId, "Temporary", "Recipe share token expires after a set time", DateTime.UtcNow, SystemPersonId }
                });

            long[] shareTokenTypeIds = new long[] {
                RecipeShareTokenTypePublicId, RecipeShareTokenTypePrivateId, RecipeShareTokenTypeTemporaryId
            };

            foreach (long id in shareTokenTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveRecipeShareTokenTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeShareTokenType;
            long[] shareTokenTypeIds = new long[] {
                RecipeShareTokenTypePublicId, RecipeShareTokenTypePrivateId, RecipeShareTokenTypeTemporaryId
            };

            foreach (long id in shareTokenTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: shareTokenTypeIds.Cast<object>().ToArray());
        }

        // Recipe Comment Types
        public static void AddRecipeCommentTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeCommentType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { RecipeCommentTypeGeneralId, "General", "General comment about the recipe", DateTime.UtcNow, SystemPersonId },
                    { RecipeCommentTypeReviewId, "Review", "Review or rating comment", DateTime.UtcNow, SystemPersonId },
                    { RecipeCommentTypeSuggestionId, "Suggestion", "Suggestion for improving the recipe", DateTime.UtcNow, SystemPersonId },
                    { RecipeCommentTypeQuestionId, "Question", "Question about the recipe", DateTime.UtcNow, SystemPersonId }
                });

            long[] commentTypeIds = new long[] {
                RecipeCommentTypeGeneralId, RecipeCommentTypeReviewId, RecipeCommentTypeSuggestionId, RecipeCommentTypeQuestionId
            };

            foreach (long id in commentTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveRecipeCommentTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeCommentType;
            long[] commentTypeIds = new long[] {
                RecipeCommentTypeGeneralId, RecipeCommentTypeReviewId, RecipeCommentTypeSuggestionId, RecipeCommentTypeQuestionId
            };

            foreach (long id in commentTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: commentTypeIds.Cast<object>().ToArray());
        }

        // Recipe Note Types
        public static void AddRecipeNoteTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeNoteType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { RecipeNoteTypePrivateId, "Private", "Private note only visible to the author", DateTime.UtcNow, SystemPersonId },
                    { RecipeNoteTypePublicId, "Public", "Public note visible to all users", DateTime.UtcNow, SystemPersonId },
                    { RecipeNoteTypeCookingTipId, "Cooking Tip", "Tip for cooking the recipe", DateTime.UtcNow, SystemPersonId },
                    { RecipeNoteTypeVariationId, "Variation", "Variation or modification of the recipe", DateTime.UtcNow, SystemPersonId },
                    { RecipeNoteTypeSubstitutionId, "Substitution", "Ingredient substitution suggestion", DateTime.UtcNow, SystemPersonId }
                });

            long[] noteTypeIds = new long[] {
                RecipeNoteTypePrivateId, RecipeNoteTypePublicId, RecipeNoteTypeCookingTipId,
                RecipeNoteTypeVariationId, RecipeNoteTypeSubstitutionId
            };

            foreach (long id in noteTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveRecipeNoteTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeNoteType;
            long[] noteTypeIds = new long[] {
                RecipeNoteTypePrivateId, RecipeNoteTypePublicId, RecipeNoteTypeCookingTipId,
                RecipeNoteTypeVariationId, RecipeNoteTypeSubstitutionId
            };

            foreach (long id in noteTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: noteTypeIds.Cast<object>().ToArray());
        }

        public static void CreateReferenceGroupView(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW reference.""ReferenceGroupView"" AS
                SELECT
                    ref.""Id"" AS ""ReferenceId"",
                    ref.""Name"" AS ""ReferenceName"",
                    ref.""Description"" AS ""ReferenceDescription"",
                    grp.""Id"" AS ""GroupId"",
                    grp.""Name"" AS ""GroupName"",
                    grp.""Description"" AS ""GroupDescription""
                FROM
                    reference.""Reference"" AS ref
                INNER JOIN
                    reference.""ReferenceIndex"" AS idx ON ref.""Id"" = idx.""ReferenceId""
                INNER JOIN
                    reference.""Group"" AS grp ON grp.""Id"" = idx.""GroupId"";
            ");
        }

        public static void DropReferenceGroupView(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS reference.""ReferenceGroupView"";");
        }

        // UI Data Conversion Seeding Methods

        public static void AddShoppingPriorityTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.ShoppingPriorityType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { ShoppingPriorityLowId, "Low", "Low priority shopping item", DateTime.UtcNow, SystemPersonId },
                    { ShoppingPriorityMediumId, "Medium", "Medium priority shopping item", DateTime.UtcNow, SystemPersonId },
                    { ShoppingPriorityHighId, "High", "High priority shopping item", DateTime.UtcNow, SystemPersonId }
                });

            long[] priorityIds = new long[] { ShoppingPriorityLowId, ShoppingPriorityMediumId, ShoppingPriorityHighId };
            foreach (long id in priorityIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void AddShoppingCategoryTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.ShoppingCategoryType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { ShoppingCategoryProduceId, "Produce", "Fresh fruits and vegetables", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategoryDairyId, "Dairy", "Milk, cheese, yogurt, and other dairy products", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategoryMeatId, "Meat", "Fresh meat, poultry, and fish", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategoryPantryId, "Pantry", "Dry goods, canned foods, and staples", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategoryFrozenId, "Frozen", "Frozen foods and ice cream", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategoryBeveragesId, "Beverages", "Drinks, juices, and sodas", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategorySnacksId, "Snacks", "Chips, crackers, and other snack foods", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategoryHouseholdId, "Household", "Cleaning supplies and household items", DateTime.UtcNow, SystemPersonId },
                    { ShoppingCategoryOtherId, "Other", "Miscellaneous items not in other categories", DateTime.UtcNow, SystemPersonId }
                });

            long[] categoryIds = new long[] {
                ShoppingCategoryProduceId, ShoppingCategoryDairyId, ShoppingCategoryMeatId, ShoppingCategoryPantryId,
                ShoppingCategoryFrozenId, ShoppingCategoryBeveragesId, ShoppingCategorySnacksId, ShoppingCategoryHouseholdId,
                ShoppingCategoryOtherId
            };
            foreach (long id in categoryIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void AddRecipeDifficultyTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeDifficultyType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { RecipeDifficultyEasyId, "Easy", "Simple recipe suitable for beginners", DateTime.UtcNow, SystemPersonId },
                    { RecipeDifficultyMediumId, "Medium", "Moderate difficulty recipe", DateTime.UtcNow, SystemPersonId },
                    { RecipeDifficultyHardId, "Hard", "Complex recipe for experienced cooks", DateTime.UtcNow, SystemPersonId }
                });

            long[] difficultyIds = new long[] { RecipeDifficultyEasyId, RecipeDifficultyMediumId, RecipeDifficultyHardId };
            foreach (long id in difficultyIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void AddPersonActivityLevelTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonActivityLevelType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { PersonActivityLevelSedentaryId, "Sedentary", "Little or no exercise", DateTime.UtcNow, SystemPersonId },
                    { PersonActivityLevelLightlyActiveId, "Lightly Active", "Light exercise/sports 1-3 days/week", DateTime.UtcNow, SystemPersonId },
                    { PersonActivityLevelModeratelyActiveId, "Moderately Active", "Moderate exercise/sports 3-5 days/week", DateTime.UtcNow, SystemPersonId },
                    { PersonActivityLevelVeryActiveId, "Very Active", "Hard exercise/sports 6-7 days a week", DateTime.UtcNow, SystemPersonId },
                    { PersonActivityLevelExtremelyActiveId, "Extremely Active", "Very hard exercise/sports & physical job", DateTime.UtcNow, SystemPersonId }
                });

            long[] activityLevelIds = new long[] {
                PersonActivityLevelSedentaryId, PersonActivityLevelLightlyActiveId, PersonActivityLevelModeratelyActiveId,
                PersonActivityLevelVeryActiveId, PersonActivityLevelExtremelyActiveId
            };
            foreach (long id in activityLevelIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void AddDayOfWeekTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.DayOfWeekType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { DayOfWeekMondayId, "Monday", "First day of the week", DateTime.UtcNow, SystemPersonId },
                    { DayOfWeekTuesdayId, "Tuesday", "Second day of the week", DateTime.UtcNow, SystemPersonId },
                    { DayOfWeekWednesdayId, "Wednesday", "Third day of the week", DateTime.UtcNow, SystemPersonId },
                    { DayOfWeekThursdayId, "Thursday", "Fourth day of the week", DateTime.UtcNow, SystemPersonId },
                    { DayOfWeekFridayId, "Friday", "Fifth day of the week", DateTime.UtcNow, SystemPersonId },
                    { DayOfWeekSaturdayId, "Saturday", "Sixth day of the week", DateTime.UtcNow, SystemPersonId },
                    { DayOfWeekSundayId, "Sunday", "Seventh day of the week", DateTime.UtcNow, SystemPersonId }
                });

            long[] dayOfWeekIds = new long[] {
                DayOfWeekMondayId, DayOfWeekTuesdayId, DayOfWeekWednesdayId, DayOfWeekThursdayId,
                DayOfWeekFridayId, DayOfWeekSaturdayId, DayOfWeekSundayId
            };
            foreach (long id in dayOfWeekIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void AddPersonDietaryRestrictionTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonDietaryRestrictionType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 60050L, "Vegan", "Excludes all animal products", DateTime.UtcNow, SystemPersonId },
                    { 60051L, "Vegetarian", "Excludes meat, poultry, and fish", DateTime.UtcNow, SystemPersonId },
                    { 60052L, "Pescatarian", "Excludes meat and poultry but includes fish", DateTime.UtcNow, SystemPersonId },
                    { 60053L, "Flexitarian", "Primarily vegetarian with occasional meat", DateTime.UtcNow, SystemPersonId },
                    { 60054L, "Raw Vegan", "Uncooked, unprocessed plant foods only", DateTime.UtcNow, SystemPersonId },
                    { 60055L, "Fruitarian", "Primarily fruits, nuts, and seeds", DateTime.UtcNow, SystemPersonId },
                    { 60056L, "Keto", "Very low-carb, high-fat diet", DateTime.UtcNow, SystemPersonId },
                    { 60057L, "Paleo", "Whole, unprocessed foods mimicking ancestral diets", DateTime.UtcNow, SystemPersonId },
                    { 60058L, "Whole30", "30-day elimination diet removing sugar, grains, dairy, and legumes", DateTime.UtcNow, SystemPersonId },
                    { 60059L, "Carnivore", "Animal products only, no plant foods", DateTime.UtcNow, SystemPersonId },
                    { 60060L, "Mediterranean", "Emphasizes fruits, vegetables, whole grains, olive oil", DateTime.UtcNow, SystemPersonId },
                    { 60061L, "DASH", "Dietary Approaches to Stop Hypertension", DateTime.UtcNow, SystemPersonId },
                    { 60062L, "MIND", "Mediterranean-DASH hybrid for brain health", DateTime.UtcNow, SystemPersonId },
                    { 60063L, "Low-FODMAP", "Reduces fermentable carbohydrates for digestive health", DateTime.UtcNow, SystemPersonId },
                    { 60064L, "Low-Carb", "Reduced carbohydrate intake", DateTime.UtcNow, SystemPersonId },
                    { 60065L, "Low-Fat", "Reduced fat intake", DateTime.UtcNow, SystemPersonId },
                    { 60066L, "Low-Sodium", "Reduced sodium/salt intake", DateTime.UtcNow, SystemPersonId },
                    { 60067L, "High-Protein", "Emphasizes protein-rich foods", DateTime.UtcNow, SystemPersonId },
                    { 60068L, "Anti-Inflammatory", "Foods that reduce chronic inflammation", DateTime.UtcNow, SystemPersonId },
                    { 60069L, "Autoimmune Protocol (AIP)", "Elimination diet for autoimmune conditions", DateTime.UtcNow, SystemPersonId },
                    { 60070L, "Elimination Diet", "Systematic removal and reintroduction of foods", DateTime.UtcNow, SystemPersonId },
                    { 60071L, "Macrobiotic", "Whole grains, vegetables, and fermented foods", DateTime.UtcNow, SystemPersonId },
                    { 60072L, "Nordic", "Emphasizes Nordic staples: fish, whole grains, root vegetables", DateTime.UtcNow, SystemPersonId },
                    { 60073L, "Volumetrics", "Focus on low-calorie-density foods for satiety", DateTime.UtcNow, SystemPersonId },
                    { 60074L, "Zone Diet", "Balanced macronutrient ratios (40/30/30)", DateTime.UtcNow, SystemPersonId },
                    { 60075L, "South Beach", "Phased approach emphasizing good carbs and fats", DateTime.UtcNow, SystemPersonId },
                    { 60076L, "Intermittent Fasting", "Time-restricted eating patterns", DateTime.UtcNow, SystemPersonId },
                    { 60077L, "Plant-Based", "Primarily plant-derived foods, may include some animal products", DateTime.UtcNow, SystemPersonId },
                    { 60078L, "Clean Eating", "Whole, minimally processed foods", DateTime.UtcNow, SystemPersonId },
                    { 60079L, "Sugar-Free", "Excludes added sugars", DateTime.UtcNow, SystemPersonId },
                    { 60080L, "Grain-Free", "Excludes all grains", DateTime.UtcNow, SystemPersonId },
                    { 60081L, "Lectin-Free", "Avoids lectin-containing foods", DateTime.UtcNow, SystemPersonId },
                    { 60082L, "Nightshade-Free", "Excludes nightshade vegetables (tomatoes, peppers, eggplant)", DateTime.UtcNow, SystemPersonId },
                    { 60083L, "Calorie-Restricted", "Controlled calorie intake for weight management", DateTime.UtcNow, SystemPersonId },
                    { 60084L, "Carb Cycling", "Alternating high and low carbohydrate days", DateTime.UtcNow, SystemPersonId }
                });

            foreach (long id in new long[] { 60050L, 60051L, 60052L, 60053L, 60054L, 60055L, 60056L, 60057L, 60058L, 60059L, 60060L, 60061L, 60062L, 60063L, 60064L, 60065L, 60066L, 60067L, 60068L, 60069L, 60070L, 60071L, 60072L, 60073L, 60074L, 60075L, 60076L, 60077L, 60078L, 60079L, 60080L, 60081L, 60082L, 60083L, 60084L })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemovePersonDietaryRestrictionTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonDietaryRestrictionType;
            long[] ids = new long[] { 60050L, 60051L, 60052L, 60053L, 60054L, 60055L, 60056L, 60057L, 60058L, 60059L, 60060L, 60061L, 60062L, 60063L, 60064L, 60065L, 60066L, 60067L, 60068L, 60069L, 60070L, 60071L, 60072L, 60073L, 60074L, 60075L, 60076L, 60077L, 60078L, 60079L, 60080L, 60081L, 60082L, 60083L, 60084L };
            foreach (long id in ids)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddAllergyTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.AllergyType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 60700L, "Milk Allergy", "Immune reaction to milk proteins (casein, whey)", DateTime.UtcNow, SystemPersonId },
                    { 60701L, "Egg Allergy", "Immune reaction to egg proteins", DateTime.UtcNow, SystemPersonId },
                    { 60702L, "Fish Allergy", "Immune reaction to finned fish", DateTime.UtcNow, SystemPersonId },
                    { 60703L, "Shellfish Allergy", "Immune reaction to crustaceans and mollusks", DateTime.UtcNow, SystemPersonId },
                    { 60704L, "Tree Nut Allergy", "Immune reaction to tree nuts", DateTime.UtcNow, SystemPersonId },
                    { 60705L, "Peanut Allergy", "Immune reaction to peanuts (a legume)", DateTime.UtcNow, SystemPersonId },
                    { 60706L, "Wheat Allergy", "Immune reaction to wheat proteins", DateTime.UtcNow, SystemPersonId },
                    { 60707L, "Soybean Allergy", "Immune reaction to soy proteins", DateTime.UtcNow, SystemPersonId },
                    { 60708L, "Sesame Allergy", "Immune reaction to sesame seeds", DateTime.UtcNow, SystemPersonId },
                    { 60709L, "Almond Allergy", "Specific tree nut allergy to almonds", DateTime.UtcNow, SystemPersonId },
                    { 60710L, "Cashew Allergy", "Specific tree nut allergy to cashews", DateTime.UtcNow, SystemPersonId },
                    { 60711L, "Walnut Allergy", "Specific tree nut allergy to walnuts", DateTime.UtcNow, SystemPersonId },
                    { 60712L, "Pecan Allergy", "Specific tree nut allergy to pecans", DateTime.UtcNow, SystemPersonId },
                    { 60713L, "Pistachio Allergy", "Specific tree nut allergy to pistachios", DateTime.UtcNow, SystemPersonId },
                    { 60714L, "Macadamia Allergy", "Specific tree nut allergy to macadamia nuts", DateTime.UtcNow, SystemPersonId },
                    { 60715L, "Brazil Nut Allergy", "Specific tree nut allergy to Brazil nuts", DateTime.UtcNow, SystemPersonId },
                    { 60716L, "Hazelnut Allergy", "Specific tree nut allergy to hazelnuts", DateTime.UtcNow, SystemPersonId },
                    { 60717L, "Pine Nut Allergy", "Specific tree nut allergy to pine nuts", DateTime.UtcNow, SystemPersonId },
                    { 60718L, "Shrimp Allergy", "Specific shellfish allergy to shrimp", DateTime.UtcNow, SystemPersonId },
                    { 60719L, "Crab Allergy", "Specific shellfish allergy to crab", DateTime.UtcNow, SystemPersonId },
                    { 60720L, "Lobster Allergy", "Specific shellfish allergy to lobster", DateTime.UtcNow, SystemPersonId },
                    { 60721L, "Clam Allergy", "Specific shellfish allergy to clams", DateTime.UtcNow, SystemPersonId },
                    { 60722L, "Mussel Allergy", "Specific shellfish allergy to mussels", DateTime.UtcNow, SystemPersonId },
                    { 60723L, "Oyster Allergy", "Specific shellfish allergy to oysters", DateTime.UtcNow, SystemPersonId },
                    { 60724L, "Scallop Allergy", "Specific shellfish allergy to scallops", DateTime.UtcNow, SystemPersonId },
                    { 60725L, "Corn Allergy", "Immune reaction to corn proteins", DateTime.UtcNow, SystemPersonId },
                    { 60726L, "Coconut Allergy", "Immune reaction to coconut", DateTime.UtcNow, SystemPersonId },
                    { 60727L, "Mustard Allergy", "Immune reaction to mustard seeds", DateTime.UtcNow, SystemPersonId },
                    { 60728L, "Celery Allergy", "Immune reaction to celery", DateTime.UtcNow, SystemPersonId },
                    { 60729L, "Lupin Allergy", "Immune reaction to lupin (a legume)", DateTime.UtcNow, SystemPersonId },
                    { 60730L, "Buckwheat Allergy", "Immune reaction to buckwheat", DateTime.UtcNow, SystemPersonId },
                    { 60731L, "Kiwi Allergy", "Immune reaction to kiwi fruit", DateTime.UtcNow, SystemPersonId },
                    { 60732L, "Banana Allergy", "Immune reaction to bananas", DateTime.UtcNow, SystemPersonId },
                    { 60733L, "Avocado Allergy", "Immune reaction to avocados", DateTime.UtcNow, SystemPersonId },
                    { 60734L, "Mango Allergy", "Immune reaction to mangoes", DateTime.UtcNow, SystemPersonId },
                    { 60735L, "Strawberry Allergy", "Immune reaction to strawberries", DateTime.UtcNow, SystemPersonId },
                    { 60736L, "Citrus Allergy", "Immune reaction to citrus fruits", DateTime.UtcNow, SystemPersonId },
                    { 60737L, "Lactose Intolerance", "Difficulty digesting lactose in dairy products", DateTime.UtcNow, SystemPersonId },
                    { 60738L, "Fructose Intolerance", "Difficulty absorbing fructose", DateTime.UtcNow, SystemPersonId },
                    { 60739L, "Histamine Intolerance", "Reduced ability to break down histamine in foods", DateTime.UtcNow, SystemPersonId },
                    { 60740L, "Sulfite Sensitivity", "Adverse reaction to sulfite preservatives", DateTime.UtcNow, SystemPersonId },
                    { 60741L, "Salicylate Sensitivity", "Sensitivity to salicylates found in many foods", DateTime.UtcNow, SystemPersonId },
                    { 60742L, "Tyramine Sensitivity", "Sensitivity to tyramine in aged and fermented foods", DateTime.UtcNow, SystemPersonId },
                    { 60743L, "Caffeine Sensitivity", "Heightened response to caffeine", DateTime.UtcNow, SystemPersonId },
                    { 60744L, "MSG Sensitivity", "Sensitivity to monosodium glutamate", DateTime.UtcNow, SystemPersonId },
                    { 60745L, "Nightshade Sensitivity", "Sensitivity to nightshade family vegetables", DateTime.UtcNow, SystemPersonId },
                    { 60746L, "FODMAP Sensitivity", "Sensitivity to fermentable short-chain carbohydrates", DateTime.UtcNow, SystemPersonId },
                    { 60747L, "Alpha-Gal Syndrome", "Delayed allergic reaction to red meat from tick bites", DateTime.UtcNow, SystemPersonId },
                    { 60748L, "Oral Allergy Syndrome", "Cross-reactive allergies between pollen and raw fruits/vegetables", DateTime.UtcNow, SystemPersonId },
                    { 60749L, "Gluten Sensitivity", "Non-celiac sensitivity to gluten proteins", DateTime.UtcNow, SystemPersonId }
                });

            foreach (long id in new long[] { 60700L, 60701L, 60702L, 60703L, 60704L, 60705L, 60706L, 60707L, 60708L, 60709L, 60710L, 60711L, 60712L, 60713L, 60714L, 60715L, 60716L, 60717L, 60718L, 60719L, 60720L, 60721L, 60722L, 60723L, 60724L, 60725L, 60726L, 60727L, 60728L, 60729L, 60730L, 60731L, 60732L, 60733L, 60734L, 60735L, 60736L, 60737L, 60738L, 60739L, 60740L, 60741L, 60742L, 60743L, 60744L, 60745L, 60746L, 60747L, 60748L, 60749L })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveAllergyTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.AllergyType;
            long[] ids = new long[] { 60700L, 60701L, 60702L, 60703L, 60704L, 60705L, 60706L, 60707L, 60708L, 60709L, 60710L, 60711L, 60712L, 60713L, 60714L, 60715L, 60716L, 60717L, 60718L, 60719L, 60720L, 60721L, 60722L, 60723L, 60724L, 60725L, 60726L, 60727L, 60728L, 60729L, 60730L, 60731L, 60732L, 60733L, 60734L, 60735L, 60736L, 60737L, 60738L, 60739L, 60740L, 60741L, 60742L, 60743L, 60744L, 60745L, 60746L, 60747L, 60748L, 60749L };
            foreach (long id in ids)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddMedicalConditionTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.MedicalConditionType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 60800L, "Celiac Disease", "Autoimmune disorder triggered by gluten", DateTime.UtcNow, SystemPersonId },
                    { 60801L, "Crohn's Disease", "Inflammatory bowel disease affecting the digestive tract", DateTime.UtcNow, SystemPersonId },
                    { 60802L, "Ulcerative Colitis", "Inflammatory bowel disease affecting the colon", DateTime.UtcNow, SystemPersonId },
                    { 60803L, "Irritable Bowel Syndrome (IBS)", "Chronic digestive condition with varied symptoms", DateTime.UtcNow, SystemPersonId },
                    { 60804L, "GERD", "Gastroesophageal reflux disease", DateTime.UtcNow, SystemPersonId },
                    { 60805L, "Type 1 Diabetes", "Autoimmune condition requiring insulin management", DateTime.UtcNow, SystemPersonId },
                    { 60806L, "Type 2 Diabetes", "Metabolic condition affecting blood sugar regulation", DateTime.UtcNow, SystemPersonId },
                    { 60807L, "Gestational Diabetes", "Diabetes occurring during pregnancy", DateTime.UtcNow, SystemPersonId },
                    { 60808L, "Prediabetes", "Blood sugar levels higher than normal but not yet diabetes", DateTime.UtcNow, SystemPersonId },
                    { 60809L, "Hypertension", "High blood pressure requiring dietary management", DateTime.UtcNow, SystemPersonId },
                    { 60810L, "Heart Disease", "Cardiovascular conditions requiring heart-healthy diet", DateTime.UtcNow, SystemPersonId },
                    { 60811L, "High Cholesterol", "Elevated cholesterol requiring dietary management", DateTime.UtcNow, SystemPersonId },
                    { 60812L, "Chronic Kidney Disease", "Kidney conditions requiring protein/mineral management", DateTime.UtcNow, SystemPersonId },
                    { 60813L, "Liver Disease", "Liver conditions requiring dietary modifications", DateTime.UtcNow, SystemPersonId },
                    { 60814L, "Phenylketonuria (PKU)", "Genetic disorder requiring phenylalanine restriction", DateTime.UtcNow, SystemPersonId },
                    { 60815L, "Galactosemia", "Genetic disorder requiring galactose restriction", DateTime.UtcNow, SystemPersonId },
                    { 60816L, "Fructose Malabsorption", "Impaired fructose transport in the intestine", DateTime.UtcNow, SystemPersonId },
                    { 60817L, "Gout", "Inflammatory arthritis requiring purine restriction", DateTime.UtcNow, SystemPersonId },
                    { 60818L, "Osteoporosis", "Bone density loss requiring calcium and vitamin D focus", DateTime.UtcNow, SystemPersonId },
                    { 60819L, "Iron-Deficiency Anemia", "Low iron requiring iron-rich diet", DateTime.UtcNow, SystemPersonId },
                    { 60820L, "Thyroid Disorders", "Thyroid conditions affecting metabolism and nutrition needs", DateTime.UtcNow, SystemPersonId },
                    { 60821L, "Diverticulitis", "Inflamed pouches in the digestive tract", DateTime.UtcNow, SystemPersonId },
                    { 60822L, "Gastroparesis", "Delayed stomach emptying requiring modified diet", DateTime.UtcNow, SystemPersonId },
                    { 60823L, "SIBO", "Small intestinal bacterial overgrowth", DateTime.UtcNow, SystemPersonId },
                    { 60824L, "Eosinophilic Esophagitis", "Immune-mediated esophageal condition", DateTime.UtcNow, SystemPersonId },
                    { 60825L, "PCOS", "Polycystic ovary syndrome with metabolic implications", DateTime.UtcNow, SystemPersonId },
                    { 60826L, "Endometriosis", "Condition that may benefit from anti-inflammatory diet", DateTime.UtcNow, SystemPersonId },
                    { 60827L, "Autoimmune Conditions", "General autoimmune disorders requiring dietary management", DateTime.UtcNow, SystemPersonId },
                    { 60828L, "Cancer Recovery", "Nutritional needs during or after cancer treatment", DateTime.UtcNow, SystemPersonId },
                    { 60829L, "Post-Bariatric Surgery", "Dietary requirements after weight loss surgery", DateTime.UtcNow, SystemPersonId },
                    { 60830L, "Eating Disorder Recovery", "Nutritional rehabilitation and recovery support", DateTime.UtcNow, SystemPersonId },
                    { 60831L, "Pregnancy", "Nutritional needs during pregnancy", DateTime.UtcNow, SystemPersonId },
                    { 60832L, "Breastfeeding", "Nutritional needs during lactation", DateTime.UtcNow, SystemPersonId },
                    { 60833L, "MCAS", "Mast cell activation syndrome requiring low-histamine diet", DateTime.UtcNow, SystemPersonId },
                    { 60834L, "Wilson's Disease", "Copper metabolism disorder requiring copper restriction", DateTime.UtcNow, SystemPersonId }
                });

            foreach (long id in new long[] { 60800L, 60801L, 60802L, 60803L, 60804L, 60805L, 60806L, 60807L, 60808L, 60809L, 60810L, 60811L, 60812L, 60813L, 60814L, 60815L, 60816L, 60817L, 60818L, 60819L, 60820L, 60821L, 60822L, 60823L, 60824L, 60825L, 60826L, 60827L, 60828L, 60829L, 60830L, 60831L, 60832L, 60833L, 60834L })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveMedicalConditionTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.MedicalConditionType;
            long[] ids = new long[] { 60800L, 60801L, 60802L, 60803L, 60804L, 60805L, 60806L, 60807L, 60808L, 60809L, 60810L, 60811L, 60812L, 60813L, 60814L, 60815L, 60816L, 60817L, 60818L, 60819L, 60820L, 60821L, 60822L, 60823L, 60824L, 60825L, 60826L, 60827L, 60828L, 60829L, 60830L, 60831L, 60832L, 60833L, 60834L };
            foreach (long id in ids)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddSocietalRestrictionTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.SocietalRestrictionType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 60900L, "Kosher", "Adheres to Jewish dietary laws (kashrut)", DateTime.UtcNow, SystemPersonId },
                    { 60901L, "Halal", "Adheres to Islamic dietary laws", DateTime.UtcNow, SystemPersonId },
                    { 60902L, "Jain", "Strict vegetarian diet excluding root vegetables", DateTime.UtcNow, SystemPersonId },
                    { 60903L, "Hindu Vegetarian", "Vegetarian diet following Hindu traditions", DateTime.UtcNow, SystemPersonId },
                    { 60904L, "Buddhist Vegetarian", "Vegetarian diet following Buddhist traditions", DateTime.UtcNow, SystemPersonId },
                    { 60905L, "Seventh-Day Adventist", "Health-focused diet recommended by the church", DateTime.UtcNow, SystemPersonId },
                    { 60906L, "Rastafarian Ital", "Natural, unprocessed diet following Rastafarian principles", DateTime.UtcNow, SystemPersonId },
                    { 60907L, "Orthodox Fasting", "Periodic fasting following Orthodox Christian traditions", DateTime.UtcNow, SystemPersonId },
                    { 60908L, "Ethical Vegan", "Avoiding animal products for ethical reasons", DateTime.UtcNow, SystemPersonId },
                    { 60909L, "Sustainability-Focused", "Prioritizing environmentally sustainable food choices", DateTime.UtcNow, SystemPersonId },
                    { 60910L, "Fair Trade Only", "Preference for fair trade certified products", DateTime.UtcNow, SystemPersonId },
                    { 60911L, "Locally Sourced", "Preference for locally produced foods", DateTime.UtcNow, SystemPersonId },
                    { 60912L, "Organic Only", "Preference for certified organic foods", DateTime.UtcNow, SystemPersonId },
                    { 60913L, "No Alcohol", "Avoidance of alcohol in cooking and beverages", DateTime.UtcNow, SystemPersonId }
                });

            foreach (long id in new long[] { 60900L, 60901L, 60902L, 60903L, 60904L, 60905L, 60906L, 60907L, 60908L, 60909L, 60910L, 60911L, 60912L, 60913L })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemoveSocietalRestrictionTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.SocietalRestrictionType;
            long[] ids = new long[] { 60900L, 60901L, 60902L, 60903L, 60904L, 60905L, 60906L, 60907L, 60908L, 60909L, 60910L, 60911L, 60912L, 60913L };
            foreach (long id in ids)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddPersonalPreferenceTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonalPreferenceType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 61000L, "No Spicy Food", "Avoidance of spicy or hot foods", DateTime.UtcNow, SystemPersonId },
                    { 61001L, "Mild Spice Only", "Preference for mild levels of spice", DateTime.UtcNow, SystemPersonId },
                    { 61002L, "No Raw Fish", "Avoidance of raw fish (sushi, sashimi)", DateTime.UtcNow, SystemPersonId },
                    { 61003L, "No Raw Meat", "Avoidance of raw or undercooked meat", DateTime.UtcNow, SystemPersonId },
                    { 61004L, "No Organ Meats", "Avoidance of offal and organ meats", DateTime.UtcNow, SystemPersonId },
                    { 61005L, "No Game Meat", "Avoidance of wild game meats", DateTime.UtcNow, SystemPersonId },
                    { 61006L, "No Insects", "Avoidance of insect-based foods (entomophagy)", DateTime.UtcNow, SystemPersonId },
                    { 61007L, "No Fermented Foods", "Avoidance of fermented foods (kimchi, sauerkraut, etc.)", DateTime.UtcNow, SystemPersonId },
                    { 61008L, "No Artificial Sweeteners", "Avoidance of synthetic sugar substitutes", DateTime.UtcNow, SystemPersonId },
                    { 61009L, "No Artificial Colors", "Avoidance of artificial food colorings", DateTime.UtcNow, SystemPersonId },
                    { 61010L, "No Preservatives", "Avoidance of chemical preservatives", DateTime.UtcNow, SystemPersonId },
                    { 61011L, "No GMO", "Preference for non-genetically modified foods", DateTime.UtcNow, SystemPersonId },
                    { 61012L, "No Processed Foods", "Avoidance of heavily processed foods", DateTime.UtcNow, SystemPersonId },
                    { 61013L, "No Fast Food", "Avoidance of fast food and takeout", DateTime.UtcNow, SystemPersonId },
                    { 61014L, "No Mushy Textures", "Aversion to soft or mushy food textures", DateTime.UtcNow, SystemPersonId },
                    { 61015L, "No Slimy Textures", "Aversion to slimy food textures", DateTime.UtcNow, SystemPersonId },
                    { 61016L, "No Cilantro", "Avoidance of cilantro (genetic taste aversion)", DateTime.UtcNow, SystemPersonId },
                    { 61017L, "No Olives", "Avoidance of olives", DateTime.UtcNow, SystemPersonId },
                    { 61018L, "No Mushrooms", "Avoidance of mushrooms", DateTime.UtcNow, SystemPersonId },
                    { 61019L, "No Onions", "Avoidance of onions", DateTime.UtcNow, SystemPersonId },
                    { 61020L, "No Garlic", "Avoidance of garlic", DateTime.UtcNow, SystemPersonId },
                    { 61021L, "Low-Waste Cooking", "Preference for minimal food waste cooking", DateTime.UtcNow, SystemPersonId },
                    { 61022L, "Budget-Friendly", "Preference for cost-effective ingredients", DateTime.UtcNow, SystemPersonId },
                    { 61023L, "Quick Prep Only", "Preference for recipes with short preparation time", DateTime.UtcNow, SystemPersonId },
                    { 61024L, "Meal Prep Friendly", "Preference for recipes suitable for batch cooking", DateTime.UtcNow, SystemPersonId }
                });

            foreach (long id in new long[] { 61000L, 61001L, 61002L, 61003L, 61004L, 61005L, 61006L, 61007L, 61008L, 61009L, 61010L, 61011L, 61012L, 61013L, 61014L, 61015L, 61016L, 61017L, 61018L, 61019L, 61020L, 61021L, 61022L, 61023L, 61024L })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemovePersonalPreferenceTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonalPreferenceType;
            long[] ids = new long[] { 61000L, 61001L, 61002L, 61003L, 61004L, 61005L, 61006L, 61007L, 61008L, 61009L, 61010L, 61011L, 61012L, 61013L, 61014L, 61015L, 61016L, 61017L, 61018L, 61019L, 61020L, 61021L, 61022L, 61023L, 61024L };
            foreach (long id in ids)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: ids.Cast<object>().ToArray());
        }

        public static void AddPersonHealthGoalTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonHealthGoalType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { PersonHealthGoalWeightLossId, "Weight Loss", "Goal to reduce body weight", DateTime.UtcNow, SystemPersonId },
                    { PersonHealthGoalWeightGainId, "Weight Gain", "Goal to increase body weight", DateTime.UtcNow, SystemPersonId },
                    { PersonHealthGoalMaintenanceId, "Maintenance", "Goal to maintain current body weight", DateTime.UtcNow, SystemPersonId },
                    { PersonHealthGoalMuscleGainId, "Muscle Gain", "Goal to build muscle mass", DateTime.UtcNow, SystemPersonId },
                    { PersonHealthGoalGeneralHealthId, "General Health", "Goal to improve overall health and nutrition", DateTime.UtcNow, SystemPersonId }
                });

            long[] healthGoalIds = new long[] {
                PersonHealthGoalWeightLossId, PersonHealthGoalWeightGainId, PersonHealthGoalMaintenanceId,
                PersonHealthGoalMuscleGainId, PersonHealthGoalGeneralHealthId
            };
            foreach (long id in healthGoalIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemovePersonHealthGoalTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonHealthGoalType;
            long[] healthGoalIds = new long[] {
                PersonHealthGoalWeightLossId, PersonHealthGoalWeightGainId, PersonHealthGoalMaintenanceId,
                PersonHealthGoalMuscleGainId, PersonHealthGoalGeneralHealthId
            };

            foreach (long id in healthGoalIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: healthGoalIds.Cast<object>().ToArray());
        }

        public static void AddPersonAttributeTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonAttributeType;
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { PersonAttributeTypeHeightId, "Height", "Person's height (stored in centimeters)", DateTime.UtcNow, SystemPersonId },
                    { PersonAttributeTypeWeightId, "Weight", "Person's weight (stored in kilograms)", DateTime.UtcNow, SystemPersonId },
                    { PersonAttributeTypeGenderId, "Gender", "Person's gender identity", DateTime.UtcNow, SystemPersonId },
                    { PersonAttributeTypeDateOfBirthId, "Date of Birth", "Person's date of birth (ISO date string)", DateTime.UtcNow, SystemPersonId },
                    { PersonAttributeTypeActivityLevelId, "Activity Level", "Person's physical activity level (value is a PersonActivityLevelType reference ID)", DateTime.UtcNow, SystemPersonId },
                    { PersonAttributeTypeHealthGoalId, "Health Goal", "Person's health goal (value is a PersonHealthGoalType reference ID)", DateTime.UtcNow, SystemPersonId },
                    { PersonAttributeTypeRMRId, "Resting Metabolic Rate", "Resting metabolic rate in kcal/day", DateTime.UtcNow, SystemPersonId },
                    { PersonAttributeTypeAMRId, "Active Metabolic Rate", "Active metabolic rate in kcal/day", DateTime.UtcNow, SystemPersonId }
                });

            long[] attributeTypeIds = new long[] {
                PersonAttributeTypeHeightId, PersonAttributeTypeWeightId,
                PersonAttributeTypeGenderId, PersonAttributeTypeDateOfBirthId,
                PersonAttributeTypeActivityLevelId, PersonAttributeTypeHealthGoalId,
                PersonAttributeTypeRMRId, PersonAttributeTypeAMRId
            };
            foreach (long id in attributeTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, groupId });
            }
        }

        public static void RemovePersonAttributeTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonAttributeType;
            long[] attributeTypeIds = new long[] {
                PersonAttributeTypeHeightId, PersonAttributeTypeWeightId,
                PersonAttributeTypeGenderId, PersonAttributeTypeDateOfBirthId,
                PersonAttributeTypeActivityLevelId, PersonAttributeTypeHealthGoalId,
                PersonAttributeTypeRMRId, PersonAttributeTypeAMRId
            };

            foreach (long id in attributeTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: attributeTypeIds.Cast<object>().ToArray());
        }

        // UI Data Conversion Removal Methods

        public static void RemoveShoppingPriorityTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.ShoppingPriorityType;
            long[] priorityIds = new long[] { ShoppingPriorityLowId, ShoppingPriorityMediumId, ShoppingPriorityHighId };
            
            foreach (long id in priorityIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: priorityIds.Cast<object>().ToArray());
        }

        public static void RemoveShoppingCategoryTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.ShoppingCategoryType;
            long[] categoryIds = new long[] {
                ShoppingCategoryProduceId, ShoppingCategoryDairyId, ShoppingCategoryMeatId, ShoppingCategoryPantryId,
                ShoppingCategoryFrozenId, ShoppingCategoryBeveragesId, ShoppingCategorySnacksId, ShoppingCategoryHouseholdId,
                ShoppingCategoryOtherId
            };
            
            foreach (long id in categoryIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: categoryIds.Cast<object>().ToArray());
        }

        public static void RemoveRecipeDifficultyTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.RecipeDifficultyType;
            long[] difficultyIds = new long[] { RecipeDifficultyEasyId, RecipeDifficultyMediumId, RecipeDifficultyHardId };
            
            foreach (long id in difficultyIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: difficultyIds.Cast<object>().ToArray());
        }

        public static void RemovePersonActivityLevelTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.PersonActivityLevelType;
            long[] activityLevelIds = new long[] {
                PersonActivityLevelSedentaryId, PersonActivityLevelLightlyActiveId, PersonActivityLevelModeratelyActiveId,
                PersonActivityLevelVeryActiveId, PersonActivityLevelExtremelyActiveId
            };
            
            foreach (long id in activityLevelIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: activityLevelIds.Cast<object>().ToArray());
        }

        public static void RemoveDayOfWeekTypes(MigrationBuilder migrationBuilder)
        {
            long groupId = (long)ReferenceDiscriminatorEnum.DayOfWeekType;
            long[] dayOfWeekIds = new long[] {
                DayOfWeekMondayId, DayOfWeekTuesdayId, DayOfWeekWednesdayId, DayOfWeekThursdayId,
                DayOfWeekFridayId, DayOfWeekSaturdayId, DayOfWeekSundayId
            };
            
            foreach (long id in dayOfWeekIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, groupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: dayOfWeekIds.Cast<object>().ToArray());
        }
        // =====================================================================
        // Sample Recipe Seed Data (from SeedData/recipes.json embedded resource)
        // =====================================================================

        private class SeedIngredientDto
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string PluralName { get; set; } = string.Empty;
        }

        private class SeedRecipeDto
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public long PrepTimeMinutes { get; set; }
            public long CookTimeMinutes { get; set; }
            public long Servings { get; set; }
            public long CategoryId { get; set; }
            public string Slug { get; set; } = string.Empty;
            public string? Image { get; set; }
            public List<SeedRecipeStepDto> Steps { get; set; } = new();
            public List<SeedRecipeIngredientDto> Ingredients { get; set; } = new();
            public List<SeedRecipeNutritionDto> Nutrition { get; set; } = new();
        }

        private class SeedRecipeStepDto
        {
            public int StepNumber { get; set; }
            public string Summary { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        private class SeedRecipeIngredientDto
        {
            public long IngredientId { get; set; }
            public decimal Quantity { get; set; }
            public long MeasurementId { get; set; }
            public string RawLine { get; set; } = string.Empty;
        }

        private class SeedRecipeNutritionDto
        {
            public long NutrientId { get; set; }
            public decimal Amount { get; set; }
            public string Unit { get; set; } = string.Empty;
            public decimal? DailyValuePercentage { get; set; }
        }

        private class SeedDataRoot
        {
            public List<SeedIngredientDto> Ingredients { get; set; } = new();
            public List<SeedRecipeDto> Recipes { get; set; } = new();
        }

        private static SeedDataRoot LoadSeedData()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .First(n => n.EndsWith("recipes.json"));

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            return JsonSerializer.Deserialize<SeedDataRoot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        }

        private static string EscapeSql(string value)
        {
            return value.Replace("'", "''");
        }

        public static void SeedSampleRecipes(MigrationBuilder migrationBuilder)
        {
            var data = LoadSeedData();
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            // 1. Insert ingredients
            foreach (var ing in data.Ingredients)
            {
                var nameNorm = EscapeSql(ing.Name.ToLowerInvariant());
                var pluralNorm = EscapeSql(ing.PluralName.ToLowerInvariant());
                migrationBuilder.Sql($@"
                    INSERT INTO recipe.""Ingredient"" (""Id"", ""Name"", ""PluralName"", ""NameNormalized"", ""PluralNameNormalized"", ""FdcDataType"", ""CurationStatusId"", ""AuthorId"", ""OnHand"", ""CreatedDate"", ""CreatedByPersonId"")
                    VALUES ({ing.Id}, '{EscapeSql(ing.Name)}', '{EscapeSql(ing.PluralName)}', '{nameNorm}', '{pluralNorm}', '', {CurationStatusTypeCuratedId}, {SystemPersonId}, false, '{now}', {SystemPersonId});
                ");
            }

            // Update ingredient sequence
            migrationBuilder.Sql(@"SELECT setval(pg_get_serial_sequence('recipe.""Ingredient""', 'Id'), (SELECT COALESCE(MAX(""Id""), 1) FROM recipe.""Ingredient""));");

            // 2. Insert recipes
            foreach (var recipe in data.Recipes)
            {
                var nameNorm = EscapeSql(recipe.Name.ToLowerInvariant());
                var descNorm = EscapeSql((recipe.Description ?? "").ToLowerInvariant());
                var imageVal = recipe.Image != null ? $"'{EscapeSql(recipe.Image)}'" : "NULL";
                migrationBuilder.Sql($@"
                    INSERT INTO recipe.""Recipe"" (""Id"", ""Name"", ""Description"", ""PrepTimeMinutes"", ""CookTimeMinutes"", ""Servings"", ""CurationStatusId"", ""AuthorId"", ""Version"", ""Slug"", ""Image"", ""NameNormalized"", ""DescriptionNormalized"", ""IsOcrRecipe"", ""CreatedDate"", ""CreatedByPersonId"")
                    VALUES ({recipe.Id}, '{EscapeSql(recipe.Name)}', '{EscapeSql(recipe.Description)}', {recipe.PrepTimeMinutes}, {recipe.CookTimeMinutes}, {recipe.Servings}, {CurationStatusTypeCuratedId}, {SystemPersonId}, 1, '{EscapeSql(recipe.Slug)}', {imageVal}, '{nameNorm}', '{descNorm}', false, '{now}', {SystemPersonId});
                ");

                // 3. Insert recipe steps
                foreach (var step in recipe.Steps)
                {
                    migrationBuilder.Sql($@"
                        INSERT INTO recipe.""RecipeStep"" (""RecipeId"", ""StepNumber"", ""Summary"", ""Description"", ""CreatedDate"", ""CreatedByPersonId"")
                        VALUES ({recipe.Id}, {step.StepNumber}, '{EscapeSql(step.Summary)}', '{EscapeSql(step.Description)}', '{now}', {SystemPersonId});
                    ");
                }

                // 4. Insert recipe ingredients
                foreach (var ing in recipe.Ingredients)
                {
                    migrationBuilder.Sql($@"
                        INSERT INTO recipe.""RecipeIngredient"" (""RecipeId"", ""IngredientId"", ""Quantity"", ""MeasurementId"", ""RawLine"", ""CreatedDate"", ""CreatedByPersonId"")
                        VALUES ({recipe.Id}, {ing.IngredientId}, {ing.Quantity}, {ing.MeasurementId}, '{EscapeSql(ing.RawLine)}', '{now}', {SystemPersonId});
                    ");
                }

                // 5. Insert recipe category
                migrationBuilder.Sql($@"
                    INSERT INTO recipe.""RecipeCategory"" (""RecipeId"", ""CategoryId"", ""CreatedDate"", ""CreatedByPersonId"")
                    VALUES ({recipe.Id}, {recipe.CategoryId}, '{now}', {SystemPersonId});
                ");

                // 6. Insert recipe nutrition
                foreach (var nut in recipe.Nutrition)
                {
                    var dvpVal = nut.DailyValuePercentage.HasValue
                        ? nut.DailyValuePercentage.Value.ToString("F2")
                        : "NULL";
                    migrationBuilder.Sql($@"
                        INSERT INTO recipe.""RecipeNutrition"" (""RecipeId"", ""NutrientId"", ""Amount"", ""Unit"", ""DailyValuePercentage"", ""CreatedDate"", ""CreatedByPersonId"")
                        VALUES ({recipe.Id}, {nut.NutrientId}, {nut.Amount.ToString("F4")}, '{EscapeSql(nut.Unit)}', {dvpVal}, '{now}', {SystemPersonId});
                    ");
                }
            }

            // Update recipe sequence
            migrationBuilder.Sql(@"SELECT setval(pg_get_serial_sequence('recipe.""Recipe""', 'Id'), (SELECT COALESCE(MAX(""Id""), 1) FROM recipe.""Recipe""));");
        }

        public static void RemoveSampleRecipes(MigrationBuilder migrationBuilder)
        {
            // Remove in reverse dependency order
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeNutrition"" WHERE ""RecipeId"" BETWEEN 100 AND 199;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeCategory"" WHERE ""RecipeId"" BETWEEN 100 AND 199;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeIngredient"" WHERE ""RecipeId"" BETWEEN 100 AND 199;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeStep"" WHERE ""RecipeId"" BETWEEN 100 AND 199;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""Recipe"" WHERE ""Id"" BETWEEN 100 AND 199;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""Ingredient"" WHERE ""Id"" BETWEEN 100 AND 199;");
        }

        // =====================================================================
        // Extended Recipe Seed Data (from SeedData/recipes-extended.json)
        // =====================================================================

        private class ExtendedSeedRecipeDto
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public long PrepTimeMinutes { get; set; }
            public long CookTimeMinutes { get; set; }
            public long Servings { get; set; }
            public long CategoryId { get; set; }
            public string Slug { get; set; } = string.Empty;
            public string? Image { get; set; }
            public long? RecipeTypeId { get; set; }
            public List<SeedRecipeStepDto> Steps { get; set; } = new();
            public List<SeedRecipeIngredientDto> Ingredients { get; set; } = new();
            public List<SeedRecipeNutritionDto> Nutrition { get; set; } = new();
        }

        private class ExtendedSeedDataRoot
        {
            public List<SeedIngredientDto> Ingredients { get; set; } = new();
            public List<ExtendedSeedRecipeDto> Recipes { get; set; } = new();
        }

        private static ExtendedSeedDataRoot LoadExtendedSeedData()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .First(n => n.EndsWith("recipes-extended.json"));

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            return JsonSerializer.Deserialize<ExtendedSeedDataRoot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        }

        public static void SeedExtendedRecipes(MigrationBuilder migrationBuilder)
        {
            var data = LoadExtendedSeedData();
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            // 1. Insert any new ingredients (if present)
            foreach (var ing in data.Ingredients)
            {
                var nameNorm = EscapeSql(ing.Name.ToLowerInvariant());
                var pluralNorm = EscapeSql(ing.PluralName.ToLowerInvariant());
                migrationBuilder.Sql($@"
                    INSERT INTO recipe.""Ingredient"" (""Id"", ""Name"", ""PluralName"", ""NameNormalized"", ""PluralNameNormalized"", ""FdcDataType"", ""CurationStatusId"", ""AuthorId"", ""OnHand"", ""CreatedDate"", ""CreatedByPersonId"")
                    VALUES ({ing.Id}, '{EscapeSql(ing.Name)}', '{EscapeSql(ing.PluralName)}', '{nameNorm}', '{pluralNorm}', '', {CurationStatusTypeCuratedId}, {SystemPersonId}, false, '{now}', {SystemPersonId});
                ");
            }

            // 2. Insert recipes
            foreach (var recipe in data.Recipes)
            {
                var nameNorm = EscapeSql(recipe.Name.ToLowerInvariant());
                var descNorm = EscapeSql((recipe.Description ?? "").ToLowerInvariant());
                var imageVal = recipe.Image != null ? $"'{EscapeSql(recipe.Image)}'" : "NULL";
                migrationBuilder.Sql($@"
                    INSERT INTO recipe.""Recipe"" (""Id"", ""Name"", ""Description"", ""PrepTimeMinutes"", ""CookTimeMinutes"", ""Servings"", ""CurationStatusId"", ""AuthorId"", ""Version"", ""Slug"", ""Image"", ""NameNormalized"", ""DescriptionNormalized"", ""IsOcrRecipe"", ""CreatedDate"", ""CreatedByPersonId"")
                    VALUES ({recipe.Id}, '{EscapeSql(recipe.Name)}', '{EscapeSql(recipe.Description)}', {recipe.PrepTimeMinutes}, {recipe.CookTimeMinutes}, {recipe.Servings}, {CurationStatusTypeCuratedId}, {SystemPersonId}, 1, '{EscapeSql(recipe.Slug)}', {imageVal}, '{nameNorm}', '{descNorm}', false, '{now}', {SystemPersonId});
                ");

                // 3. Insert recipe steps
                foreach (var step in recipe.Steps)
                {
                    migrationBuilder.Sql($@"
                        INSERT INTO recipe.""RecipeStep"" (""RecipeId"", ""StepNumber"", ""Summary"", ""Description"", ""CreatedDate"", ""CreatedByPersonId"")
                        VALUES ({recipe.Id}, {step.StepNumber}, '{EscapeSql(step.Summary)}', '{EscapeSql(step.Description)}', '{now}', {SystemPersonId});
                    ");
                }

                // 4. Insert recipe ingredients
                foreach (var ing in recipe.Ingredients)
                {
                    migrationBuilder.Sql($@"
                        INSERT INTO recipe.""RecipeIngredient"" (""RecipeId"", ""IngredientId"", ""Quantity"", ""MeasurementId"", ""RawLine"", ""CreatedDate"", ""CreatedByPersonId"")
                        VALUES ({recipe.Id}, {ing.IngredientId}, {ing.Quantity}, {ing.MeasurementId}, '{EscapeSql(ing.RawLine)}', '{now}', {SystemPersonId});
                    ");
                }

                // 5. Insert recipe category
                migrationBuilder.Sql($@"
                    INSERT INTO recipe.""RecipeCategory"" (""RecipeId"", ""CategoryId"", ""CreatedDate"", ""CreatedByPersonId"")
                    VALUES ({recipe.Id}, {recipe.CategoryId}, '{now}', {SystemPersonId});
                ");

                // 6. Insert recipe nutrition
                foreach (var nut in recipe.Nutrition)
                {
                    var dvpVal = nut.DailyValuePercentage.HasValue
                        ? nut.DailyValuePercentage.Value.ToString("F2")
                        : "NULL";
                    migrationBuilder.Sql($@"
                        INSERT INTO recipe.""RecipeNutrition"" (""RecipeId"", ""NutrientId"", ""Amount"", ""Unit"", ""DailyValuePercentage"", ""CreatedDate"", ""CreatedByPersonId"")
                        VALUES ({recipe.Id}, {nut.NutrientId}, {nut.Amount.ToString("F4")}, '{EscapeSql(nut.Unit)}', {dvpVal}, '{now}', {SystemPersonId});
                    ");
                }

                // 7. Insert recipe type assignment (if recipeTypeId present)
                if (recipe.RecipeTypeId.HasValue && recipe.RecipeTypeId.Value > 0)
                {
                    migrationBuilder.InsertData(
                        schema: "recipe",
                        table: "recipe_type_index",
                        columns: new[] { "RecipeId", "RecipeTypeId" },
                        values: new object[] { recipe.Id, recipe.RecipeTypeId.Value });
                }
            }

            // Update sequences
            migrationBuilder.Sql(@"SELECT setval(pg_get_serial_sequence('recipe.""Ingredient""', 'Id'), (SELECT COALESCE(MAX(""Id""), 1) FROM recipe.""Ingredient""));");
            migrationBuilder.Sql(@"SELECT setval(pg_get_serial_sequence('recipe.""Recipe""', 'Id'), (SELECT COALESCE(MAX(""Id""), 1) FROM recipe.""Recipe""));");
        }

        public static void RemoveExtendedRecipes(MigrationBuilder migrationBuilder)
        {
            // Remove recipe type assignments for extended recipes
            migrationBuilder.Sql(@"DELETE FROM recipe.""recipe_type_index"" WHERE ""RecipeId"" BETWEEN 200 AND 399;");
            // Remove in reverse dependency order
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeNutrition"" WHERE ""RecipeId"" BETWEEN 200 AND 399;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeCategory"" WHERE ""RecipeId"" BETWEEN 200 AND 399;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeIngredient"" WHERE ""RecipeId"" BETWEEN 200 AND 399;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""RecipeStep"" WHERE ""RecipeId"" BETWEEN 200 AND 399;");
            migrationBuilder.Sql(@"DELETE FROM recipe.""Recipe"" WHERE ""Id"" BETWEEN 200 AND 399;");
        }
    }
#pragma warning restore CS8625
}