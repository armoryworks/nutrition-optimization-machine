using Microsoft.EntityFrameworkCore.Migrations;
using Nom.Data.Reference;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;
using System.Linq;

namespace Nom.Data
{
    public static class CustomMigration
    {
        public static void ApplyCustomUpOperations(this MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "person");
            migrationBuilder.EnsureSchema(name: "audit");
            // No need to EnsureSchema("restriction") as RestrictionEntity is in 'plan' schema
            migrationBuilder.EnsureSchema(name: "plan"); // Ensure 'plan' schema exists for PlanParticipant

            SeedInitialSystemPerson(migrationBuilder);

            AddReferenceGroups(migrationBuilder);
            AddRestrictionTypes(migrationBuilder); // Seed restriction types
            AddPlanInvitationRoles(migrationBuilder); // NEW: Seed Plan Invitation Roles
            CreateReferenceGroupView(migrationBuilder);
        }

        public static void ApplyCustomDownOperations(this MigrationBuilder migrationBuilder)
        {
            DropReferenceGroupView(migrationBuilder);
            RemovePlanInvitationRoles(migrationBuilder); // NEW: Remove Plan Invitation Roles
            RemoveRestrictionTypes(migrationBuilder);
            RemoveReferenceGroups(migrationBuilder);

            RemoveInitialSystemPerson(migrationBuilder);
            migrationBuilder.DropSchema(name: "person");
            migrationBuilder.DropSchema(name: "audit");
            // No need to DropSchema("restriction")
            migrationBuilder.DropSchema(name: "plan"); // Drop 'plan' schema
        }

        public static void SeedInitialSystemPerson(MigrationBuilder migrationBuilder)
        {
#pragma warning disable CS8625 // Disable warnings for nullable reference type assignments
            migrationBuilder.InsertData(
                schema: "person",
                table: "Person",
                columns: new[] { "Id", "Name", "UserId", "InvitationCode", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 1L, "System", null, null, DateTime.UtcNow, 1L } // Use null directly for nullable columns
                });
#pragma warning restore CS8625 // Re-enable warnings
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
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { (long)ReferenceDiscriminatorEnum.MealType, "Meal Types", "Categories for meals like breakfast, lunch, dinner.", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.MeasurementType, "Measurement Types", "Units of measurement for ingredients and quantities.", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.RecipeType, "Recipe Types", "Categorization of recipes (e.g., appetizer, main course, dessert).", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.ShoppingStatusType, "Shopping Status Types", "Statuses for shopping trips (e.g., planned, completed, canceled).", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.ItemStatusType, "Item Status Types", "Statuses for pantry items (e.g., on list, in pantry, used, expired).", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.RestrictionType, "Restriction Types", "Dietary restrictions (e.g., gluten-free, vegan).", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.GoalType, "Goal Types", "Nutritional goals (e.g., weight loss, muscle gain).", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.NutrientType, "Nutrient Types", "Categories of nutrients (e.g., macronutrients, vitamins, minerals).", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.CuisineType, "Cuisine Types", "Types of culinary styles (e.g., Italian, Mexican, Asian).", DateTime.UtcNow, 1L },
                    { (long)ReferenceDiscriminatorEnum.PlanInvitationRole, "Plan Invitation Roles", "Roles for invited participants in a plan (e.g., Admin, Member)", DateTime.UtcNow, 1L }
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
                    (long)ReferenceDiscriminatorEnum.PlanInvitationRole // REMOVE NEW GROUP
                });
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
                    { 2000L, "Gluten-Free", "Excludes all gluten-containing grains (wheat, barley, rye).", DateTime.UtcNow, 1L },
                    { 2001L, "Dairy-Free", "Excludes all dairy products (milk, cheese, yogurt).", DateTime.UtcNow, 1L },
                    { 2002L, "Lactose-Intolerant", "Excludes lactose, common in dairy.", DateTime.UtcNow, 1L },
                    { 2003L, "Vegan", "Excludes all animal products (meat, dairy, eggs, honey).", DateTime.UtcNow, 1L },
                    { 2004L, "Vegetarian", "Excludes meat, poultry, and fish.", DateTime.UtcNow, 1L },
                    { 2005L, "Pescatarian", "Excludes meat and poultry, but includes fish and seafood.", DateTime.UtcNow, 1L },
                    { 2006L, "Keto", "Very low-carb, high-fat diet.", DateTime.UtcNow, 1L },
                    { 2007L, "Paleo", "Focuses on whole, unprocessed foods, mimicking ancestral diets.", DateTime.UtcNow, 1L },
                    { 2008L, "Mediterranean", "Emphasizes fruits, vegetables, whole grains, olive oil, lean proteins.", DateTime.UtcNow, 1L },
                    { 2009L, "Dash Diet", "Dietary Approaches to Stop Hypertension.", DateTime.UtcNow, 1L },
                    { 2010L, "Kosher", "Adheres to Jewish dietary laws.", DateTime.UtcNow, 1L },
                    { 2011L, "Halal", "Adheres to Islamic dietary laws.", DateTime.UtcNow, 1L },
                    { 2012L, "Nut Allergy", "Avoidance of nuts (peanuts, tree nuts).", DateTime.UtcNow, 1L },
                    { 2013L, "Egg Allergy", "Avoidance of eggs.", DateTime.UtcNow, 1L },
                    { 2014L, "Soy Allergy", "Avoidance of soy products.", DateTime.UtcNow, 1L },
                    { 2015L, "Fish Allergy", "Avoidance of fish.", DateTime.UtcNow, 1L },
                    { 2016L, "Shellfish Allergy", "Avoidance of shellfish.", DateTime.UtcNow, 1L },
                    { 2017L, "Sesame Allergy", "Avoidance of sesame.", DateTime.UtcNow, 1L },
                    { 2018L, "Corn Allergy", "Avoidance of corn.", DateTime.UtcNow, 1L },
                    { 2019L, "Sulfites Sensitivity", "Avoidance of sulfites.", DateTime.UtcNow, 1L }
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
                keyValues: new object[]
                {
                    2000L, 2001L, 2002L, 2003L, 2004L, 2005L, 2006L, 2007L, 2008L, 2009L, 2010L, 2011L, 2012L, 2013L, 2014L, 2015L, 2016L, 2017L, 2018L, 2019L
                });
        }

        // NEW: Seed Plan Invitation Roles
        public static void AddPlanInvitationRoles(MigrationBuilder migrationBuilder)
        {
            long planInvitationRoleGroupId = (long)ReferenceDiscriminatorEnum.PlanInvitationRole;

            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { 3000L, "Plan Admin", "A person who can manage plan settings, participants, and overall plan details.", DateTime.UtcNow, 1L },
                    { 3001L, "Plan Member", "A person who participates in the plan and has individual settings.", DateTime.UtcNow, 1L }
                });

            foreach (long id in new long[] { 3000L, 3001L })
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, planInvitationRoleGroupId });
            }
        }

        // NEW: Remove Plan Invitation Roles
        public static void RemovePlanInvitationRoles(MigrationBuilder migrationBuilder)
        {
            long planInvitationRoleGroupId = (long)ReferenceDiscriminatorEnum.PlanInvitationRole;

            foreach (long id in new long[] { 3000L, 3001L })
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
                keyValues: new object[]
                {
                    3000L, 3001L
                });
        }

        public static void CreateReferenceGroupView(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW reference.""ReferenceGroupView"" AS
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
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS reference.""ReferenceGroupView"";");
        }


    }
}
