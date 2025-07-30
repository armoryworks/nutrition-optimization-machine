-- Enhanced transform script with quality filtering and comprehensive data import
-- Fixed version - removes QualityScore columns and uses correct schema

-- Truncate the final tables to ensure a clean import and reset identity sequences.
TRUNCATE TABLE nutrient."Nutrient", recipe."Ingredient", nutrient."IngredientNutrient", nutrient."NutrientGuideline" RESTART IDENTITY CASCADE;

-- 1. Populate the "Nutrient" table from the enhanced staging nutrient data using MERGE.
MERGE INTO nutrient."Nutrient" AS target
USING (
    SELECT DISTINCT ON (s.name, s.unit_name)
        CASE 
            WHEN s.id ~ '^[0-9]+$' THEN s.id::BIGINT 
            ELSE NULL 
        END as nutrient_id,
        s.name,
        CASE 
            WHEN s.id ~ '^[0-9]+$' THEN s.id 
            ELSE NULL 
        END as fdc_id,
        ref."Id" AS measurement_type_id,
        NOW() as created_date
    FROM "Staging_Nutrient_Enhanced" s
    JOIN reference."Reference" ref ON LOWER(ref."Name") = LOWER(s.unit_name)
    WHERE EXISTS (
        SELECT 1 FROM reference."Group" g
        JOIN reference."ReferenceIndex" ri ON g."Id" = ri."GroupId"
        WHERE g."Name" = 'Measurement Types' AND ri."ReferenceId" = ref."Id"
    )
    AND s.quality_score >= 0.5
    AND s.id != '' 
    AND s.id IS NOT NULL
    AND LENGTH(s.id) > 0
    AND s.id ~ '^[0-9]+$'
    AND CASE 
        WHEN s.id ~ '^[0-9]+$' THEN s.id::BIGINT 
        ELSE NULL 
    END IS NOT NULL
) AS source 
ON target."Name" = source.name AND target."DefaultMeasurementTypeId" = source.measurement_type_id
WHEN MATCHED THEN
    UPDATE SET "LastModifiedDate" = NOW()
WHEN NOT MATCHED THEN
    INSERT ("Id", "Name", "FdcId", "DefaultMeasurementTypeId", "CreatedDate")
    VALUES (source.nutrient_id, source.name, source.fdc_id, source.measurement_type_id, source.created_date);

-- 2. Populate the "Ingredient" table with quality-filtered foods using MERGE.
MERGE INTO recipe."Ingredient" AS target
USING (
    SELECT DISTINCT
        fdc_id,
        description,
        description as description_text,
        data_type,
        NOW() as created_date,
        9000 as curation_status_id
    FROM (
        SELECT 
            fdc_id,
            description,
            data_type,
            quality_score,
            ROW_NUMBER() OVER (PARTITION BY description ORDER BY quality_score DESC, fdc_id) as rn
        FROM "Staging_Food_Enhanced"
        WHERE quality_score >= 0.5
        AND description IS NOT NULL 
        AND description != ''
        AND LENGTH(description) <= 150
    ) ranked
    WHERE rn = 1
) AS source ON target."FdcId" = source.fdc_id
WHEN MATCHED THEN
    UPDATE SET 
        "Name" = source.description,
        "Description" = source.description_text,
        "FdcDataType" = source.data_type,
        "LastModifiedDate" = NOW()
WHEN NOT MATCHED THEN
    INSERT ("FdcId", "Name", "Description", "FdcDataType", "CreatedDate", "CurationStatusId")
    VALUES (source.fdc_id, source.description, source.description_text, source.data_type, source.created_date, source.curation_status_id);

-- 3. Populate the "NutrientGuideline" table from enhanced guidelines using MERGE
MERGE INTO nutrient."NutrientGuideline" AS target
USING (
    SELECT DISTINCT
        n."Id" AS nutrient_id,
        goal."Id" AS goal_type_id,
        unit."Id" AS measurement_type_id,
        NULLIF(sg."RecommendedAmount", '')::NUMERIC as recommended_amount,
        NULLIF(sg."MaxAmount", '')::NUMERIC as max_amount,
        'Imported from FDA Labeling Guidelines' AS notes,
        NOW() as created_date
    FROM "Staging_Guideline" sg
    JOIN nutrient."Nutrient" n ON n."Name" = sg."NutrientName"
    JOIN reference."Reference" goal ON goal."Name" = sg."GoalTypeName"
    JOIN reference."Reference" unit ON unit."Name" = sg."UnitName"
) AS source ON target."NutrientId" = source.nutrient_id AND target."GoalTypeId" = source.goal_type_id
WHEN MATCHED THEN
    UPDATE SET
        "RecommendedAmount" = source.recommended_amount,
        "MaxAmount" = source.max_amount,
        "LastModifiedDate" = NOW()
