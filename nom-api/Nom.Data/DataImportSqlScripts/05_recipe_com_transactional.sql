-- 05_recipe_com_transactional.sql
-- This script contains the transactional logic for inserting/updating recipe data
-- into the main application tables, and generates duplicate reports.

DO $$
DECLARE
    system_person_id BIGINT := 1;
BEGIN

    -- 1. Insert/Update recipe.Recipe
    -- This section assumes recipe.recipe_com_recipe_staging has already been populated
    -- and duplicates within the staging table have been handled by 04_recipe_com_temp_staging.sql.
    INSERT INTO recipe."Recipe" ("Name", "Description", "Instructions", "PrepTimeMinutes", "CookTimeMinutes", "Servings", "ServingQuantity", "ServingQuantityMeasurementTypeId", "RawIngredientsString", "IsCurated", "CuratedById", "CuratedDate", "SourceUrl", "SourceSite", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
    SELECT DISTINCT ON (TRIM(rs.recipe_name)) -- Use recipe name as primary conflict target AND de-duplicate incoming data
        TRIM(rs.recipe_name),
        NULL, -- Description not directly available in source
        TRIM(rs.raw_directions_json), -- Store raw JSON string of directions
        NULL, -- PrepTimeMinutes not directly available
        NULL, -- CookTimeMinutes not directly available
        NULL, -- Servings not directly available
        NULL, -- ServingQuantity not directly available
        NULL, -- ServingQuantityMeasurementTypeId not directly available
        TRIM(rs.raw_ingredients_json), -- Store raw JSON string of ingredients
        FALSE, -- IsCurated defaults to false for imported recipes
        NULL, -- CuratedById defaults to NULL
        NULL, -- CuratedDate defaults to NULL
        TRIM(rs.source_link),
        TRIM(rs.source_site),
        NOW() AT TIME ZONE 'UTC',
        system_person_id,
        NOW() AT TIME ZONE 'UTC',
        system_person_id
    FROM recipe.recipe_com_recipe_staging rs
    WHERE TRIM(rs.recipe_name) IS NOT NULL AND TRIM(rs.recipe_name) != ''
    ORDER BY TRIM(rs.recipe_name), rs.source_link -- Ensure deterministic selection for DISTINCT ON
    ON CONFLICT ("Name") DO UPDATE SET -- Using "Name" for ON CONFLICT, relies on IX_Recipe_Name
        "Description" = COALESCE(EXCLUDED."Description", recipe."Recipe"."Description"),
        "Instructions" = COALESCE(EXCLUDED."Instructions", recipe."Recipe"."Instructions"),
        "PrepTimeMinutes" = COALESCE(EXCLUDED."PrepTimeMinutes", recipe."Recipe"."PrepTimeMinutes"),
        "CookTimeMinutes" = COALESCE(EXCLUDED."CookTimeMinutes", recipe."Recipe"."CookTimeMinutes"),
        "Servings" = COALESCE(EXCLUDED."Servings", recipe."Recipe"."Servings"),
        "ServingQuantity" = COALESCE(EXCLUDED."ServingQuantity", recipe."Recipe"."ServingQuantity"),
        "ServingQuantityMeasurementTypeId" = COALESCE(EXCLUDED."ServingQuantityMeasurementTypeId", recipe."Recipe"."ServingQuantityMeasurementTypeId"),
        "RawIngredientsString" = COALESCE(EXCLUDED."RawIngredientsString", recipe."Recipe"."RawIngredientsString"),
        "IsCurated" = EXCLUDED."IsCurated", -- Update if imported later
        "CuratedById" = COALESCE(EXCLUDED."CuratedById", recipe."Recipe"."CuratedById"),
        "CuratedDate" = COALESCE(EXCLUDED."CuratedDate", recipe."Recipe"."CuratedDate"),
        "SourceUrl" = COALESCE(EXCLUDED."SourceUrl", recipe."Recipe"."SourceUrl"),
        "SourceSite" = COALESCE(EXCLUDED."SourceSite", recipe."Recipe"."SourceSite"), -- Explicitly use recipe."Recipe".
        "LastModifiedDate" = NOW() AT TIME ZONE 'UTC',
        "LastModifiedByPersonId" = system_person_id;
    -- Removed WHERE clause as it's redundant with ON CONFLICT ("Name")

    RAISE NOTICE 'Processed recipes into recipe.Recipe table.';

    -- Insert new ingredients into recipe.Ingredient if they don't already exist
    -- This section assumes recipe_com_ingredient_parsed_staging has been populated.
    INSERT INTO recipe."Ingredient" ("Name", "Description", "FdcId", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
    SELECT DISTINCT ON (TRIM(rips.final_ingredient_name))
        TRIM(rips.final_ingredient_name),
        'Imported from recipe.com: ' || rips.ingredient_raw_text,
        NULL, -- FdcId will be NULL initially for recipe.com ingredients
        NOW() AT TIME ZONE 'UTC',
        system_person_id,
        NOW() AT TIME ZONE 'UTC',
        system_person_id
    FROM recipe_com_ingredient_parsed_staging rips
    LEFT JOIN recipe."Ingredient" existing_ing ON TRIM(rips.final_ingredient_name) = TRIM(existing_ing."Name")
    WHERE existing_ing."Id" IS NULL AND TRIM(rips.final_ingredient_name) IS NOT NULL AND TRIM(rips.final_ingredient_name) != ''
    ORDER BY TRIM(rips.final_ingredient_name), rips.source_link -- Deterministic selection
    ON CONFLICT ("Name") DO NOTHING; -- If a concurrent process inserted it, do nothing

    RAISE NOTICE 'New ingredients added to recipe.Ingredient table.';


    -- 2. Insert into recipe.RecipeIngredient
    -- Identify and store duplicates for recipe_com_ingredient_duplicates_report_staging
    INSERT INTO recipe_com_ingredient_duplicates_report_staging (source_link, ingredient_raw_text, ner_ingredient_name, final_ingredient_name, duplicate_reason)
    SELECT
        ri.source_link,
        ri.ingredient_raw_text,
        ri.ner_ingredient_name,
        ri.final_ingredient_name,
        'Duplicate (RecipeId, IngredientId) after parsing, only one kept'
    FROM (
        SELECT
            rips.*,
            r."Id" AS recipe_id_for_dedupe,
            i."Id" AS ingredient_id_for_dedupe,
            ROW_NUMBER() OVER (PARTITION BY r."Id", i."Id" ORDER BY rips.line_order) as rn
        FROM recipe_com_ingredient_parsed_staging rips
        INNER JOIN recipe."Recipe" r ON rips.source_link = r."SourceUrl"
        INNER JOIN recipe."Ingredient" i ON TRIM(rips.final_ingredient_name) = TRIM(i."Name")
        WHERE rips.final_ingredient_name IS NOT NULL AND rips.final_ingredient_name != ''
    ) ri
    WHERE ri.rn > 1;


    INSERT INTO recipe."RecipeIngredient" ("RecipeId", "IngredientId", "Quantity", "MeasurementTypeId", "RawLine", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
    SELECT DISTINCT ON (r."Id", i."Id") -- Ensure unique (RecipeId, IngredientId) pairs
        r."Id" AS "RecipeId",
        i."Id" AS "IngredientId",
        COALESCE(rips.parsed_amount, 1), -- Use parsed amount, default to 1 if NULL
        rips.final_measurement_type_id,
        TRIM(rips.ingredient_raw_text) AS "RawLine", -- Store raw text for auditing/traceability
        NOW() AT TIME ZONE 'UTC',
        system_person_id,
        NOW() AT TIME ZONE 'UTC',
        system_person_id
    FROM recipe_com_ingredient_parsed_staging rips
    INNER JOIN recipe."Recipe" r ON rips.source_link = r."SourceUrl"
    INNER JOIN recipe."Ingredient" i ON TRIM(rips.final_ingredient_name) = TRIM(i."Name")
    WHERE rips.final_ingredient_name IS NOT NULL AND TRIM(rips.final_ingredient_name) != ''
    ORDER BY r."Id", i."Id", rips.line_order -- Ensure deterministic selection for DISTINCT ON
    ON CONFLICT ("RecipeId", "IngredientId") DO UPDATE SET
        "Quantity" = EXCLUDED."Quantity",
        "MeasurementTypeId" = EXCLUDED."MeasurementTypeId",
        "RawLine" = EXCLUDED."RawLine",
        "LastModifiedDate" = NOW() AT TIME ZONE 'UTC',
        "LastModifiedByPersonId" = system_person_id;

    RAISE NOTICE 'Processed recipe ingredients into recipe.RecipeIngredient table.';


    -- 3. Insert into recipe.RecipeStep
    INSERT INTO recipe."RecipeStep" ("RecipeId", "StepNumber", "Summary", "Description", "StepTypeId", "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId")
    SELECT DISTINCT ON (r."Id", rips.instruction_step_number)
        r."Id",
        rips.instruction_step_number,
        TRIM(rips.summary_text), -- New Summary field
        TRIM(rips.instruction_text), -- Instruction text maps to Description
        NULL, -- StepTypeId is NULL as it's not in the source CSV
        NOW() AT TIME ZONE 'UTC',
        system_person_id,
        NOW() AT TIME ZONE 'UTC',
        system_person_id
    FROM recipe_com_instruction_parsed_staging rips
    INNER JOIN recipe."Recipe" r ON rips.source_link = r."SourceUrl"
    WHERE TRIM(rips.instruction_text) IS NOT NULL AND TRIM(rips.instruction_text) != ''
    ORDER BY r."Id", rips.instruction_step_number
    ON CONFLICT ("RecipeId", "StepNumber") DO UPDATE SET
        "Summary" = EXCLUDED."Summary", -- Update Summary
        "Description" = EXCLUDED."Description", -- Update Description
        "StepTypeId" = COALESCE(EXCLUDED."StepTypeId", recipe."RecipeStep"."StepTypeId"), -- Explicitly use recipe."RecipeStep"
        "LastModifiedDate" = NOW() AT TIME ZONE 'UTC',
        "LastModifiedByPersonId" = system_person_id;

    -- Capture duplicates for recipe_com_instruction_duplicates_report_staging
    INSERT INTO recipe_com_instruction_duplicates_report_staging (source_link, instruction_step_number, instruction_text, duplicate_reason)
    SELECT
        rde.source_link,
        rde.instruction_step_number,
        rde.instruction_text,
        'Duplicate (RecipeId, StepNumber) in source, only one kept'
    FROM (
        SELECT
            rips.*,
            r."Id" AS recipe_id_for_dedupe,
            ROW_NUMBER() OVER (PARTITION BY r."Id", rips.instruction_step_number ORDER BY rips.source_link) as rn_conflict -- For identifying conflicts on RecipeId, StepNumber
        FROM recipe_com_instruction_parsed_staging rips
        INNER JOIN recipe."Recipe" r ON rips.source_link = r."SourceUrl"
        WHERE rips.instruction_text IS NOT NULL AND rips.instruction_text != ''
    ) rde
    WHERE rde.rn_conflict > 1;

    RAISE NOTICE 'Processed recipe instructions into recipe.RecipeStep table.';


EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'CRITICAL ERROR in 05_recipe_com_transactional.sql: %', SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        RAISE NOTICE '--- Max Length Report for recipe.recipe_com_recipe_staging (from prior stage) ---';
        RAISE NOTICE 'source_link max length: %', (SELECT MAX(LENGTH(source_link)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'recipe_name max length: %', (SELECT MAX(LENGTH(recipe_name)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'source_site max length: %', (SELECT MAX(LENGTH(source_site)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'raw_ingredients_json max length: %', (SELECT MAX(LENGTH(raw_ingredients_json)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'raw_directions_json max length: %', (SELECT MAX(LENGTH(raw_directions_json)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE '--- Max Length Report for recipe_com_ingredient_parsed_staging ---';
        RAISE NOTICE 'source_link max length: %', (SELECT MAX(LENGTH(source_link)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'ingredient_raw_text max length: %', (SELECT MAX(LENGTH(ingredient_raw_text)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'ner_ingredient_name max length: %', (SELECT MAX(LENGTH(ner_ingredient_name)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'final_ingredient_name max length: %', (SELECT MAX(LENGTH(final_ingredient_name)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'parsed_unit_name max length: %', (SELECT MAX(LENGTH(parsed_unit_name)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE '--- Max Length Report for recipe_com_instruction_parsed_staging ---';
        RAISE NOTICE 'source_link max length: %', (SELECT MAX(LENGTH(source_link)) FROM recipe_com_instruction_parsed_staging);
        RAISE NOTICE 'instruction_text max length: %', (SELECT MAX(LENGTH(instruction_text)) FROM recipe_com_instruction_parsed_staging);
        RAISE NOTICE 'summary_text max length: %', (SELECT MAX(LENGTH(summary_text)) FROM recipe_com_instruction_parsed_staging);
        RAISE NOTICE 'A duplicate report for recipes might have been generated (recipe_com_recipe_duplicates_report.csv).';
        RAISE; -- Re-raise the original error
END $$;

-- Clean up temporary tables (only here, as 04_ handles its own drops)
-- Drop dependent tables first, then the tables they depend on
DROP TABLE IF EXISTS recipe_com_ingredient_parsed_staging CASCADE;
DROP TABLE IF EXISTS recipe_com_instruction_parsed_staging CASCADE;
DROP TABLE IF EXISTS recipe.recipe_com_raw_staging CASCADE; -- Schema-qualified
DROP TABLE IF EXISTS recipe.recipe_com_recipe_staging CASCADE; -- Schema-qualified


-- Export duplicate reports
-- The path will be substituted by the calling shell script
\copy recipe_com_recipe_duplicates_report_staging TO '/path/to/your/downloaded/files/recipe_com_recipe_duplicates_report.csv' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');
DROP TABLE IF EXISTS recipe_com_recipe_duplicates_report_staging;

\copy recipe_com_ingredient_duplicates_report_staging TO '/path/to/your/downloaded/files/recipe_com_ingredient_duplicates_report.csv' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');
DROP TABLE IF EXISTS recipe_com_ingredient_duplicates_report_staging;

\copy recipe_com_instruction_duplicates_report_staging TO '/path/to/your/downloaded/files/recipe_com_instruction_duplicates_report.csv' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');
DROP TABLE IF EXISTS recipe_com_instruction_duplicates_report_staging;
