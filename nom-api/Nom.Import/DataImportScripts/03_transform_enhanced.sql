-- Enhanced transform script with quality filtering and comprehensive data import

-- Truncate the final tables to ensure a clean import and reset identity sequences.
TRUNCATE TABLE nutrient."Nutrient", recipe."Ingredient", nutrient."IngredientNutrient", nutrient."NutrientGuideline" RESTART IDENTITY CASCADE;

-- 1. Populate the "Nutrient" table from the enhanced staging nutrient data using MERGE.
MERGE INTO nutrient."Nutrient" AS target
USING (
    SELECT DISTINCT ON (s.name, s.unit_name)
        s.id::BIGINT,
        s.name,
        ref."Id" AS "MeasurementTypeId",
        s.quality_score
    FROM "Staging_Nutrient_Enhanced" s
    JOIN reference."Reference" ref ON LOWER(ref."Name") = LOWER(s.unit_name)
    WHERE EXISTS (
        SELECT 1 FROM reference."Group" g
        JOIN reference."ReferenceIndex" ri ON g."Id" = ri."GroupId"
        WHERE g."Name" = 'Measurement Types' AND ri."ReferenceId" = ref."Id"
    )
    AND s.quality_score >= 0.5
) AS source 
ON target."Name" = source.name AND target."DefaultMeasurementTypeId" = source."MeasurementTypeId"
WHEN NOT MATCHED THEN
    INSERT ("Id", "Name", "FdcId", "DefaultMeasurementTypeId", "QualityScore", "CreatedDate")
    VALUES (source.id, source.name, source.id::TEXT, source."MeasurementTypeId", source.quality_score, NOW())
WHEN MATCHED THEN
    UPDATE SET "QualityScore" = source.quality_score, "UpdatedDate" = NOW();

-- 2. Populate the "Ingredient" table with quality-filtered foods using MERGE.
MERGE INTO recipe."Ingredient" AS target
USING (
    SELECT DISTINCT ON (s.description)
        s.fdc_id::TEXT AS "FdcId",
        s.description,
        s.data_type,
        s.quality_score
    FROM "Staging_Food_Enhanced" s
    WHERE s.description IS NOT NULL 
    AND s.description != ''
    AND LENGTH(s.description) <= 150
    AND s.quality_score >= 0.5
) AS source ON target."Name" = source.description
WHEN NOT MATCHED THEN
    INSERT ("FdcId", "Name", "Description", "FdcDataType", "QualityScore", "CreatedDate", "CurationStatusId")
    VALUES (source."FdcId", source.description, source.description, source.data_type, source.quality_score, NOW(), 9000)
WHEN MATCHED THEN
    UPDATE SET 
        "QualityScore" = source.quality_score,
        "FdcDataType" = source.data_type,
        "UpdatedDate" = NOW();

-- 3. Populate the "NutrientGuideline" table from enhanced guidelines
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
FROM "Staging_Guideline" sg
JOIN nutrient."Nutrient" n ON n."Name" = sg."NutrientName"
JOIN reference."Reference" goal ON goal."Name" = sg."GoalTypeName"
JOIN reference."Reference" unit ON unit."Name" = sg."UnitName"
ON CONFLICT ("NutrientId", "GoalTypeId") DO UPDATE SET
    "RecommendedAmount" = EXCLUDED."RecommendedAmount",
    "MaxAmount" = EXCLUDED."MaxAmount",
    "UpdatedDate" = NOW();

-- 4. Populate the "IngredientNutrient" linking table with quality filtering.
INSERT INTO nutrient."IngredientNutrient" (
    "IngredientId", 
    "NutrientId", 
    "Amount", 
    "MeasurementTypeId", 
    "FdcId", 
    "QualityScore",
    "MinYearAcquired",
    "CreatedDate"
)
SELECT
    i."Id" AS "IngredientId",
    sfn.nutrient_id AS "NutrientId",
    NULLIF(sfn.amount, '')::NUMERIC,
    n."DefaultMeasurementTypeId",
    sfn.fdc_id::TEXT,
    sfn.quality_score,
    NULLIF(sfn.min_year_acquired, '')::INTEGER,
    NOW()
FROM "Staging_Food_Nutrient_Enhanced" sfn
JOIN recipe."Ingredient" i ON i."FdcId" = sfn.fdc_id::TEXT
JOIN nutrient."Nutrient" n ON n."Id" = sfn.nutrient_id
WHERE NULLIF(sfn.amount, '') IS NOT NULL
AND sfn.quality_score >= 0.5
AND (sfn.min_year_acquired IS NULL OR sfn.min_year_acquired::INTEGER >= 2010)
ON CONFLICT ("IngredientId", "NutrientId") DO UPDATE SET
    "Amount" = EXCLUDED."Amount",
    "QualityScore" = EXCLUDED."QualityScore",
    "MinYearAcquired" = EXCLUDED."MinYearAcquired",
    "UpdatedDate" = NOW();

-- 5. Create quality indexes for better performance
CREATE INDEX IF NOT EXISTS idx_ingredient_quality_score ON recipe."Ingredient" ("QualityScore");
CREATE INDEX IF NOT EXISTS idx_ingredient_fdc_data_type ON recipe."Ingredient" ("FdcDataType");
CREATE INDEX IF NOT EXISTS idx_ingredient_name_length ON recipe."Ingredient" (LENGTH("Name"));
CREATE INDEX IF NOT EXISTS idx_nutrient_quality_score ON nutrient."Nutrient" ("QualityScore");
CREATE INDEX IF NOT EXISTS idx_ingredient_nutrient_quality ON nutrient."IngredientNutrient" ("QualityScore");
CREATE INDEX IF NOT EXISTS idx_ingredient_nutrient_year ON nutrient."IngredientNutrient" ("MinYearAcquired");

-- 6. Create summary statistics for quality assessment
CREATE OR REPLACE VIEW recipe."IngredientQualitySummary" AS
SELECT 
    "FdcDataType",
    COUNT(*) as total_ingredients,
    AVG("QualityScore") as avg_quality_score,
    MIN("QualityScore") as min_quality_score,
    MAX("QualityScore") as max_quality_score,
    COUNT(CASE WHEN "QualityScore" >= 0.8 THEN 1 END) as high_quality_count,
    COUNT(CASE WHEN "QualityScore" >= 0.6 AND "QualityScore" < 0.8 THEN 1 END) as medium_quality_count,
    COUNT(CASE WHEN "QualityScore" < 0.6 THEN 1 END) as low_quality_count
FROM recipe."Ingredient"
GROUP BY "FdcDataType";

-- 7. Create materialized view for common ingredient searches
CREATE MATERIALIZED VIEW recipe."IngredientSearchView" AS
SELECT 
    i."Id",
    i."Name",
    i."Description",
    i."FdcDataType",
    i."QualityScore",
    COUNT(inr."NutrientId") as nutrient_count,
    AVG(inr."QualityScore") as avg_nutrient_quality
FROM recipe."Ingredient" i
LEFT JOIN nutrient."IngredientNutrient" inr ON i."Id" = inr."IngredientId"
WHERE i."QualityScore" >= 0.5
GROUP BY i."Id", i."Name", i."Description", i."FdcDataType", i."QualityScore";

CREATE INDEX IF NOT EXISTS idx_ingredient_search_name ON recipe."IngredientSearchView" USING gin(to_tsvector('english', "Name"));
CREATE INDEX IF NOT EXISTS idx_ingredient_search_quality ON recipe."IngredientSearchView" ("QualityScore");

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