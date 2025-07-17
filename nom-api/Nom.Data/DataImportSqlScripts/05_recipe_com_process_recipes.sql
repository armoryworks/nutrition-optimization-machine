-- 05_recipe_com_process_recipes.sql
-- This script processes a single batch of raw recipe data from
-- recipe.recipe_com_recipe_staging and inserts it into recipe."Recipe".
-- It expects _offset and _limit variables to be passed by the calling script via string injection.
-- This script runs as a single, atomic transaction, with PL/pgSQL logic inside a DO block.

-- Set client_min_messages to WARNING to avoid excessive output from notices if not needed
SET client_min_messages TO WARNING;
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

-- Start a transaction for this single batch. This BEGIN/COMMIT wraps the entire script execution.
BEGIN;

-- The PL/pgSQL anonymous code block starts here
DO $$
DECLARE
    system_person_id BIGINT := 1;
    processed_count INT := 0;
    -- These placeholders will be replaced by the shell script before execution
    current_offset INT := __OFFSET_PLACEHOLDER__;
    current_limit INT := __LIMIT_PLACEHOLDER__;
BEGIN
    RAISE NOTICE '--- Starting 05_recipe_com_process_recipes.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;

    -- Acquire a session-level advisory lock for this process to prevent concurrency issues.
    -- Using a distinct lock ID for recipes (123456789)
    PERFORM pg_advisory_lock(123456789::BIGINT);

    -- Select a batch of raw recipes that have not yet been processed (i.e., not in recipe."Recipe")
    -- The _offset and _limit are applied here.
    -- Ordering by link ensures consistent pagination across runs.
    WITH unprocessed_recipes_batch AS (
        SELECT
            s.link,
            s.title,
            s.source,
            s.ingredients, -- raw ingredients string
            s.directions   -- raw directions string
        FROM recipe.recipe_com_recipe_staging s
        WHERE NOT EXISTS (
            SELECT 1
            FROM recipe."Recipe" r
            WHERE r."SourceUrl" = s.link -- Reverted to case-sensitive match
        )
        ORDER BY s.link -- Crucial for consistent OFFSET/LIMIT
        OFFSET current_offset
        LIMIT current_limit
    )
    -- Use MERGE for upserting into recipe."Recipe" based on "SourceUrl"
    MERGE INTO recipe."Recipe" AS target
    USING unprocessed_recipes_batch AS source
    ON target."SourceUrl" = source.link -- Reverted to case-sensitive match
    WHEN NOT MATCHED THEN
        INSERT (
            "Name",
            "Description",
            "Instructions",
            "RawIngredientsString",
            "SourceUrl",
            "SourceSite",
            "IsCurated",
            "CreatedByPersonId",
            "CreatedDate",
            "LastModifiedByPersonId",
            "LastModifiedDate"
        )
        VALUES (
            source.title,
            source.title,
            source.directions,
            source.ingredients,
            source.link,
            source.source,
            FALSE, -- Default to not curated
            system_person_id,
            NOW(),
            system_person_id,
            NOW()
        );

    GET DIAGNOSTICS processed_count = ROW_COUNT;

    RAISE NOTICE 'Processed % recipe records in this batch.', processed_count;

    -- Explicitly release the session-level advisory lock.
    PERFORM pg_advisory_unlock(123456789::BIGINT);

    RAISE NOTICE '--- 05_recipe_com_process_recipes.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'ERROR in 05_recipe_com_process_recipes.sql (Batch Offset: %): %', current_offset, SQLERRM;
        PERFORM pg_advisory_unlock(123456789::BIGINT); -- Attempt unlock even on error
        RAISE;
END $$; -- The PL/pgSQL anonymous code block ends here

-- Commit the transaction for this batch.
-- If an error occurred in the PL/pgSQL block and was re-raised, the transaction will be aborted.
COMMIT;
