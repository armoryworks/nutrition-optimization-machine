// Nom.Data/CustomMigration.cs
using Microsoft.EntityFrameworkCore.Migrations;
using Nom.Data.Reference;
using Nom.Data.Question;
using Microsoft.EntityFrameworkCore.Metadata; // Required for NpgsqlValueGenerationStrategy
using System;
using System.Data;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata; // For NpgsqlValueGenerationStrategy

namespace Nom.Data
{
    /// <summary>
    /// Provides static methods to encapsulate custom migration logic
    /// such as data seeding and view creation/dropping.
    /// These methods are implemented as extension methods for MigrationBuilder.
    /// </summary>
    public static class CustomMigration
    {
        public static void ApplyCustomUpOperations(this MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "question");
            migrationBuilder.EnsureSchema(name: "person");
            migrationBuilder.EnsureSchema(name: "audit"); // Ensure audit schema

            // --- REMOVED: Create AuditLogEntry table and its index from here ---
            // EF Core will automatically generate this based on DbSet<AuditLogEntryEntity> in ApplicationDbContext.

            SeedInitialSystemPerson(migrationBuilder);

            AddReferenceGroups(migrationBuilder);
            AddAnswerTypes(migrationBuilder);
            CreateReferenceGroupView(migrationBuilder);
            SeedInitialQuestions(migrationBuilder);
        }

        public static void ApplyCustomDownOperations(this MigrationBuilder migrationBuilder)
        {
            RemoveInitialQuestions(migrationBuilder);
            DropReferenceGroupView(migrationBuilder);
            RemoveAnswerTypes(migrationBuilder);
            RemoveReferenceGroups(migrationBuilder);

            // --- REMOVED: Drop AuditLogEntry table from here ---
            // EF Core will automatically generate this based on DbSet<AuditLogEntryEntity> in ApplicationDbContext.

            RemoveInitialSystemPerson(migrationBuilder);

            migrationBuilder.DropSchema(name: "question");
            migrationBuilder.DropSchema(name: "person");
            migrationBuilder.DropSchema(name: "audit"); // Drop audit schema
        }