WHEN NOT MATCHED THEN
    INSERT ("NutrientId", "GoalTypeId", "MeasurementTypeId", "RecommendedAmount", "MaxAmount", "Notes", "CreatedDate")
    VALUES (source.nutrient_id, source.goal_type_id, source.measurement_type_id, source.recommended_amount, source.max_amount, source.notes, source.created_date);

-- 4. Populate the "IngredientNutrient" linking table with quality filtering using MERGE.
MERGE INTO nutrient."IngredientNutrient" AS target
USING (
    SELECT DISTINCT
        i."Id" AS ingredient_id,
        sfn.nutrient_id AS nutrient_id,
        NULLIF(sfn.amount, '')::NUMERIC as amount,
        n."DefaultMeasurementTypeId" as measurement_type_id,
        sfn.fdc_id::TEXT as fdc_id,
        NOW() as created_date
    FROM "Staging_Food_Nutrient_Enhanced" sfn
    JOIN recipe."Ingredient" i ON i."FdcId" = sfn.fdc_id::TEXT
    JOIN nutrient."Nutrient" n ON n."Id" = sfn.nutrient_id
    WHERE NULLIF(sfn.amount, '') IS NOT NULL
    AND sfn.quality_score >= 0.5
    AND (sfn.min_year_acquired IS NULL OR NULLIF(sfn.min_year_acquired, '')::INTEGER >= 2010)
) AS source ON target."IngredientId" = source.ingredient_id AND target."NutrientId" = source.nutrient_id
WHEN MATCHED THEN
    UPDATE SET 
        "Amount" = source.amount,
        "LastModifiedDate" = NOW()
WHEN NOT MATCHED THEN
    INSERT ("IngredientId", "NutrientId", "Amount", "MeasurementTypeId", "FdcId", "CreatedDate")
    VALUES (source.ingredient_id, source.nutrient_id, source.amount, source.measurement_type_id, source.fdc_id, source.created_date);

-- 5. Create indexes for better performance (using existing columns)
CREATE INDEX IF NOT EXISTS idx_ingredient_fdc_data_type ON recipe."Ingredient" ("FdcDataType");
CREATE INDEX IF NOT EXISTS idx_ingredient_name_length ON recipe."Ingredient" (LENGTH("Name"));
CREATE INDEX IF NOT EXISTS idx_ingredient_fdc_id ON recipe."Ingredient" ("FdcId");
CREATE INDEX IF NOT EXISTS idx_nutrient_fdc_id ON nutrient."Nutrient" ("FdcId");
CREATE INDEX IF NOT EXISTS idx_ingredient_nutrient_amount ON nutrient."IngredientNutrient" ("Amount");

-- 6. Create summary statistics for ingredient assessment
DROP VIEW IF EXISTS recipe."IngredientSummary";
CREATE OR REPLACE VIEW recipe."IngredientSummary" AS
SELECT 
    "FdcDataType",
    COUNT(*) as total_ingredients,
    COUNT(CASE WHEN LENGTH("Name") > 50 THEN 1 END) as long_name_count,
    COUNT(CASE WHEN LENGTH("Name") <= 20 THEN 1 END) as short_name_count,
    AVG(LENGTH("Name")) as avg_name_length
FROM recipe."Ingredient"
GROUP BY "FdcDataType";

-- 7. Create materialized view for common ingredient searches
DROP MATERIALIZED VIEW IF EXISTS recipe."IngredientSearchView";
CREATE MATERIALIZED VIEW recipe."IngredientSearchView" AS
SELECT 
    i."Id",
    i."Name",
    i."Description",
    i."FdcDataType",
    COUNT(inr."NutrientId") as nutrient_count
FROM recipe."Ingredient" i
LEFT JOIN nutrient."IngredientNutrient" inr ON i."Id" = inr."IngredientId"
GROUP BY i."Id", i."Name", i."Description", i."FdcDataType";

CREATE INDEX IF NOT EXISTS idx_ingredient_search_name ON recipe."IngredientSearchView" USING gin(to_tsvector('english', "Name"));
CREATE INDEX IF NOT EXISTS idx_ingredient_search_type ON recipe."IngredientSearchView" ("FdcDataType");

-- 8. Clean up staging tables (optional - uncomment to remove staging data)
-- DROP TABLE IF EXISTS "Staging_Food_Enhanced";
-- DROP TABLE IF EXISTS "Staging_Nutrient_Enhanced";
-- DROP TABLE IF EXISTS "Staging_Food_Nutrient_Enhanced";
-- DROP TABLE IF EXISTS "Staging_Guideline";
-- DROP TABLE IF EXISTS "Staging_Measure_Unit";
-- DROP TABLE IF EXISTS "Staging_Food_Category";
-- DROP TABLE IF EXISTS "Staging_Foundation_Food";
-- DROP TABLE IF EXISTS "Staging_Survey_Food";
-- DROP TABLE IF EXISTS "Staging_Branded_Food"; 