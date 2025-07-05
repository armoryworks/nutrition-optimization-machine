-- 03_fdc_ingredient_nutrients.sql
-- This script sets up a staging table for FDC food_nutrient data,
-- loads food_nutrient.csv, and inserts into nutrient.IngredientNutrient.

-- 1. Load data from FDC food_nutrient.csv into the staging table
-- IMPORTANT: This \copy command MUST be outside any DO $$ BEGIN ... END $$; block.
-- The path should NOT be enclosed in single quotes here. The bash script will substitute the placeholder.
-- Explicitly listing columns to match your exact food_nutrient.csv header.
\copy fdc_food_nutrient_staging (id, fdc_id, nutrient_id, amount, data_points, derivation_id, min, max, median, loq, footnote, min_year_acquired, percent_daily_value) FROM /path/to/your/downloaded/files/food_nutrient.csv WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');

-- Set a variable for the system person ID
DO $$
DECLARE
    system_person_id BIGINT := 1;
    -- These measurement type IDs are fetched but not directly used in the MERGE statement's INSERT/UPDATE
    -- because the "MeasurementTypeId" is sourced directly from n."DefaultMeasurementTypeId" (the Nutrient's default unit).
    measurement_type_g_id BIGINT;
    measurement_type_mg_id BIGINT;
    measurement_type_mcg_id BIGINT;
    measurement_type_kcal_id BIGINT;
    measurement_type_unknown_id BIGINT;
BEGIN
    -- Fetch MeasurementType IDs dynamically (kept for consistency with other scripts, though not directly used in MERGE's final values)
    SELECT "Id" INTO measurement_type_g_id FROM reference."Reference" WHERE "Name" = 'g' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_mg_id FROM reference."Reference" WHERE "Name" = 'mg' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_mcg_id FROM reference."Reference" WHERE "Name" = 'mcg' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_kcal_id FROM reference."Reference" WHERE "Name" = 'kcal' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_unknown_id FROM reference."Reference" WHERE "Name" = 'unknown' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));


    RAISE NOTICE 'Loaded % rows into fdc_food_nutrient_staging.', (SELECT COUNT(*) FROM fdc_food_nutrient_staging);

    -- Identify and store duplicates within fdc_food_nutrient_staging based on (fdc_id, nutrient_id)
    INSERT INTO fdc_food_nutrient_duplicates_report_staging (id, fdc_id, nutrient_id, amount, data_points, derivation_id, min, max, median, loq, footnote, min_year_acquired, percent_daily_value, duplicate_reason)
    SELECT
        fns.id,
        fns.fdc_id,
        fns.nutrient_id,
        fns.amount,
        fns.data_points,
        fns.derivation_id,
        fns.min,
        fns.max,
        fns.median,
        fns.loq,
        fns.footnote,
        fns.min_year_acquired,
        fns.percent_daily_value,
        'Duplicate (fdc_id, nutrient_id) in source CSV'
    FROM (
        SELECT
            *,
            ROW_NUMBER() OVER (PARTITION BY TRIM(fdc_id), TRIM(nutrient_id) ORDER BY id) as rn
        FROM fdc_food_nutrient_staging
    ) fns
    WHERE fns.rn > 1;

    -- MERGE nutrient.IngredientNutrient table
    -- This uses the FDC food_nutrient.csv data to insert new ingredient-nutrient associations
    -- or update existing ones. The MERGE statement carefully constructs its source
    -- to match the original JOINs, filters, and DISTINCT ON logic.
    MERGE INTO nutrient."IngredientNutrient" AS target
    USING (
        SELECT DISTINCT ON (i."Id", n."Id") -- Ensure only one row per unique (IngredientId, NutrientId) pair from source
            i."Id" AS ingredient_id,
            n."Id" AS nutrient_id,
            NULLIF(TRIM(fns.amount), '')::DECIMAL(18,4) AS amount_source,
            n."DefaultMeasurementTypeId" AS measurement_type_id_source, -- Get MeasurementTypeId from the Nutrient table
            TRIM(fns.id) AS fdc_nutrient_trace_id_source -- Store the FDC food_nutrient ID for traceability
        FROM fdc_food_nutrient_staging fns
        INNER JOIN recipe."Ingredient" i ON TRIM(fns.fdc_id) = i."FdcId" -- Match FDC Food ID to Ingredient FdcId
        INNER JOIN nutrient."Nutrient" n ON TRIM(fns.nutrient_id) = n."FdcId" -- Match FDC Nutrient ID to Nutrient FdcId
        WHERE
            NULLIF(TRIM(fns.amount), '')::DECIMAL(18,4) IS NOT NULL AND
            NULLIF(TRIM(fns.amount), '')::DECIMAL(18,4) >= 0 AND
            i."Id" IS NOT NULL AND n."Id" IS NOT NULL -- Ensure both ingredient and nutrient exist
        ORDER BY i."Id", n."Id", fns.id -- Order to ensure consistent selection for DISTINCT ON (e.g., pick by fns.id)
    ) AS source
    ON target."IngredientId" = source.ingredient_id AND target."NutrientId" = source.nutrient_id
    WHEN MATCHED THEN
        UPDATE SET
            "Amount" = source.amount_source, -- Use the new amount from the CSV
            "MeasurementTypeId" = source.measurement_type_id_source,
            "FdcId" = source.fdc_nutrient_trace_id_source, -- Update traceability ID
            "LastModifiedDate" = NOW() AT TIME ZONE 'UTC',
            "LastModifiedByPersonId" = system_person_id
    WHEN NOT MATCHED THEN
        INSERT ("IngredientId", "NutrientId", "Amount", "MeasurementTypeId", "FdcId", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
        VALUES (
            source.ingredient_id,
            source.nutrient_id,
            source.amount_source,
            source.measurement_type_id_source,
            source.fdc_nutrient_trace_id_source,
            NOW() AT TIME ZONE 'UTC',
            system_person_id,
            NOW() AT TIME ZONE 'UTC',
            system_person_id
        );

    RAISE NOTICE 'Processed food nutrient associations into nutrient.IngredientNutrient table using MERGE.';

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'CRITICAL ERROR in 03_fdc_ingredient_nutrients.sql: %', SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        RAISE NOTICE '--- Max Length Report for fdc_food_nutrient_staging ---';
        RAISE NOTICE 'id max length: %', (SELECT MAX(LENGTH(id)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'fdc_id max length: %', (SELECT MAX(LENGTH(fdc_id)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'nutrient_id max length: %', (SELECT MAX(LENGTH(nutrient_id)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'amount max length: %', (SELECT MAX(LENGTH(amount)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'data_points max length: %', (SELECT MAX(LENGTH(data_points)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'derivation_id max length: %', (SELECT MAX(LENGTH(derivation_id)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'min max length: %', (SELECT MAX(LENGTH(min)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'max max length: %', (SELECT MAX(LENGTH(max)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'median max length: %', (SELECT MAX(LENGTH(median)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'loq max length: %', (SELECT MAX(LENGTH(loq)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'footnote max length: %', (SELECT MAX(LENGTH(footnote)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'min_year_acquired max length: %', (SELECT MAX(LENGTH(min_year_acquired)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'percent_daily_value max length: %', (SELECT MAX(LENGTH(percent_daily_value)) FROM fdc_food_nutrient_staging);
        RAISE NOTICE 'A duplicate report for food nutrients might have been generated as fdc_food_nutrient_duplicates_report.csv.';
        RAISE; -- Re-raise the original error
END $$;

-- Clean up the temporary table
DROP TABLE IF EXISTS fdc_food_nutrient_staging;

-- Export duplicate report for food nutrients
-- The path will be substituted by the calling shell script
\copy fdc_food_nutrient_duplicates_report_staging TO '/path/to/your/downloaded/files/fdc_food_nutrient_duplicates_report.csv' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');
DROP TABLE IF EXISTS fdc_food_nutrient_duplicates_report_staging;
