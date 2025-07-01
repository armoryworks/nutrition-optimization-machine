-- 01_fdc_setup_and_nutrients.sql
-- This script sets up staging tables for FDC nutrient data,
-- loads the FDC nutrient.csv, and inserts/updates the nutrient.Nutrient table.

-- 1. Load data from FDC nutrient.csv into the staging table
-- IMPORTANT: This \copy command MUST be outside any DO $$ BEGIN ... END $$; block.
-- The path should NOT be enclosed in single quotes here. The bash script will substitute the placeholder.
-- Explicitly listing columns to match your exact nutrient.csv header.
\copy fdc_nutrient_staging (id, name, unit_name, nutrient_nbr, rank) FROM /path/to/your/downloaded/files/nutrient.csv WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');

-- Set a variable for the system person ID (assuming it's seeded as 1L)
-- This is used for CreatedByPersonId in audit fields.
DO $$
DECLARE
    system_person_id BIGINT := 1;
    measurement_type_g_id BIGINT;
    measurement_type_mg_id BIGINT;
    measurement_type_mcg_id BIGINT; -- Covers both 'µg' and 'mcg'
    measurement_type_kcal_id BIGINT;
    measurement_type_unknown_id BIGINT;
BEGIN

    -- Fetch MeasurementType IDs dynamically from your reference.Reference table
    SELECT "Id" INTO measurement_type_g_id FROM reference."Reference" WHERE "Name" = 'g' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_mg_id FROM reference."Reference" WHERE "Name" = 'mg' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_mcg_id FROM reference."Reference" WHERE "Name" = 'mcg' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_kcal_id FROM reference."Reference" WHERE "Name" = 'kcal' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO measurement_type_unknown_id FROM reference."Reference" WHERE "Name" = 'unknown' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));

    -- Log count of loaded items (this might not show if executed with \copy in a script)
    RAISE NOTICE 'Loaded % rows into fdc_nutrient_staging.', (SELECT COUNT(*) FROM fdc_nutrient_staging);

    -- Identify and store duplicates within fdc_nutrient_staging based on 'name'
    INSERT INTO fdc_nutrient_duplicates_report_staging (id, name, unit_name, nutrient_nbr, rank, duplicate_reason)
    SELECT
        n.id,
        n.name,
        n.unit_name,
        n.nutrient_nbr,
        n.rank,
        'Duplicate name in source CSV'
    FROM (
        SELECT
            *,
            ROW_NUMBER() OVER (PARTITION BY TRIM(name) ORDER BY id) as rn
        FROM fdc_nutrient_staging
    ) n
    WHERE n.rn > 1;

    -- 3. Insert or update nutrient.Nutrient table
    -- Deduplicate by 'Name'. Use FDC's 'id' as 'FdcId'.
    -- Use DISTINCT ON to handle duplicate names in the source CSV, picking one arbitrarily (e.g., min(id))
    INSERT INTO nutrient."Nutrient" ("Name", "Description", "DefaultMeasurementTypeId", "FdcId", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
    SELECT DISTINCT ON (TRIM(n.name)) -- Ensure only one row per unique name
        TRIM(n.name),
        NULL, -- 'footnote' is not in your nutrient.csv, so it will be NULL
        CASE
            WHEN LOWER(TRIM(n.unit_name)) = 'g' THEN measurement_type_g_id
            WHEN LOWER(TRIM(n.unit_name)) = 'mg' THEN measurement_type_mg_id
            WHEN LOWER(TRIM(n.unit_name)) = 'µg' THEN measurement_type_mcg_id
            WHEN LOWER(TRIM(n.unit_name)) = 'mcg' THEN measurement_type_mcg_id
            WHEN LOWER(TRIM(n.unit_name)) = 'kcal' THEN measurement_type_kcal_id
            ELSE measurement_type_unknown_id -- Default to unknown if not mapped
        END,
        TRIM(n.id), -- Using 'id' from nutrient.csv as FdcId
        NOW() AT TIME ZONE 'UTC',
        system_person_id,
        NOW() AT TIME ZONE 'UTC',
        system_person_id
    FROM fdc_nutrient_staging n
    ORDER BY TRIM(n.name), n.id -- Order to ensure consistent selection for DISTINCT ON
    ON CONFLICT ("Name") DO UPDATE SET -- Update if name conflicts (case-insensitive due to EF Core query)
        "Description" = COALESCE(EXCLUDED."Description", nutrient."Nutrient"."Description"),
        "FdcId" = COALESCE(EXCLUDED."FdcId", nutrient."Nutrient"."FdcId"),
        "LastModifiedDate" = NOW() AT TIME ZONE 'UTC',
        "LastModifiedByPersonId" = system_person_id
    WHERE
        nutrient."Nutrient"."FdcId" IS NULL OR nutrient."Nutrient"."Description" IS NULL; -- Only update if existing is less complete

    RAISE NOTICE 'Processed nutrients into nutrient.Nutrient table.';

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'CRITICAL ERROR in 01_fdc_setup_and_nutrients.sql: %', SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        RAISE NOTICE '--- Max Length Report for fdc_nutrient_staging ---';
        RAISE NOTICE 'id max length: %', (SELECT MAX(LENGTH(id)) FROM fdc_nutrient_staging);
        RAISE NOTICE 'name max length: %', (SELECT MAX(LENGTH(name)) FROM fdc_nutrient_staging);
        RAISE NOTICE 'unit_name max length: %', (SELECT MAX(LENGTH(unit_name)) FROM fdc_nutrient_staging);
        RAISE NOTICE 'nutrient_nbr max length: %', (SELECT MAX(LENGTH(nutrient_nbr)) FROM fdc_nutrient_staging);
        RAISE NOTICE 'rank max length: %', (SELECT MAX(LENGTH(rank)) FROM fdc_nutrient_staging);
        RAISE NOTICE 'A duplicate report for nutrients might have been generated as fdc_nutrient_duplicates_report.csv.';
        RAISE; -- Re-raise the original error
END $$;

-- Clean up the temporary table
DROP TABLE IF EXISTS fdc_nutrient_staging;

-- Export duplicate report for nutrients
-- The path will be substituted by the calling shell script
\copy fdc_nutrient_duplicates_report_staging TO '/path/to/your/downloaded/files/fdc_nutrient_duplicates_report.csv' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');
DROP TABLE IF EXISTS fdc_nutrient_duplicates_report_staging;
