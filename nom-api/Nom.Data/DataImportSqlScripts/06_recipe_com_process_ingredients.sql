-- 06_recipe_com_process_ingredients.sql
-- This script processes a single batch of raw ingredient data from
-- recipe.recipe_com_raw_ingredients_exploded_staging, and inserts it into
-- the recipe."RecipeIngredient" table.
-- It expects _offset and _limit variables to be passed by the calling script.
-- This script runs as a single, atomic transaction, with PL/pgSQL logic inside a DO block.

-- Set client_min_messages to WARNING to avoid excessive output from notices if not needed
SET client_min_messages TO WARNING;
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

-- Set custom session variables using psql's -v variables.
-- These are substituted by psql before the script is sent to the server.
SET nom.current_offset = :_offset;
SET nom.current_limit = :_limit;

-- Start a transaction for this single batch. This BEGIN/COMMIT wraps the entire script execution.
BEGIN;

-- The PL/pgSQL anonymous code block starts here
DO $$
DECLARE
    system_person_id BIGINT := 1;
    processed_count INT := 0;
    -- Retrieve values from custom session variables within PL/pgSQL
    current_offset INT := current_setting('nom.current_offset')::INT;
    current_limit INT := current_setting('nom.current_limit')::INT;

    -- Directly use hardcoded IDs from ReferenceDiscriminatorEnum and CustomMigration
    -- These are assumed to be pre-existing and managed by EF Core migrations.
    unknown_measurement_type_id BIGINT := 4000; -- From CustomMigration.MeasurementTypeUnknownId
    measurement_type_group_id BIGINT := 2;      -- From ReferenceDiscriminatorEnum.MeasurementType
BEGIN
    RAISE NOTICE '--- Starting 06_recipe_com_process_ingredients.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;

    -- Acquire a session-level advisory lock for this process to prevent concurrency issues.
    -- This lock will persist until explicitly unlocked or the session ends.
    PERFORM pg_advisory_lock(987654321::BIGINT);

    -- Reference data (MeasurementType group, Unknown reference, and their link)
    -- are now assumed to be pre-populated by EF Core migrations via CustomMigration.cs.
    -- No MERGE/INSERT/UPDATE logic is needed here for these fixed reference data points.

    -- Select a batch of ingredient lines that have not yet been processed
    -- The _offset and _limit are applied here
    -- Join with recipe."Recipe" to ensure we only process ingredients for recipes already processed
    -- and order by source_link and line_order for consistent pagination.
    WITH unprocessed_ingredient_lines AS (
        SELECT
            rie.source_link,
            rie.raw_ingredient_text,
            rie.line_order,
            r."Id" AS recipe_id -- Get recipe_id here for direct use
        FROM recipe.recipe_com_raw_ingredients_exploded_staging rie
        JOIN recipe."Recipe" r ON rie.source_link = r."SourceUrl"
        WHERE NOT EXISTS (
            SELECT 1
            FROM recipe."RecipeIngredient" ri
            WHERE ri."RecipeId" = r."Id" AND ri."RawLine" = rie.raw_ingredient_text
        )
        ORDER BY rie.source_link, rie.line_order -- Crucial for consistent OFFSET/LIMIT
        OFFSET current_offset
        LIMIT current_limit
    )
    -- Insert distinct ingredient names into Ingredient table if they don't exist
    -- This relies on recipe."Ingredient"."Name" being unique (either by schema or application logic)
    INSERT INTO recipe."Ingredient" (
        "Name", "Description", "CreatedByPersonId", "CreatedDate", "LastModifiedByPersonId", "LastModifiedDate"
    )
    SELECT
        uil.raw_ingredient_text, uil.raw_ingredient_text, system_person_id, NOW(), system_person_id, NOW()
    FROM unprocessed_ingredient_lines uil
    WHERE NOT EXISTS (
        SELECT 1 FROM recipe."Ingredient" i WHERE i."Name" = uil.raw_ingredient_text
    )
    GROUP BY uil.raw_ingredient_text; -- Group by to handle duplicates within the current batch itself

    -- Insert RecipeIngredient records
    INSERT INTO recipe."RecipeIngredient" (
        "RecipeId",
        "IngredientId",
        "Quantity",
        "MeasurementTypeId",
        "RawLine",
        "CreatedByPersonId",
        "CreatedDate",
        "LastModifiedByPersonId",
        "LastModifiedDate"
    )
    SELECT
        uil.recipe_id,
        (SELECT "Id" FROM recipe."Ingredient" WHERE "Name" = uil.raw_ingredient_text LIMIT 1), -- Get IngredientId by Name
        1.0, -- Default quantity to 1.0; parsing from raw text is complex and can be done later
        unknown_measurement_type_id, -- Use the fixed ID for 'Unknown' measurement type
        uil.raw_ingredient_text,
        system_person_id,
        NOW(),
        system_person_id,
        NOW()
    FROM unprocessed_ingredient_lines uil;

    GET DIAGNOSTICS processed_count = ROW_COUNT;

    RAISE NOTICE 'Processed % ingredient records in this batch.', processed_count;

    -- Explicitly release the session-level advisory lock.
    PERFORM pg_advisory_unlock(987654321::BIGINT);

    RAISE NOTICE '--- 06_recipe_com_process_ingredients.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'ERROR in 06_recipe_com_process_ingredients.sql (Batch Offset: %): %', current_offset, SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        -- Attempt to release the lock even on error, important for session-level locks.
        PERFORM pg_advisory_unlock(987654321::BIGINT);
        -- Re-raise the error to cause the outer transaction (managed by BEGIN/COMMIT) to rollback
        RAISE;
END $$; -- The PL/pgSQL anonymous code block ends here

-- Commit the transaction for this batch.
-- If an error occurred in the PL/pgSQL block and was re-raised, the transaction will be aborted.
COMMIT;
