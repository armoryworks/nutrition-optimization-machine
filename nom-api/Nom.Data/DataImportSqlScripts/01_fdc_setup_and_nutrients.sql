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

    -- MERGE nutrient.Nutrient table
    -- This uses the FDC nutrient.csv data to insert new nutrients or update existing ones.
    -- The MERGE statement now handles the deduplication from the source (DISTINCT ON)
    -- and applies the specific update conditions (FdcId IS NULL OR Description IS NULL).
    MERGE INTO nutrient."Nutrient" AS target
    USING (
        SELECT DISTINCT ON (TRIM(n.name)) -- Ensure only one row per unique name from source
            TRIM(n.name) AS name_trimmed,
            TRIM(n.id) AS fdc_id_source,
            CASE
                WHEN LOWER(TRIM(n.unit_name)) = 'g' THEN measurement_type_g_id
                WHEN LOWER(TRIM(n.unit_name)) = 'mg' THEN measurement_type_mg_id
                WHEN LOWER(TRIM(n.unit_name)) = 'µg' THEN measurement_type_mcg_id
                WHEN LOWER(TRIM(n.unit_name)) = 'mcg' THEN measurement_type_mcg_id
                WHEN LOWER(TRIM(n.unit_name)) = 'kcal' THEN measurement_type_kcal_id
                ELSE measurement_type_unknown_id -- Default to unknown if not mapped
            END AS default_measurement_type_id_source
        FROM fdc_nutrient_staging n
        ORDER BY TRIM(n.name), n.id -- Order to ensure consistent selection for DISTINCT ON
    ) AS source
    ON LOWER(target."Name") = LOWER(source.name_trimmed) -- Match on lower-cased name
    WHEN MATCHED AND (target."FdcId" IS NULL OR target."Description" IS NULL) THEN -- Only update if existing is less complete
        UPDATE SET
            "Description" = COALESCE(target."Description", NULL), -- No description from current CSV
            "FdcId" = COALESCE(target."FdcId", source.fdc_id_source), -- Fill FdcId if currently NULL
            "LastModifiedDate" = NOW() AT TIME ZONE 'UTC',
            "LastModifiedByPersonId" = system_person_id
    WHEN NOT MATCHED THEN
        INSERT ("Name", "Description", "DefaultMeasurementTypeId", "FdcId", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
        VALUES (
            source.name_trimmed,
            NULL, -- No description from current CSV
            source.default_measurement_type_id_source,
            source.fdc_id_source,
            NOW() AT TIME ZONE 'UTC',
            system_person_id,
            NOW() AT TIME ZONE 'UTC',
            system_person_id
        );

    RAISE NOTICE 'Processed nutrients into nutrient.Nutrient table using MERGE.';

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