        public static void SeedInitialSystemPerson(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "person",
                table: "Person",
                columns: new[] { "Id", "Name", "UserId", "InvitationCode" },
                values: new object[,]
                {
                    { 1L, "System", null, null }
                });
        }

        public static void RemoveInitialSystemPerson(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "person",
                table: "Person",
                keyColumn: "Id",
                keyValues: new object[] { 1L });
        }

        public static void AddReferenceGroups(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Group",
                columns: new[] { "Id", "Name", "Description" },
                values: new object[,]
                {
                    { (long)ReferenceDiscriminatorEnum.MealType, "Meal Types", "Categories for meals like breakfast, lunch, dinner." },
                    { (long)ReferenceDiscriminatorEnum.MeasurementType, "Measurement Types", "Units of measurement for ingredients and quantities." },
                    { (long)ReferenceDiscriminatorEnum.RecipeType, "Recipe Types", "Categorization of recipes (e.g., appetizer, main course, dessert)." },
                    { (long)ReferenceDiscriminatorEnum.ShoppingStatusType, "Shopping Status Types", "Statuses for shopping trips (e.g., planned, completed, canceled)." },
                    { (long)ReferenceDiscriminatorEnum.ItemStatusType, "Item Status Types", "Statuses for pantry items (e.g., on list, in pantry, used, expired)." },
                    { (long)ReferenceDiscriminatorEnum.RestrictionType, "Restriction Types", "Dietary restrictions (e.g., gluten-free, vegan)." },
                    { (long)ReferenceDiscriminatorEnum.GoalType, "Goal Types", "Nutritional goals (e.g., weight loss, muscle gain)." },
                    { (long)ReferenceDiscriminatorEnum.NutrientType, "Nutrient Types", "Categories of nutrients (e.g., macronutrients, vitamins, minerals)." },
                    { (long)ReferenceDiscriminatorEnum.CuisineType, "Cuisine Types", "Types of culinary styles (e.g., Italian, Mexican, Asian)." },
                    { (long)ReferenceDiscriminatorEnum.QuestionCategory, "Question Categories (Meta-Group)", "A meta-group for all question categories." },
                    { (long)ReferenceDiscriminatorEnum.AnswerType, "Answer Types (Meta-Group)", "A meta-group for all answer types." }
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
                    (long)ReferenceDiscriminatorEnum.MeasurementType,
                    (long)ReferenceDiscriminatorEnum.RecipeType,
                    (long)ReferenceDiscriminatorEnum.ShoppingStatusType,
                    (long)ReferenceDiscriminatorEnum.ItemStatusType,
                    (long)ReferenceDiscriminatorEnum.RestrictionType,
                    (long)ReferenceDiscriminatorEnum.GoalType,
                    (long)ReferenceDiscriminatorEnum.NutrientType,
                    (long)ReferenceDiscriminatorEnum.CuisineType,
                    (long)ReferenceDiscriminatorEnum.QuestionCategory,
                    (long)ReferenceDiscriminatorEnum.AnswerType
                });
        }

        public static void AddAnswerTypes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description" },
                values: new object[,]
                {
                    { 1000L, "Yes/No", "A binary true/false answer." },
                    { 1001L, "Text Input", "A free-form text answer." },
                    { 1002L, "Multi-Select", "Multiple choices can be selected (answer stored as JSON array)." },
                    { 1003L, "Single-Select", "Only one choice can be selected." }
                });
        }

        public static void RemoveAnswerTypes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    1000L, 1001L, 1002L, 1003L
                });
        }

        public static void CreateReferenceGroupView(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW reference.ReferenceGroupView AS
                SELECT
                    ref.""Id"" AS ReferenceId,
                    ref.""Name"" AS ReferenceName,
                    ref.""Description"" AS ReferenceDescription,
                    grp.""Id"" AS GroupId,
                    grp.""Name"" AS GroupName,
                    grp.""Description"" AS GroupDescription
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
            migrationBuilder.Sql("DROP VIEW IF EXISTS reference.ReferenceGroupView;");
        }

        public static void SeedInitialQuestions(MigrationBuilder migrationBuilder)
        {
            long questionCategoryId = (long)ReferenceDiscriminatorEnum.QuestionCategory;
            long yesNoAnswerTypeId = 1000L;
            long textInputAnswerTypeId = 1001L;
            long multiSelectAnswerTypeId = 1002L;
            long singleSelectAnswerTypeId = 1003L;

            migrationBuilder.InsertData(
                schema: "question",
                table: "Question",
                columns: new[] { "Id", "Text", "Hint", "QuestionCategoryId", "AnswerTypeRefId", "DisplayOrder", "IsActive", "IsRequiredForPlanCreation", "DefaultAnswer", "ValidationRegex" },
                values: new object[,]
                {
                    // Section 1: Getting Started
                    { 1L, "What name should we use for you within the plan?", null, questionCategoryId, textInputAnswerTypeId, 10, true, true, null, null },
                    { 2L, "Will anyone else be participating in this plan with you (e.g., family members, roommates)?", null, questionCategoryId, yesNoAnswerTypeId, 20, true, false, "false", null },

                    // Section 2: Additional Persons (dynamically added in UI, this provides the question template)
                    { 3L, "Participant's Name:", "Enter the name of an additional person sharing this plan.", questionCategoryId, textInputAnswerTypeId, 30, true, false, null, null },

                    // Section 3: Dietary Foundations & Values
                    { 4L, "Are there any societal, religious, or ethical dietary practices you or other participants follow?", null, questionCategoryId, yesNoAnswerTypeId, 40, true, false, "false", null },
                    { 5L, "Which of the following dietary foundations apply to anyone participating?", "Select all that apply.", questionCategoryId, multiSelectAnswerTypeId, 50, true, false, "[\"Kosher\",\"Halal\",\"Vegetarian\",\"Vegan\",\"Pescatarian\",\"Pollotarian\",\"Flexitarian\",\"Paleo\",\"Keto\",\"Mediterranean\",\"Dash Diet\"]", null },
                    { 6L, "Please describe any specific cultural or traditional food restrictions, inclusions, or fasting periods:", "e.g., no pork, no beef, specific holiday foods, Ramadan, Lent", questionCategoryId, textInputAnswerTypeId, 60, true, false, null, null },

                    // Section 4: Health & Medical Dietary Adjustments
                    { 7L, "Are there any allergies, intolerances, or medical conditions that require specific dietary adjustments for anyone on the plan?", null, questionCategoryId, yesNoAnswerTypeId, 70, true, false, "false", null },
                    { 8L, "Please indicate any diagnosed food allergies for participants:", "Select all that apply, or type 'Other' for unlisted.", questionCategoryId, multiSelectAnswerTypeId, 80, true, false, "[\"Peanuts\",\"Tree Nuts\",\"Dairy\",\"Eggs\",\"Soy\",\"Wheat\",\"Fish\",\"Shellfish\",\"Sesame\",\"Corn\",\"Sulfites\"]", null },
                    { 9L, "Is anyone managing Gluten Sensitivity or Celiac Disease?", null, questionCategoryId, yesNoAnswerTypeId, 90, true, false, "false", null },
                    { 10L, "Is anyone managing Lactose Intolerance?", null, questionCategoryId, yesNoAnswerTypeId, 100, true, false, "false", null },
                    { 11L, "Is anyone managing Type 1 Diabetes?", null, questionCategoryId, yesNoAnswerTypeId, 110, true, false, "false", null },
                    { 12L, "Is anyone managing Type 2 Diabetes?", null, questionCategoryId, yesNoAnswerTypeId, 120, true, false, "false", null },
                    { 13L, "Is anyone managing High Blood Pressure?", null, questionCategoryId, yesNoAnswerTypeId, 130, true, false, "false", null },
                    { 14L, "Is anyone managing High Cholesterol?", null, questionCategoryId, yesNoAnswerTypeId, 140, true, false, "false", null },
                    { 15L, "Is anyone managing Gastrointestinal Conditions (e.g., Crohn's, IBS, Leaky Gut, GERD)?", null, questionCategoryId, yesNoAnswerTypeId, 150, true, false, "false", null },
                    { 16L, "Please specify gastrointestinal conditions or specific triggers/avoidances:", null, questionCategoryId, textInputAnswerTypeId, 160, true, false, null, null },
                    { 17L, "Is anyone managing Kidney Disease?", null, questionCategoryId, yesNoAnswerTypeId, 170, true, false, "false", null },
                    { 18L, "Please specify kidney disease stage or specific restrictions (e.g., low potassium, low phosphorus):", null, questionCategoryId, textInputAnswerTypeId, 180, true, false, null, null },
                    { 19L, "Is anyone managing Gout?", null, questionCategoryId, yesNoAnswerTypeId, 190, true, false, "false", null },
                    { 20L, "Are there any other medical conditions or health goals impacting diet (e.g., anemia, specific vitamin deficiencies, pregnancy/lactation needs, specific medication interactions)? Please describe:", null, questionCategoryId, textInputAnswerTypeId, 200, true, false, null, null },

                    // Section 5: Personal Food Preferences & Aversions
                    { 21L, "Are there any specific foods, ingredients, or textures that you or other participants strongly dislike or prefer to avoid?", null, questionCategoryId, yesNoAnswerTypeId, 210, true, false, "false", null },
                    { 22L, "Which ingredients or foods do you want to exclude?", "e.g., Cilantro, Mushrooms, Olives, Bell Peppers", questionCategoryId, multiSelectAnswerTypeId, 220, true, false, "[\"Cilantro\",\"Mushrooms\",\"Olives\",\"Bell Peppers\",\"Onions\",\"Garlic\",\"Spicy Foods (general)\",\"Fishy taste\",\"Gamey meats\"]", null },
                    { 23L, "Are there any textures you strongly dislike?", "e.g., mushy, slimy, gritty, soggy, crunchy (if aversion)", questionCategoryId, multiSelectAnswerTypeId, 230, true, false, "[\"Mushy\",\"Slimy\",\"Gritty\",\"Chewy (e.g., undercooked beans)\",\"Soggy\",\"Crunchy\"]", null },
                    { 24L, "What spice level do you generally prefer?", null, questionCategoryId, singleSelectAnswerTypeId, 240, true, false, "Mild", "[\"Mild\",\"Medium\",\"Spicy\",\"Very Spicy\"]" },
                    { 25L, "Are there any preferred cooking methods?", "Select all that apply.", questionCategoryId, multiSelectAnswerTypeId, 250, true, false, "[\"Grilled\",\"Baked\",\"Roasted\",\"Stir-fried\",\"Slow-cooked\",\"Pressure cooked\",\"Raw\"]", null },
                    { 26L, "Do you have any other general food likes or dislikes (e.g., preference for specific cuisines, dislike of strong odors)?", null, questionCategoryId, textInputAnswerTypeId, 260, true, false, null, null }
                });
        }

        public static void RemoveInitialQuestions(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "question",
                table: "Question",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L, 11L, 12L, 13L, 14L, 15L, 16L, 17L, 18L, 19L, 20L, 21L, 22L, 23L, 24L, 25L, 26L
                });
        }
    }
}