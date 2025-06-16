// Nom.Data/Migrations/Custom/CustomMigration.cs
using Microsoft.EntityFrameworkCore.Migrations;
using Nom.Data.Reference; // Required for ReferenceDiscriminatorEnum

namespace Nom.Data
{
    /// <summary>
    /// Provides static methods to encapsulate custom migration logic
    /// such as data seeding and view creation/dropping.
    /// These methods are implemented as extension methods for MigrationBuilder.
    /// </summary>
    public static class CustomMigration
    {
        // --- High-level Extension Methods for MigrationBuilder ---

        /// <summary>
        /// Applies all custom 'Up' operations (e.g., seeding data, creating views).
        /// This should be called at a suitable point in a migration's Up() method
        /// AFTER all necessary tables have been created.
        /// </summary>
        public static void ApplyCustomUpOperations(this MigrationBuilder migrationBuilder)
        {
            AddReferenceGroups(migrationBuilder);        // Call internal static method
            CreateReferenceGroupView(migrationBuilder);  // Call internal static method
        }

        /// <summary>
        /// Applies all custom 'Down' operations (e.g., dropping views, deleting seeded data).
        /// This should be called at a suitable point in a migration's Down() method
        /// BEFORE any tables that depend on these objects are dropped.
        /// </summary>
        public static void ApplyCustomDownOperations(this MigrationBuilder migrationBuilder)
        {
            DropReferenceGroupView(migrationBuilder);    // Call internal static method
            RemoveReferenceGroups(migrationBuilder);     // Call internal static method
        }

        // --- Lower-level, specific custom migration operations (remain static for internal use) ---

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
                    { (long)ReferenceDiscriminatorEnum.CuisineType, "Cuisine Types", "Types of culinary styles (e.g., Italian, Mexican, Asian)." }
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
                    (long)ReferenceDiscriminatorEnum.CuisineType
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
    }
}