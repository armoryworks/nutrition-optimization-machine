-- 04_recipe_com_raw_to_temp.sql
-- This script processes data from recipe.recipe_com_raw_staging.
-- It performs the following actions:
-- 1. Populates recipe.recipe_com_recipe_staging with recipe metadata (title, link, source).
-- 2. Explodes the 'ingredients' JSON text into individual lines and inserts them into recipe.recipe_com_raw_ingredients_exploded_staging.
-- 3. Explodes the 'directions' JSON text into individual steps and inserts them into recipe.recipe_com_raw_instructions_exploded_staging.
-- This script is designed to be idempotent and handles batches.

SET client_min_messages TO NOTICE;
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

BEGIN;

DO $$
DECLARE
    system_person_id BIGINT := 1;
    processed_recipes_count INT := 0;
    processed_ingredients_count INT := 0;
    processed_instructions_count INT := 0;
    -- These placeholders will be replaced by the shell script before execution
    current_offset INT := __OFFSET_PLACEHOLDER__;
    current_limit INT := __LIMIT_PLACEHOLDER__;
BEGIN
    RAISE NOTICE '--- Starting 04_recipe_com_raw_to_temp.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;

    -- Acquire a session-level advisory lock for this process
    PERFORM pg_advisory_lock(530919875::BIGINT); -- Distinct lock ID for this script

    -- Step 1: Populate recipe.recipe_com_recipe_staging with recipe metadata
    RAISE NOTICE 'Step 1: Populating recipe.recipe_com_recipe_staging...';
    WITH raw_batch AS (
        SELECT
            id,
            title,
            link,
            source
        FROM recipe.recipe_com_raw_staging
        ORDER BY id -- Ensure consistent batching
        OFFSET current_offset
        LIMIT current_limit
        FOR UPDATE SKIP LOCKED -- Use SKIP LOCKED to allow concurrent runs if needed
    )
    INSERT INTO recipe.recipe_com_recipe_staging (title, link, source)
    SELECT
        TRIM(rb.title),
        TRIM(rb.link),
        TRIM(rb.source)
    FROM raw_batch rb
    WHERE TRIM(rb.link) IS NOT NULL AND TRIM(rb.link) != ''
    ON CONFLICT (link) DO NOTHING; -- Avoid inserting duplicates based on unique link
    GET DIAGNOSTICS processed_recipes_count = ROW_COUNT;
    RAISE NOTICE 'Inserted/skipped % recipe records into recipe.recipe_com_recipe_staging.', processed_recipes_count;

    -- Step 2: Explode 'ingredients' JSON into recipe.recipe_com_raw_ingredients_exploded_staging
    RAISE NOTICE 'Step 2: Exploding ingredients JSON into recipe.recipe_com_raw_ingredients_exploded_staging...';
    WITH raw_batch AS (
        SELECT
            id,
            ingredients,
            link AS source_link
        FROM recipe.recipe_com_raw_staging
        WHERE ingredients IS NOT NULL AND TRIM(ingredients) != ''
        ORDER BY id
        OFFSET current_offset
        LIMIT current_limit
        FOR UPDATE SKIP LOCKED
    )
    INSERT INTO recipe.recipe_com_raw_ingredients_exploded_staging (source_link, line_order, ingredient_line)
    SELECT
        rb.source_link,
        (idx - 1) AS line_order, -- Array index is 1-based, convert to 0-based
        TRIM(ingredient_text.value)
    FROM raw_batch rb,
        -- Sanitize null characters before casting to jsonb
        jsonb_array_elements_text(REPLACE(rb.ingredients, '\u0000', '')::jsonb) WITH ORDINALITY AS ingredient_text(value, idx)
    WHERE TRIM(ingredient_text.value) IS NOT NULL AND TRIM(ingredient_text.value) != '' -- Exclude empty lines
    ON CONFLICT (source_link, line_order) DO NOTHING; -- Avoid inserting duplicates (requires unique constraint on source_link, line_order)
    GET DIAGNOSTICS processed_ingredients_count = ROW_COUNT;
    RAISE NOTICE 'Inserted/skipped % ingredient lines into recipe.recipe_com_raw_ingredients_exploded_staging.', processed_ingredients_count;

    -- Step 3: Explode 'directions' JSON into recipe.recipe_com_raw_instructions_exploded_staging
    RAISE NOTICE 'Step 3: Exploding directions JSON into recipe.recipe_com_raw_instructions_exploded_staging...';
    WITH raw_batch AS (
        SELECT
            id,
            directions,
            link AS source_link
        FROM recipe.recipe_com_raw_staging
        WHERE directions IS NOT NULL AND TRIM(directions) != ''
        ORDER BY id
        OFFSET current_offset
        LIMIT current_limit
        FOR UPDATE SKIP LOCKED
    )
    INSERT INTO recipe.recipe_com_raw_instructions_exploded_staging (source_link, instruction_step_number, instruction_text)
    SELECT
        rb.source_link,
        (idx - 1) AS instruction_step_number, -- Array index is 1-based, convert to 0-based
        TRIM(instruction_text.value)
    FROM raw_batch rb,
        -- Sanitize null characters before casting to jsonb
        jsonb_array_elements_text(REPLACE(rb.directions, '\u0000', '')::jsonb) WITH ORDINALITY AS instruction_text(value, idx)
    WHERE TRIM(instruction_text.value) IS NOT NULL AND TRIM(instruction_text.value) != '' -- Exclude empty lines
    ON CONFLICT (source_link, instruction_step_number) DO NOTHING; -- Avoid inserting duplicates (requires unique constraint on source_link, instruction_step_number)
    GET DIAGNOSTICS processed_instructions_count = ROW_COUNT;
    RAISE NOTICE 'Inserted/skipped % instruction steps into recipe.recipe_com_raw_instructions_exploded_staging.', processed_instructions_count;

    PERFORM pg_advisory_unlock(530919875::BIGINT);

    RAISE NOTICE '--- 04_recipe_com_raw_to_temp.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'ERROR in 04_recipe_com_raw_to_temp.sql (Batch Offset: %): %', current_offset, SQLERRM;
        PERFORM pg_advisory_unlock(530919875::BIGINT);
        RAISE;
END $$;

COMMIT;
