using Microsoft.EntityFrameworkCore.Migrations;
using Nom.Data.Reference;
using Nom.Data.Question;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;
using System.Linq;

namespace Nom.Data
{
    public static class CustomMigration
    {
        public static void ApplyCustomUpOperations(this MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "question");
            migrationBuilder.EnsureSchema(name: "person");
            migrationBuilder.EnsureSchema(name: "audit");
            // No need to EnsureSchema("restriction") as RestrictionEntity is in 'plan' schema
            migrationBuilder.EnsureSchema(name: "plan"); // Ensure 'plan' schema exists for PlanParticipant

            SeedInitialSystemPerson(migrationBuilder);

            AddReferenceGroups(migrationBuilder);
            AddAnswerTypes(migrationBuilder);
            AddRestrictionTypes(migrationBuilder); // Seed restriction types
            AddPlanInvitationRoles(migrationBuilder); // NEW: Seed Plan Invitation Roles
            CreateReferenceGroupView(migrationBuilder);
            SeedInitialQuestions(migrationBuilder);
            SeedAdditionalRestrictionQuestions(migrationBuilder);
        }

        public static void ApplyCustomDownOperations(this MigrationBuilder migrationBuilder)
        {
            RemoveInitialQuestions(migrationBuilder);
            RemoveAdditionalRestrictionQuestions(migrationBuilder);
            DropReferenceGroupView(migrationBuilder);
            RemovePlanInvitationRoles(migrationBuilder); // NEW: Remove Plan Invitation Roles
            RemoveRestrictionTypes(migrationBuilder);
            RemoveAnswerTypes(migrationBuilder);
            RemoveReferenceGroups(migrationBuilder);

            RemoveInitialSystemPerson(migrationBuilder);

            migrationBuilder.DropSchema(name: "question");
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
                columns: new[] { "Id", "Name", "UserId", "InvitationCode" },
                values: new object[,]
                {
                    { 1L, "System", null, null } // Use null directly for nullable columns
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
                    { (long)ReferenceDiscriminatorEnum.AnswerType, "Answer Types (Meta-Group)", "A meta-group for all answer types." },
                    { (long)ReferenceDiscriminatorEnum.PlanInvitationRole, "Plan Invitation Roles", "Roles for invited participants in a plan (e.g., Admin, Member)." } // NEW GROUP
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
                    (long)ReferenceDiscriminatorEnum.AnswerType,
                    (long)ReferenceDiscriminatorEnum.PlanInvitationRole // REMOVE NEW GROUP
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

        public static void AddRestrictionTypes(MigrationBuilder migrationBuilder)
        {
            long restrictionGroupId = (long)ReferenceDiscriminatorEnum.RestrictionType;

            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description" },
                values: new object[,]
                {
                    { 2000L, "Gluten-Free", "Excludes all gluten-containing grains (wheat, barley, rye)." },
                    { 2001L, "Dairy-Free", "Excludes all dairy products (milk, cheese, yogurt)." },
                    { 2002L, "Lactose-Intolerant", "Excludes lactose, common in dairy." },
                    { 2003L, "Vegan", "Excludes all animal products (meat, dairy, eggs, honey)." },
                    { 2004L, "Vegetarian", "Excludes meat, poultry, and fish." },
                    { 2005L, "Pescatarian", "Excludes meat and poultry, but includes fish and seafood." },
                    { 2006L, "Keto", "Very low-carb, high-fat diet." },
                    { 2007L, "Paleo", "Focuses on whole, unprocessed foods, mimicking ancestral diets." },
                    { 2008L, "Mediterranean", "Emphasizes fruits, vegetables, whole grains, olive oil, lean proteins." },
                    { 2009L, "Dash Diet", "Dietary Approaches to Stop Hypertension." },
                    { 2010L, "Kosher", "Adheres to Jewish dietary laws." },
                    { 2011L, "Halal", "Adheres to Islamic dietary laws." },
                    { 2012L, "Nut Allergy", "Avoidance of nuts (peanuts, tree nuts)." },
                    { 2013L, "Egg Allergy", "Avoidance of eggs." },
                    { 2014L, "Soy Allergy", "Avoidance of soy products." },
                    { 2015L, "Fish Allergy", "Avoidance of fish." },
                    { 2016L, "Shellfish Allergy", "Avoidance of shellfish." },
                    { 2017L, "Sesame Allergy", "Avoidance of sesame." },
                    { 2018L, "Corn Allergy", "Avoidance of corn." },
                    { 2019L, "Sulfites Sensitivity", "Avoidance of sulfites." }
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
                columns: new[] { "Id", "Name", "Description" },
                values: new object[,]
                {
                    { 3000L, "Plan Admin", "A person who can manage plan settings, participants, and overall plan details." },
                    { 3001L, "Plan Member", "A person who participates in the plan and has individual settings." }
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
            #pragma warning disable CS8625 // Disable warnings for nullable reference type assignments
            migrationBuilder.InsertData(
                schema: "question",
                table: "Question",
                columns: new[] { "Id", "Text", "Hint", "QuestionCategoryId", "AnswerTypeRefId", "DisplayOrder", "IsActive", "IsRequiredForPlanCreation", "DefaultAnswer", "ValidationRegex", "NextQuestionOnTrue", "NextQuestionOnFalse" },
                values: new object[,]
                {
                    // Section 1: Getting Started
                    { 1L, "Will anyone else be participating in this plan with you (e.g., family members, roommates)?", null, questionCategoryId, yesNoAnswerTypeId, 10, true, false, "false", null, 2L, 4L },
                    { 2L, "How many people will be participating in this plan?", "Enter the number of participants.", questionCategoryId, textInputAnswerTypeId, 20, true, false, null, @"^\d+$", null, null },

                    // Section 2: Additional Persons (dynamically added in UI, this provides the question template)
                    { 3L, "Participant's Name:", "Enter the name of an additional person sharing this plan.", questionCategoryId, textInputAnswerTypeId, 30, true, false, null, null, null, null },

                    // NEW QUESTION: Invitation Code
                    { 4L, "Do you have an invitation code to join an existing plan?", null, questionCategoryId, textInputAnswerTypeId, 40, true, false, null, null, null, null },

                    // Section 3: Dietary Foundations & Values
                    { 5L, "Are there any societal, religious, or ethical dietary practices you or other participants follow?", null, questionCategoryId, yesNoAnswerTypeId, 50, true, false, "false", null, 6L, null },
                    { 6L, "Which of the following dietary foundations apply to anyone participating?", "Select all that apply.", questionCategoryId, multiSelectAnswerTypeId, 60, true, false, "[\"Kosher\",\"Halal\",\"Vegetarian\",\"Vegan\",\"Pescatarian\",\"Pollotarian\",\"Flexitarian\",\"Paleo\",\"Keto\",\"Mediterranean\",\"Dash Diet\"]", null, null, null },
                    { 7L, "Please describe any specific cultural or traditional food restrictions, inclusions, or fasting periods:", "e.g., no pork, no beef, specific holiday foods, Ramadan, Lent", questionCategoryId, textInputAnswerTypeId, 70, true, false, null, null, null, null },

                    // Section 4: Health & Medical Dietary Adjustments
                    { 8L, "Are there any allergies, intolerances, or medical conditions that require specific dietary adjustments for anyone on the plan?", null, questionCategoryId, yesNoAnswerTypeId, 80, true, false, "false", null, 9L, null },
                    { 9L, "Please indicate any diagnosed food allergies for participants:", "Select all that apply, or type 'Other' for unlisted.", questionCategoryId, multiSelectAnswerTypeId, 90, true, false, "[\"Peanuts\",\"Tree Nuts\",\"Dairy\",\"Eggs\",\"Soy\",\"Wheat\",\"Fish\",\"Shellfish\",\"Sesame\",\"Corn\",\"Sulfites\"]", null, null, null },
                    { 10L, "Is anyone managing Gluten Sensitivity or Celiac Disease?", null, questionCategoryId, yesNoAnswerTypeId, 100, true, false, "false", null, null, null },
                    { 11L, "Is anyone managing Lactose Intolerance?", null, questionCategoryId, yesNoAnswerTypeId, 110, true, false, "false", null, null, null },
                    { 12L, "Is anyone managing Type 1 Diabetes?", null, questionCategoryId, yesNoAnswerTypeId, 120, true, false, "false", null, null, null },
                    { 13L, "Is anyone managing Type 2 Diabetes?", null, questionCategoryId, yesNoAnswerTypeId, 130, true, false, "false", null, null, null },
                    { 14L, "Is anyone managing High Blood Pressure?", null, questionCategoryId, yesNoAnswerTypeId, 140, true, false, "false", null, null, null },
                    { 15L, "Is anyone managing High Cholesterol?", null, questionCategoryId, yesNoAnswerTypeId, 150, true, false, "false", null, null, null },
                    { 16L, "Is anyone managing Gastrointestinal Conditions (e.g., Crohn's, IBS, Leaky Gut, GERD)?", null, questionCategoryId, yesNoAnswerTypeId, 160, true, false, "false", null, null, null },
                    { 17L, "Please specify gastrointestinal conditions or specific triggers/avoidances:", null, questionCategoryId, textInputAnswerTypeId, 170, true, false, null, null, null, null },
                    { 18L, "Is anyone managing Kidney Disease?", null, questionCategoryId, yesNoAnswerTypeId, 180, true, false, "false", null, null, null },
                    { 19L, "Please specify kidney disease stage or specific restrictions (e.g., low potassium, low phosphorus):", null, questionCategoryId, textInputAnswerTypeId, 190, true, false, null, null, null, null },
                    { 20L, "Is anyone managing Gout?", null, questionCategoryId, yesNoAnswerTypeId, 200, true, false, "false", null, null, null },
                    { 21L, "Are there any other medical conditions or health goals impacting diet (e.g., anemia, specific vitamin deficiencies, pregnancy/lactation needs, specific medication interactions)? Please describe:", null, questionCategoryId, textInputAnswerTypeId, 210, true, false, null, null, null, null },

                    // Section 5: Personal Food Preferences & Aversions
                    { 22L, "Are there any specific foods, ingredients, or textures that you or other participants strongly dislike or prefer to avoid?", null, questionCategoryId, yesNoAnswerTypeId, 220, true, false, "false", null, null, null },
                    { 23L, "Which ingredients or foods do you want to exclude?", "e.g., Cilantro, Mushrooms, Olives, Bell Peppers", questionCategoryId, multiSelectAnswerTypeId, 230, true, false, "[\"Cilantro\",\"Mushrooms\",\"Olives\",\"Bell Peppers\",\"Onions\",\"Garlic\",\"Spicy Foods (general)\",\"Fishy taste\",\"Gamey meats\"]", null, null, null },
                    { 24L, "Are there any textures you strongly dislike?", "e.g., mushy, slimy, gritty, soggy, crunchy (if aversion)", questionCategoryId, multiSelectAnswerTypeId, 240, true, false, "[\"Mushy\",\"Slimy\",\"Gritty\",\"Chewy (e.g., undercooked beans)\",\"Soggy\",\"Crunchy\"]", null, null, null },
                    { 25L, "What spice level do you generally prefer?", null, questionCategoryId, singleSelectAnswerTypeId, 250, true, false, "Mild", "[\"Mild\",\"Medium\",\"Spicy\",\"Very Spicy\"]", null, null },
                    { 26L, "Are there any preferred cooking methods?", "Select all that apply.", questionCategoryId, multiSelectAnswerTypeId, 260, true, false, "[\"Grilled\",\"Baked\",\"Roasted\",\"Stir-fried\",\"Slow-cooked\",\"Pressure cooked\",\"Raw\"]", null, null, null },
                    { 27L, "Do you have any other general food likes or dislikes (e.g., preference for specific cuisines, dislike of strong odors)?", null, questionCategoryId, textInputAnswerTypeId, 270, true, false, null, null, null, null }
                });
            #pragma warning restore CS8625 // Re-enable warnings
        }

        public static void RemoveInitialQuestions(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "question",
                table: "Question",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L, 11L, 12L, 13L, 14L, 15L, 16L, 17L, 18L, 19L, 20L, 21L, 22L, 23L, 24L, 25L, 26L, 27L
                });
        }

        public static void SeedAdditionalRestrictionQuestions(MigrationBuilder migrationBuilder)
        {
            long questionCategoryId = (long)ReferenceDiscriminatorEnum.QuestionCategory;
            long yesNoAnswerTypeId = 1000L;
            long textInputAnswerTypeId = 1001L;
            long multiSelectAnswerTypeId = 1002L;

            migrationBuilder.InsertData(
                schema: "question",
                table: "Question",
                columns: new[] { "Id", "Text", "Hint", "QuestionCategoryId", "AnswerTypeRefId", "DisplayOrder", "IsActive", "IsRequiredForPlanCreation", "DefaultAnswer", "ValidationRegex", "NextQuestionOnTrue", "NextQuestionOnFalse" },
                values: new object[,]
                {
                    { 28L, "Should this restriction apply to the entire plan?", null, questionCategoryId, yesNoAnswerTypeId, 280, true, false, "false", null, 29L, null },
                    { 29L, "Which participant(s) should this restriction apply to?", "Select all participants or leave blank for none.", questionCategoryId, multiSelectAnswerTypeId, 290, true, false, null, null, null, null },
                    { 30L, "Please provide additional details or notes about this restriction:", "Optional description or clarification.", questionCategoryId, textInputAnswerTypeId, 300, true, false, null, null, null, null }
                });
        }

        public static void RemoveAdditionalRestrictionQuestions(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "question",
                table: "Question",
                keyColumn: "Id",
                keyValues: new object[] { 28L, 29L, 30L });
        }
    }
}
