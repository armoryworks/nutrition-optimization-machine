using Microsoft.EntityFrameworkCore.Migrations;
using Nom.Data.Reference; // For ReferenceDiscriminatorEnum
using System;
using System.Linq;
using System.Collections.Generic; // Required for List

namespace Nom.Data
{
    public static class CustomMigration
    {
        // --- System Person ID ---
        private const long SystemPersonId = 1L;

        // --- Reference Data IDs (Measurement Types) ---
        private const long MeasurementTypeUnknownId = 4000L;
        private const long MeasurementTypeToTasteId = 4001L;
        private const long MeasurementTypeEachId = 4002L;
        private const long MeasurementTypeGramId = 4003L; // g
        private const long MeasurementTypeMilligramId = 4004L; // mg
        private const long MeasurementTypeMicrogramId = 4005L; // µg
        private const long MeasurementTypeKcalId = 4006L; // kcal
        // Additional common cooking units
        private const long MeasurementTypeKilogramId = 4007L; // kg
        private const long MeasurementTypeLiterId = 4008L; // l
        private const long MeasurementTypeMilliliterId = 4009L; // ml
        private const long MeasurementTypeTeaspoonId = 4010L; // tsp
        private const long MeasurementTypeTablespoonId = 4011L; // tbsp
        private const long MeasurementTypeCupId = 4012L; // cup
        private const long MeasurementTypeOunceId = 4013L; // oz
        private const long MeasurementTypePoundId = 4014L; // lb
        private const long MeasurementTypePintId = 4015L; // pint
        private const long MeasurementTypeQuartId = 4016L; // quart
        private const long MeasurementTypeGallonId = 4017L; // gallon
        // Specific from PDFs if needed - often just "mcg"
        private const long MeasurementTypeMcgId = 4018L; // mcg (as seen in PDFs for Vitamin A, D, etc.)

        // --- Nutrient IDs (Derived from DRVs and RDIs) ---
        // DRV Nutrients
        private const long NutrientFatId = 5000L;
        private const long NutrientSaturatedFatId = 5001L;
        private const long NutrientCholesterolId = 5002L;
        private const long NutrientTotalCarbohydratesId = 5003L;
        private const long NutrientSodiumId = 5004L;
        private const long NutrientDietaryFiberId = 5005L;
        private const long NutrientProteinId = 5006L;
        private const long NutrientAddedSugarsId = 5007L;
        // RDI Nutrients (Vitamins)
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
        // RDI Nutrients (Minerals)
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
        // Special case for Calories (Energy)
        private const long NutrientCaloriesFDCId = 5035L; // Different ID if "Energy" from FDC comes in. Using 5000L for general.

        // --- Reference Data IDs (Goal Types - for Nutrient Guidelines, matching PDF demographics) ---
        private const long GoalTypeAdultsAndChildren4PlusId = 6000L; // Adults and Children >= 4 years
        private const long GoalTypeInfantsThrough12MonthsId = 6001L; // Infants through 12 months
        private const long GoalTypeChildren1Through3YearsId = 6002L; // Children 1 through 3 years
        private const long GoalTypePregnantAndLactatingWomenId = 6003L; // Pregnant women and lactating women
        private const long GoalTypeGeneralAdultId = 6004L; // Broader "General Adult" if needed for non-label specific goals

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

            SeedInitialSystemPerson(migrationBuilder);

            AddReferenceGroups(migrationBuilder);

            AddRestrictionTypes(migrationBuilder);
            AddPlanInvitationRoles(migrationBuilder);
            AddMeasurementTypes(migrationBuilder);
            AddGoalTypes(migrationBuilder);

            // Add Nutrients including parent-child relationships
            AddNutrientTypes(migrationBuilder);

            // Add Nutrient Guidelines
            AddNutrientGuidelines(migrationBuilder);

            CreateReferenceGroupView(migrationBuilder);
        }

