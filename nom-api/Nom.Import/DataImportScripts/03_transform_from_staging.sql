-- File: nom-api/Nom.Import/DataImportSqlScripts/03_transform_from_staging.sql
-- Description: Transforms and inserts the data from the staging tables into the
-- final application tables using MERGE for robust upsert logic.

-- Truncate the final tables to ensure a clean import and reset identity sequences.
TRUNCATE TABLE nutrient."Nutrient", recipe."Ingredient", nutrient."IngredientNutrient" RESTART IDENTITY CASCADE;

-- 1. Populate the "Nutrient" table from the staging nutrient data using MERGE.
MERGE INTO nutrient."Nutrient" AS target
USING (
    SELECT DISTINCT ON (s.name, s.unit_name)
        s.id,
        s.name,
        ref."Id" AS "MeasurementTypeId"
    FROM "Staging_Nutrient" s
    JOIN reference."Reference" ref ON LOWER(ref."Name") = LOWER(s.unit_name)
    WHERE EXISTS (
        SELECT 1 FROM reference."Group" g
        JOIN reference."ReferenceIndex" ri ON g."Id" = ri."GroupId"
        WHERE g."Name" = 'Measurement Types' AND ri."ReferenceId" = ref."Id"
    )
) AS source 
ON target."Name" = source.name AND target."DefaultMeasurementTypeId" = source."MeasurementTypeId"
WHEN NOT MATCHED THEN
    INSERT ("Id", "Name", "FdcId", "DefaultMeasurementTypeId", "CreatedDate")
    VALUES (source.id, source.name, source.id::TEXT, source."MeasurementTypeId", NOW());

-- 2. Populate the "Ingredient" table with foods using MERGE.
MERGE INTO recipe."Ingredient" AS target
USING (
    SELECT DISTINCT ON (s.description)
        s.fdc_id::TEXT AS "FdcId",
        s.description
    FROM "Staging_Food" s
    WHERE s.data_type = 'sr_legacy_food' OR s.data_type = 'branded_food'
) AS source ON target."Name" = source.description
WHEN NOT MATCHED THEN
    INSERT ("FdcId", "Name", "Description", "CreatedDate")
    VALUES (source."FdcId", source.description, source.description, NOW());

-- 3. Populate the "IngredientNutrient" linking table.
-- CORRECTED: This now de-duplicates the source data and joins on the final de-duplicated
-- Ingredient and Nutrient tables to prevent unique constraint violations.
INSERT INTO nutrient."IngredientNutrient" ("IngredientId", "NutrientId", "Amount", "MeasurementTypeId", "FdcId", "CreatedDate")
SELECT
    i."Id" AS "IngredientId",
    sfn.nutrient_id AS "NutrientId",
    NULLIF(sfn.amount, '')::NUMERIC,
    n."DefaultMeasurementTypeId",
    sfn.fdc_id::TEXT,
    NOW()
FROM (
    -- First, select distinct nutrient information for each food item
    SELECT DISTINCT ON (fdc_id, nutrient_id) *
    FROM "Staging_Food_Nutrient"
) sfn
-- Join with the final de-duplicated Ingredients table. This is the crucial step
-- that ensures we only consider nutrients for ingredients that actually exist in our system.
JOIN recipe."Ingredient" i ON i."FdcId" = sfn.fdc_id::TEXT
-- Join with the final Nutrients table to get the MeasurementTypeId
JOIN nutrient."Nutrient" n ON n."Id" = sfn.nutrient_id
WHERE NULLIF(sfn.amount, '') IS NOT NULL;

-- 4. Clean up the staging tables after the import is complete.
--DROP TABLE "Staging_Food";
--DROP TABLE "Staging_Nutrient";
--DROP TABLE "Staging_Food_Nutrient";
