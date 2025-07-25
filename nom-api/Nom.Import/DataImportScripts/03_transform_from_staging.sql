-- Truncate the final tables to ensure a clean import and reset identity sequences.
TRUNCATE TABLE nutrient."Nutrient", recipe."Ingredient", nutrient."IngredientNutrient", nutrient."NutrientGuideline" RESTART IDENTITY CASCADE;

-- 1. Populate the "Nutrient" table from the staging nutrient data using MERGE.
MERGE INTO nutrient."Nutrient" AS target
USING (
    SELECT DISTINCT ON (s.name, s.unit_name)
        s.id::BIGINT,
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
        s.description,
        s.data_type
    FROM "Staging_Food" s
) AS source ON target."Name" = source.description
WHEN NOT MATCHED THEN
    INSERT ("FdcId", "Name", "Description", "FdcDataType", "CreatedDate", "CurationStatusId")
    VALUES (source."FdcId", source.description, source.description, source.data_type, NOW(), 9000);

-- *** ADDED: 3. Populate the "NutrientGuideline" table ***
INSERT INTO nutrient."NutrientGuideline" (
    "NutrientId",
    "GoalTypeId",
    "MeasurementTypeId",
    "RecommendedAmount",
    "MaxAmount",
    "Notes",
    "CreatedDate"
)
SELECT
    n."Id" AS "NutrientId",
    goal."Id" AS "GoalTypeId",
    unit."Id" AS "MeasurementTypeId",
    NULLIF(sg."RecommendedAmount", '')::NUMERIC,
    NULLIF(sg."MaxAmount", '')::NUMERIC,
    'Imported from FDA Labeling Guidelines' AS "Notes",
    NOW()
FROM
    "Staging_Guideline" sg
-- Join to get the NutrientId from the nutrient name
JOIN nutrient."Nutrient" n ON n."Name" = sg."NutrientName"
-- Join to get the GoalTypeId from the goal/demographic name
JOIN reference."Reference" goal ON goal."Name" = sg."GoalTypeName"
-- Join to get the MeasurementTypeId from the unit name
JOIN reference."Reference" unit ON unit."Name" = sg."UnitName";


-- 4. Populate the "IngredientNutrient" linking table.
INSERT INTO nutrient."IngredientNutrient" ("IngredientId", "NutrientId", "Amount", "MeasurementTypeId", "FdcId", "CreatedDate")
SELECT
    i."Id" AS "IngredientId",
    sfn.nutrient_id AS "NutrientId",
    NULLIF(sfn.amount, '')::NUMERIC,
    n."DefaultMeasurementTypeId",
    sfn.fdc_id::TEXT,
    NOW()
FROM (
    SELECT DISTINCT ON (fdc_id, nutrient_id) *
    FROM "Staging_Food_Nutrient"
) sfn
JOIN recipe."Ingredient" i ON i."FdcId" = sfn.fdc_id::TEXT
JOIN nutrient."Nutrient" n ON n."Id" = sfn.nutrient_id
WHERE NULLIF(sfn.amount, '') IS NOT NULL;

-- 5. Clean up the staging tables after the import is complete.
-- DROP TABLE "Staging_Food";
-- DROP TABLE "Staging_Nutrient";
-- DROP TABLE "Staging_Food_Nutrient";
-- DROP TABLE "Staging_Guideline";