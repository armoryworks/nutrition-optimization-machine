-- 05_recipe_com_process_recipes.sql
-- This script processes a single batch of recipe main data from recipe.recipe_com_recipe_staging
-- and inserts it into the recipe."Recipe" table.
-- It expects _offset and _limit variables to be passed by the calling script (e.g., import_recipes.sh).
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
BEGIN
    RAISE NOTICE '--- Starting 05_recipe_com_process_recipes.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;

    -- Acquire an advisory lock for this process to prevent concurrency issues
    PERFORM pg_advisory_xact_lock(123456789::BIGINT);

    -- Select a batch of recipe links that have not yet been processed
    -- The OFFSET and LIMIT are applied here
    WITH unprocessed_recipes AS (
        SELECT
            s.link,
            s.title AS recipe_name,
            s.source AS source_site
        FROM recipe.recipe_com_recipe_staging s
        WHERE NOT EXISTS (
            SELECT 1
            FROM recipe."Recipe" r
            WHERE r."SourceUrl" = s.link
        )
        ORDER BY s.link -- Ensure consistent ordering for pagination
        OFFSET current_offset
        LIMIT current_limit
    )
    INSERT INTO recipe."Recipe" (
        "Name",
        "Description",
        "SourceUrl",
        "SourceSite",
        "IsCurated",
        "CuratedById",
        "CuratedDate",
        "CreatedByPersonId",
        "CreatedDate",
        "LastModifiedByPersonId",
        "LastModifiedDate"
    )
    SELECT
        ur.recipe_name,
        ur.recipe_name, -- Placeholder, can be refined from NER or summary later
        ur.link,
        ur.source_site,
        TRUE,
        system_person_id,
        NOW(),
        system_person_id,
        NOW(),
        system_person_id,
        NOW()
    FROM unprocessed_recipes ur;

    GET DIAGNOSTICS processed_count = ROW_COUNT;

    RAISE NOTICE 'Processed % recipe records in this batch.', processed_count;

    -- Release the advisory lock
    PERFORM pg_advisory_unlock(123456789::BIGINT);

    RAISE NOTICE '--- 05_recipe_com_process_recipes.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'ERROR in 05_recipe_com_process_recipes.sql (Batch Offset: %): %', current_offset, SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        -- Re-raise the error to cause the outer transaction (managed by BEGIN/COMMIT) to rollback
        RAISE;
END $$; -- The PL/pgSQL anonymous code block ends here

-- Commit the transaction for this batch.
-- If an error occurred in the PL/pgSQL block and was re-raised, the transaction will be aborted.
COMMIT;
