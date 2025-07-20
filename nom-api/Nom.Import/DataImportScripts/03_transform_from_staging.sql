-- File: nom-api/Nom.Import/DataImportScripts/03_transform_from_staging.sql
-- Description: Transforms and inserts the data from the staging tables into the
-- final application tables ("Nutrients", "Ingredients", "IngredientNutrients").

-- Truncate the final tables to ensure a clean import and reset identity sequences.
TRUNCATE TABLE "Nutrients", "Ingredients", "IngredientNutrients" RESTART IDENTITY CASCADE;

-- 1. Populate the "Nutrients" table from the staging nutrient data.
INSERT INTO "Nutrients" ("Id", "Name", "UnitName")
SELECT s.id, s.name, s.unit_name
FROM "Staging_Nutrient" s
ON CONFLICT ("Id") DO NOTHING; -- Avoid errors if a nutrient already exists

-- 2. Populate the "Ingredients" table with foods from the "SR Legacy Food" data type,
-- as these provide a high-quality, foundational dataset.
INSERT INTO "Ingredients" ("FdcId", "Name")
SELECT s.fdc_id, s.description
FROM "Staging_Food" s
WHERE s.data_type = 'sr_legacy_food'
ON CONFLICT ("FdcId") DO NOTHING; -- Avoid duplicate ingredients

-- 3. Populate the "IngredientNutrients" linking table.
-- This joins the staged food nutrient data with the newly created Ingredients and Nutrients.
INSERT INTO "IngredientNutrients" ("IngredientId", "NutrientId", "Amount")
SELECT
    i."Id" AS "IngredientId",
    sfn.nutrient_id AS "NutrientId",
    sfn.amount
FROM "Staging_Food_Nutrient" sfn
-- Join with the final Ingredients table to get our internal Ingredient ID
JOIN "Ingredients" i ON i."FdcId" = sfn.fdc_id
-- Ensure the nutrient exists in our final Nutrients table
WHERE EXISTS (SELECT 1 FROM "Nutrients" n WHERE n."Id" = sfn.nutrient_id)
  AND sfn.amount IS NOT NULL;

-- 4. Clean up the staging tables after the import is complete.
DROP TABLE "Staging_Food";
DROP TABLE "Staging_Nutrient";
DROP TABLE "Staging_Food_Nutrient";
