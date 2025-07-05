-- 02_fdc_ingredients.sql
-- This script sets up a staging table for FDC food data,
-- loads food.csv, inserts/updates recipe.Ingredient,
-- and populates recipe.IngredientAlias.

-- 1. Load data from FDC food.csv into the staging table
-- IMPORTANT: This \copy command MUST be outside any DO $$ BEGIN ... END $$; block.
-- The path should NOT be enclosed in single quotes here. The bash script will substitute the placeholder.
-- Explicitly listing columns to match your exact food.csv header.
\copy fdc_food_staging (fdc_id, data_type, description, food_category_id, publication_date) FROM /path/to/your/downloaded/files/food.csv WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');

-- Set a variable for the system person ID
DO $$
DECLARE
    system_person_id BIGINT := 1;
BEGIN

    RAISE NOTICE 'Loaded % rows into fdc_food_staging.', (SELECT COUNT(*) FROM fdc_food_staging);

    -- Identify and store duplicates within fdc_food_staging based on 'description'
    INSERT INTO fdc_food_duplicates_report_staging (fdc_id, data_type, description, food_category_id, publication_date, duplicate_reason)
    SELECT
        f.fdc_id,
        f.data_type,
        f.description,
        f.food_category_id,
        f.publication_date,
        'Duplicate description in source CSV'
    FROM (
        SELECT
            *,
            ROW_NUMBER() OVER (PARTITION BY TRIM(description) ORDER BY fdc_id) as rn
        FROM fdc_food_staging
        WHERE TRIM(description) IS NOT NULL AND TRIM(description) != ''
    ) f
    WHERE f.rn > 1;

    -- MERGE recipe.Ingredient table
    -- This uses the FDC food.csv data to insert new ingredients or update existing ones.
    -- The MERGE statement handles deduplication from the source (DISTINCT ON)
    -- and applies the specific update conditions (FdcId IS NULL OR Description IS NULL).
    MERGE INTO recipe."Ingredient" AS target
    USING (
        SELECT DISTINCT ON (TRIM(f.description)) -- Ensure only one row per unique description from source
            TRIM(f.description) AS description_trimmed,
            TRIM(f.fdc_id) AS fdc_id_source
        FROM fdc_food_staging f
        WHERE TRIM(f.description) IS NOT NULL AND TRIM(f.description) != ''
        ORDER BY TRIM(f.description), f.fdc_id -- Order to ensure consistent selection for DISTINCT ON
    ) AS source
    ON LOWER(target."Name") = LOWER(source.description_trimmed) -- Match on lower-cased name
    WHEN MATCHED AND (target."FdcId" IS NULL OR target."Description" IS NULL) THEN -- Only update if existing is less complete
        UPDATE SET
            "Description" = COALESCE(target."Description", NULL), -- No scientific_name from current CSV
            "FdcId" = COALESCE(target."FdcId", source.fdc_id_source), -- Fill FdcId if currently NULL
            "LastModifiedDate" = NOW() AT TIME ZONE 'UTC',
            "LastModifiedByPersonId" = system_person_id
    WHEN NOT MATCHED THEN
        INSERT ("Name", "Description", "FdcId", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
        VALUES (
            source.description_trimmed,
            NULL, -- No scientific_name from current CSV
            source.fdc_id_source,
            NOW() AT TIME ZONE 'UTC',
            system_person_id,
            NOW() AT TIME ZONE 'UTC',
            system_person_id
        );

    RAISE NOTICE 'Processed ingredients into recipe.Ingredient table using MERGE.';

    -- 4. Populate recipe.IngredientAlias table
    -- NOTE: Your food.csv does not contain 'branded_food_category' or 'scientific_name'.
    -- Therefore, this section will not insert any aliases based on those fields.
    -- If you have other FDC files (e.g., branded_food.csv) that contain these,
    -- you would need a separate import process for those.
    RAISE NOTICE 'Skipping branded food categories and scientific names as aliases, as these columns are not present in your food.csv.';

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'CRITICAL ERROR in 02_fdc_ingredients.sql: %', SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        RAISE NOTICE '--- Max Length Report for fdc_food_staging ---';
        RAISE NOTICE 'fdc_id max length: %', (SELECT MAX(LENGTH(fdc_id)) FROM fdc_food_staging);
        RAISE NOTICE 'data_type max length: %', (SELECT MAX(LENGTH(data_type)) FROM fdc_food_staging);
        RAISE NOTICE 'description max length: %', (SELECT MAX(LENGTH(description)) FROM fdc_food_staging);
        RAISE NOTICE 'food_category_id max length: %', (SELECT MAX(LENGTH(food_category_id)) FROM fdc_food_staging);
        RAISE NOTICE 'publication_date max length: %', (SELECT MAX(LENGTH(publication_date)) FROM fdc_food_staging);
        RAISE NOTICE 'A duplicate report for foods might have been generated as fdc_food_duplicates_report.csv.';
        RAISE; -- Re-raise the original error
END $$;

-- Clean up the temporary table
DROP TABLE IF EXISTS fdc_food_staging;

-- Export duplicate report for foods
-- The path will be substituted by the calling shell script
\copy fdc_food_duplicates_report_staging TO '/path/to/your/downloaded/files/fdc_food_duplicates_report.csv' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');
DROP TABLE IF EXISTS fdc_food_duplicates_report_staging;