        public static void ApplyCustomDownOperations(this MigrationBuilder migrationBuilder)
        {
            DropReferenceGroupView(migrationBuilder);

            // Remove guidelines first (they depend on nutrients)
            RemoveNutrientGuidelines(migrationBuilder);

            // Remove nutrient types (which now includes setting ParentNutrientId to null)
            RemoveNutrientTypes(migrationBuilder);

            RemoveGoalTypes(migrationBuilder);
            RemoveMeasurementTypes(migrationBuilder);
            RemovePlanInvitationRoles(migrationBuilder);
            RemoveRestrictionTypes(migrationBuilder);
            RemoveReferenceGroups(migrationBuilder);

            RemoveInitialSystemPerson(migrationBuilder);

            migrationBuilder.DropSchema(name: "person");
            migrationBuilder.DropSchema(name: "audit");
            migrationBuilder.DropSchema(name: "plan");
            migrationBuilder.DropSchema(name: "recipe");
            migrationBuilder.DropSchema(name: "nutrient");
            migrationBuilder.DropSchema(name: "shopping");
            migrationBuilder.DropSchema(name: "reference");
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
                    { SystemPersonId, "System", null, null, DateTime.UtcNow, SystemPersonId }
                });
#pragma warning restore CS8625
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
                    { (long)ReferenceDiscriminatorEnum.MeasurementType, "Measurement Types", "Units of measurement for ingredients and quantities.", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RecipeType, "Recipe Types", "Categorization of recipes (e.g., appetizer, main course, dessert).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.ShoppingStatusType, "Shopping Status Types", "Statuses for shopping trips (e.g., planned, completed, canceled).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.ItemStatusType, "Item Status Types", "Statuses for pantry items (e.g., on list, in pantry, used, expired).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.RestrictionType, "Restriction Types", "Dietary restrictions (e.g., gluten-free, vegan).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.GoalType, "Goal Types", "Nutritional goals or demographic categories for guidelines (e.g., 'Adults 4+ years').", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.NutrientType, "Nutrient Types", "Categories of nutrients (e.g., macronutrients, vitamins, minerals).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.CuisineType, "Cuisine Types", "Types of culinary styles (e.g., Italian, Mexican, Asian).", DateTime.UtcNow, SystemPersonId },
                    { (long)ReferenceDiscriminatorEnum.PlanInvitationRole, "Plan Invitation Roles", "Roles for invited participants in a plan (e.g., Admin, Member)", DateTime.UtcNow, SystemPersonId }
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
                    (long)ReferenceDiscriminatorEnum.PlanInvitationRole
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
                keyValues: new object[]
                {
                    2000L, 2001L, 2002L, 2003L, 2004L, 2005L, 2006L, 2007L, 2008L, 2009L, 2010L, 2011L, 2012L, 2013L, 2014L, 2015L, 2016L, 2017L, 2018L, 2019L
                });
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
                    { 3000L, "Plan Admin", "A person who can manage plan settings, participants, and overall plan details.", DateTime.UtcNow, SystemPersonId },
                    { 3001L, "Plan Member", "A person who participates in the plan and has individual settings.", DateTime.UtcNow, SystemPersonId }
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

        // Add Measurement Types from PDFs and common sense
        public static void AddMeasurementTypes(MigrationBuilder migrationBuilder)
        {
            long measurementGroupId = (long)ReferenceDiscriminatorEnum.MeasurementType;

            migrationBuilder.InsertData(
                schema: "reference",
                table: "Reference",
                columns: new[] { "Id", "Name", "Description", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    { MeasurementTypeUnknownId, "unknown", "Used when the measurement unit cannot be determined.", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeToTasteId, "to taste", "Indicates an ingredient quantity determined by preference.", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeEachId, "each", "Used when quantity refers to individual items.", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeGramId, "g", "Gram", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeMilligramId, "mg", "Milligram", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeMicrogramId, "µg", "Microgram (scientific symbol)", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeMcgId, "mcg", "Microgram (common abbreviation, as seen in FDA docs)", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeKilogramId, "kg", "Kilogram", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeLiterId, "l", "Liter", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeMilliliterId, "ml", "Milliliter", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeTeaspoonId, "tsp", "Teaspoon", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeTablespoonId, "tbsp", "Tablespoon", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeCupId, "cup", "Cup", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeOunceId, "oz", "Ounce", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypePoundId, "lb", "Pound", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypePintId, "pint", "Pint", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeQuartId, "quart", "Quart", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeGallonId, "gallon", "Gallon", DateTime.UtcNow, SystemPersonId },
                    { 4019L, "slice", "Slice", DateTime.UtcNow, SystemPersonId },
                    { 4020L, "piece", "Piece", DateTime.UtcNow, SystemPersonId },
                    { 4021L, "can", "Can", DateTime.UtcNow, SystemPersonId },
                    { 4022L, "bottle", "Bottle", DateTime.UtcNow, SystemPersonId },
                    { 4023L, "package", "Package", DateTime.UtcNow, SystemPersonId },
                    { 4024L, "clove", "Clove (e.g., garlic)", DateTime.UtcNow, SystemPersonId },
                    { 4025L, "sprig", "Sprig (e.g., herb)", DateTime.UtcNow, SystemPersonId },
                    { 4026L, "leaf", "Leaf (e.g., lettuce)", DateTime.UtcNow, SystemPersonId },
                    { 4027L, "stalk", "Stalk (e.g., celery)", DateTime.UtcNow, SystemPersonId },
                    { 4028L, "pinch", "Pinch", DateTime.UtcNow, SystemPersonId },
                    { 4029L, "dash", "Dash", DateTime.UtcNow, SystemPersonId },
                    { 4030L, "splash", "Splash", DateTime.UtcNow, SystemPersonId },
                    { MeasurementTypeKcalId, "kcal", "Kilocalorie (energy unit)", DateTime.UtcNow, SystemPersonId }
                });

            long[] measurementTypeIds = new long[] {
                MeasurementTypeUnknownId, MeasurementTypeToTasteId, MeasurementTypeEachId,
                MeasurementTypeGramId, MeasurementTypeMilligramId, MeasurementTypeMicrogramId,
                MeasurementTypeMcgId,
                MeasurementTypeKilogramId, MeasurementTypeLiterId, MeasurementTypeMilliliterId,
                MeasurementTypeTeaspoonId, MeasurementTypeTablespoonId, MeasurementTypeCupId,
                MeasurementTypeOunceId, MeasurementTypePoundId, MeasurementTypePintId,
                MeasurementTypeQuartId, MeasurementTypeGallonId, 4019L, 4020L, 4021L, 4022L,
                4023L, 4024L, 4025L, 4026L, 4027L, 4028L, 4029L, 4030L, MeasurementTypeKcalId
            };

            foreach (long id in measurementTypeIds)
            {
                migrationBuilder.InsertData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    columns: new[] { "ReferenceId", "GroupId" },
                    values: new object[] { id, measurementGroupId });
            }
        }

        public static void RemoveMeasurementTypes(MigrationBuilder migrationBuilder)
        {
            long measurementGroupId = (long)ReferenceDiscriminatorEnum.MeasurementType;

            long[] measurementTypeIds = new long[] {
                MeasurementTypeUnknownId, MeasurementTypeToTasteId, MeasurementTypeEachId,
                MeasurementTypeGramId, MeasurementTypeMilligramId, MeasurementTypeMicrogramId,
                MeasurementTypeMcgId,
                MeasurementTypeKilogramId, MeasurementTypeLiterId, MeasurementTypeMilliliterId,
                MeasurementTypeTeaspoonId, MeasurementTypeTablespoonId, MeasurementTypeCupId,
                MeasurementTypeOunceId, MeasurementTypePoundId, MeasurementTypePintId,
                MeasurementTypeQuartId, MeasurementTypeGallonId, 4019L, 4020L, 4021L, 4022L,
                4023L, 4024L, 4025L, 4026L, 4027L, 4028L, 4029L, 4030L, MeasurementTypeKcalId
            };

            foreach (long id in measurementTypeIds)
            {
                migrationBuilder.DeleteData(
                    schema: "reference",
                    table: "ReferenceIndex",
                    keyColumns: new[] { "ReferenceId", "GroupId" },
                    keyValues: new object[] { id, measurementGroupId });
            }

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "Reference",
                keyColumn: "Id",
                keyValues: measurementTypeIds.Cast<object>().ToArray());
        }

        // Add Goal Types (matching FDA demographic categories)
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
                    { GoalTypeGeneralAdultId, "General Adult", "Broader dietary guidelines for typical healthy adults (non-FDA specific).", DateTime.UtcNow, SystemPersonId }
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

        // Add all Nutrients from DRVs and RDIs, including ParentNutrientId for components
        public static void AddNutrientTypes(MigrationBuilder migrationBuilder)
        {
            // First, insert all parent nutrients
            migrationBuilder.InsertData(
                schema: "nutrient",
                table: "Nutrient",
                columns: new[] { "Id", "Name", "Description", "DefaultMeasurementTypeId", "CreatedDate", "CreatedByPersonId", "ParentNutrientId" },
                values: new object[,]
                {
                    // DRVs (Food Components) - Parent Nutrients
                    { NutrientFatId, "Fat", "Total fat content.", MeasurementTypeGramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientTotalCarbohydratesId, "Total Carbohydrates", "Total carbohydrate content.", MeasurementTypeGramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientProteinId, "Protein", "Protein content.", MeasurementTypeGramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCholesterolId, "Cholesterol", "Cholesterol content.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientSodiumId, "Sodium", "Sodium content.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCaloriesFDCId, "Calories", "Energy content of food. Often referred to as 'Energy' in FDC API.", MeasurementTypeKcalId, DateTime.UtcNow, SystemPersonId, null },

                    // RDIs (Vitamins) - No explicit parents in the provided context
                    { NutrientVitaminAId, "Vitamin A", "Vitamin A, Retinol Activity Equivalents (RAE).", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminCId, "Vitamin C", "Ascorbic acid.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminDId, "Vitamin D", "Vitamin D.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminEId, "Vitamin E", "Vitamin E, alpha-tocopherol equivalents.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminKId, "Vitamin K", "Vitamin K.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientThiaminId, "Thiamin", "Vitamin B1.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientRiboflavinId, "Riboflavin", "Vitamin B2.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientNiacinId, "Niacin", "Vitamin B3, Niacin Equivalents (NE).", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminB6Id, "Vitamin B6", "Pyridoxine.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientFolateId, "Folate", "Folate, Dietary Folate Equivalents (DFE).", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientVitaminB12Id, "Vitamin B12", "Cobalamin.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientBiotinId, "Biotin", "Vitamin B7.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientPantothenicAcidId, "Pantothenic Acid", "Vitamin B5.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCholineId, "Choline", "Choline.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },

                    // RDIs (Minerals) - No explicit parents in the provided context
                    { NutrientCalciumId, "Calcium", "Calcium.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientIronId, "Iron", "Iron.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientPhosphorusId, "Phosphorus", "Phosphorus.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientIodineId, "Iodine", "Iodine.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientMagnesiumId, "Magnesium", "Magnesium.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientZincId, "Zinc", "Zinc.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientSeleniumId, "Selenium", "Selenium.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientCopperId, "Copper", "Copper.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientManganeseId, "Manganese", "Manganese.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientChromiumId, "Chromium", "Chromium.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientMolybdenumId, "Molybdenum", "Molybdenum.", MeasurementTypeMcgId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientChlorideId, "Chloride", "Chloride.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null },
                    { NutrientPotassiumId, "Potassium", "Potassium.", MeasurementTypeMilligramId, DateTime.UtcNow, SystemPersonId, null }
                });

            // Now, insert child nutrients, linking them to their parents
            migrationBuilder.InsertData(
                schema: "nutrient",
                table: "Nutrient",
                columns: new[] { "Id", "Name", "Description", "DefaultMeasurementTypeId", "CreatedDate", "CreatedByPersonId", "ParentNutrientId" },
                values: new object[,]
                {
                    // Components of Fat
                    { NutrientSaturatedFatId, "Saturated Fat", "Saturated fatty acids.", MeasurementTypeGramId, DateTime.UtcNow, SystemPersonId, NutrientFatId },
                    // If you add Monounsaturated Fat or Polyunsaturated Fat later, they would also link to NutrientFatId

                    // Components of Total Carbohydrates
                    { NutrientDietaryFiberId, "Dietary Fiber", "Dietary fiber content.", MeasurementTypeGramId, DateTime.UtcNow, SystemPersonId, NutrientTotalCarbohydratesId },
                    { NutrientAddedSugarsId, "Added Sugars", "Added sugars content.", MeasurementTypeGramId, DateTime.UtcNow, SystemPersonId, NutrientTotalCarbohydratesId }
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

            // Order of deletion matters for self-referencing FK: delete children before parents
            // Or set parent IDs to null first. Simplest is to delete all by ID.
            // EF Core's DeleteData handles this if you delete the children first.
            // Here, we just delete all by ID, assuming the database will handle FK constraints
            // (e.g., if ON DELETE CASCADE is configured, or if FK is nullable and children are deleted first).
            // Since ParentNutrientId is nullable, this is fine.
            migrationBuilder.DeleteData(
                schema: "nutrient",
                table: "Nutrient",
                keyColumn: "Id",
                keyValues: nutrientIds.Cast<object>().ToArray());
        }

        // Removed AddNutrientComponents and RemoveNutrientComponents methods

        // Add Nutrient Guidelines based on FDA DRVs and RDIs from PDFs
        public static void AddNutrientGuidelines(MigrationBuilder migrationBuilder)
        {
            // Helper to generate a new unique ID for guidelines
            long GetNextGuidelineId() => nextGuidelineId++;

            // Data from "Daily-Reference-Values-(DRVs)-under-the-New-NFL.pdf" (Page 2)
            // "Adults and Children >= 4 years"
            migrationBuilder.InsertData(
                schema: "nutrient",
                table: "NutrientGuideline",
                columns: new[] { "Id", "NutrientId", "GoalTypeId", "MeasurementTypeId", "MinAmount", "MaxAmount", "RecommendedAmount", "Notes", "CreatedDate", "CreatedByPersonId" },
                values: new object[,]
                {
                    // DRVs - Adults and Children >= 4 years
                    { GetNextGuidelineId(), NutrientFatId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeGramId, null, null, 78.0m, "DRV for Fat based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSaturatedFatId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeGramId, null, null, 20.0m, "DRV for Saturated Fat based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeGramId, null, null, 275.0m, "DRV for Total Carbohydrates based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSodiumId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 2300.0m, "DRV for Sodium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientDietaryFiberId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeGramId, null, null, 28.0m, "DRV for Dietary Fiber based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeGramId, null, null, 50.0m, "DRV for Protein based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientAddedSugarsId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeGramId, null, null, 50.0m, "DRV for Added Sugars based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },

                    // DRVs - Infants through 12 months
                    { GetNextGuidelineId(), NutrientFatId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeGramId, null, null, 30.0m, "DRV for Fat.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeGramId, null, null, 95.0m, "DRV for Total Carbohydrates.", DateTime.UtcNow, SystemPersonId },

                    // DRVs - Children 1 through 3 years
                    { GetNextGuidelineId(), NutrientFatId, GoalTypeChildren1Through3YearsId, MeasurementTypeGramId, null, null, 39.0m, "DRV for Fat based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSaturatedFatId, GoalTypeChildren1Through3YearsId, MeasurementTypeGramId, null, null, 10.0m, "DRV for Saturated Fat based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypeChildren1Through3YearsId, MeasurementTypeGramId, null, null, 150.0m, "DRV for Total Carbohydrates based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSodiumId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 1500.0m, "DRV for Sodium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientDietaryFiberId, GoalTypeChildren1Through3YearsId, MeasurementTypeGramId, null, null, 14.0m, "DRV for Dietary Fiber based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypeChildren1Through3YearsId, MeasurementTypeGramId, null, null, 13.0m, "DRV for Protein based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientAddedSugarsId, GoalTypeChildren1Through3YearsId, MeasurementTypeGramId, null, null, 25.0m, "DRV for Added Sugars based on 1,000 kcal diet.", DateTime.UtcNow, SystemPersonId },

                    // DRVs - Pregnant women and lactating women
                    { GetNextGuidelineId(), NutrientFatId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeGramId, null, null, 78.0m, "DRV for Fat based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholesterolId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 300.0m, "DRV for Cholesterol.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientTotalCarbohydratesId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeGramId, null, null, 275.0m, "DRV for Total Carbohydrates based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSodiumId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 2300.0m, "DRV for Sodium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientDietaryFiberId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeGramId, null, null, 28.0m, "DRV for Dietary Fiber based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientAddedSugarsId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeGramId, null, null, 50.0m, "DRV for Added Sugars based on 2,000 kcal diet.", DateTime.UtcNow, SystemPersonId },

                    // Data from "Reference-Daily-Intakes-(RDIs)-in-the-New-Nutrition-Facts-Label.pdf" (Page 2 & 3)
                    // RDIs - Adults and Children >= 4 years
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 900.0m, "RDI for Vitamin A (RAE).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 90.0m, "RDI for Vitamin C.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 1300.0m, "RDI for Calcium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 18.0m, "RDI for Iron.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 20.0m, "RDI for Vitamin D.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 15.0m, "RDI for Vitamin E (alpha-tocopherol).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 120.0m, "RDI for Vitamin K.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 1.2m, "RDI for Thiamin (Vitamin B1).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 1.3m, "RDI for Riboflavin (Vitamin B2).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 16.0m, "RDI for Niacin (NE).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 1.7m, "RDI for Vitamin B6.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 400.0m, "RDI for Folate (DFE).", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 2.4m, "RDI for Vitamin B12.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 30.0m, "RDI for Biotin.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 5.0m, "RDI for Pantothenic Acid.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 1250.0m, "RDI for Phosphorus.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 150.0m, "RDI for Iodine.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 420.0m, "RDI for Magnesium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 11.0m, "RDI for Zinc.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 55.0m, "RDI for Selenium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 0.9m, "RDI for Copper.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 2.3m, "RDI for Manganese.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 35.0m, "RDI for Chromium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMcgId, null, null, 45.0m, "RDI for Molybdenum.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 2300.0m, "RDI for Chloride.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 4700.0m, "RDI for Potassium.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypeAdultsAndChildren4PlusId, MeasurementTypeMilligramId, null, null, 550.0m, "RDI for Choline.", DateTime.UtcNow, SystemPersonId },

                    // RDIs - Infants through 12 months
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 500.0m, "RDI for Vitamin A (RAE) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 50.0m, "RDI for Vitamin C for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 260.0m, "RDI for Calcium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 11.0m, "RDI for Iron for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 10.0m, "RDI for Vitamin D for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 5.0m, "RDI for Vitamin E for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 2.5m, "RDI for Vitamin K for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 0.3m, "RDI for Thiamin (Vitamin B1) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 0.4m, "RDI for Riboflavin (Vitamin B2) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 4.0m, "RDI for Niacin (NE) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 0.3m, "RDI for Vitamin B6 for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 80.0m, "RDI for Folate (DFE) for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 0.5m, "RDI for Vitamin B12 for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 6.0m, "RDI for Biotin for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 1.8m, "RDI for Pantothenic Acid for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 275.0m, "RDI for Phosphorus for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 130.0m, "RDI for Iodine for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 75.0m, "RDI for Magnesium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 3.0m, "RDI for Zinc for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 20.0m, "RDI for Selenium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 0.2m, "RDI for Copper for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 0.6m, "RDI for Manganese for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 5.5m, "RDI for Chromium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMcgId, null, null, 3.0m, "RDI for Molybdenum for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 570.0m, "RDI for Chloride for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 700.0m, "RDI for Potassium for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeMilligramId, null, null, 150.0m, "RDI for Choline for infants.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypeInfantsThrough12MonthsId, MeasurementTypeGramId, null, null, 11.0m, "RDI for Protein for infants.", DateTime.UtcNow, SystemPersonId },

                    // RDIs - Children 1 through 3 years
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 300.0m, "RDI for Vitamin A (RAE) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 15.0m, "RDI for Vitamin C for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 700.0m, "RDI for Calcium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 7.0m, "RDI for Iron for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 15.0m, "RDI for Vitamin D for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 6.0m, "RDI for Vitamin E for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 30.0m, "RDI for Vitamin K for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 0.5m, "RDI for Thiamin (Vitamin B1) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 0.5m, "RDI for Riboflavin (Vitamin B2) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 6.0m, "RDI for Niacin (NE) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 0.5m, "RDI for Vitamin B6 for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 150.0m, "RDI for Folate (DFE) for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 0.9m, "RDI for Vitamin B12 for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 8.0m, "RDI for Biotin for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 2.0m, "RDI for Pantothenic Acid for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 460.0m, "RDI for Phosphorus for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 90.0m, "RDI for Iodine for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 80.0m, "RDI for Magnesium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 3.0m, "RDI for Zinc for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 20.0m, "RDI for Selenium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 0.3m, "RDI for Copper for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 1.2m, "RDI for Manganese for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 11.0m, "RDI for Chromium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypeChildren1Through3YearsId, MeasurementTypeMcgId, null, null, 17.0m, "RDI for Molybdenum for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 1500.0m, "RDI for Chloride for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 3000.0m, "RDI for Potassium for children 1-3 years.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypeChildren1Through3YearsId, MeasurementTypeMilligramId, null, null, 200.0m, "RDI for Choline for children 1-3 years.", DateTime.UtcNow, SystemPersonId },

                    // RDIs - Pregnant women and lactating women
                    { GetNextGuidelineId(), NutrientVitaminAId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 1300.0m, "RDI for Vitamin A (RAE) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminCId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 120.0m, "RDI for Vitamin C for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCalciumId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 1300.0m, "RDI for Calcium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIronId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 27.0m, "RDI for Iron for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminDId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 15.0m, "RDI for Vitamin D for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminEId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 19.0m, "RDI for Vitamin E for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminKId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 90.0m, "RDI for Vitamin K for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientThiaminId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 1.4m, "RDI for Thiamin (Vitamin B1) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientRiboflavinId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 1.6m, "RDI for Riboflavin (Vitamin B2) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientNiacinId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 18.0m, "RDI for Niacin (NE) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB6Id, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 2.0m, "RDI for Vitamin B6 for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientFolateId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 600.0m, "RDI for Folate (DFE) for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientVitaminB12Id, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 2.8m, "RDI for Vitamin B12 for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientBiotinId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 35.0m, "RDI for Biotin for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPantothenicAcidId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 7.0m, "RDI for Pantothenic Acid for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPhosphorusId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 1250.0m, "RDI for Phosphorus for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientIodineId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 290.0m, "RDI for Iodine for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMagnesiumId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 400.0m, "RDI for Magnesium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientZincId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 13.0m, "RDI for Zinc for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientSeleniumId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 70.0m, "RDI for Selenium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCopperId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 1.3m, "RDI for Copper for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientManganeseId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 2.6m, "RDI for Manganese for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChromiumId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 45.0m, "RDI for Chromium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientMolybdenumId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMcgId, null, null, 50.0m, "RDI for Molybdenum for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientChlorideId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 2300.0m, "RDI for Chloride for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientPotassiumId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 5100.0m, "RDI for Potassium for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientCholineId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeMilligramId, null, null, 550.0m, "RDI for Choline for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId },
                    { GetNextGuidelineId(), NutrientProteinId, GoalTypePregnantAndLactatingWomenId, MeasurementTypeGramId, null, null, 71.0m, "RDI for Protein for pregnant/lactating women.", DateTime.UtcNow, SystemPersonId }
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
    }
}
