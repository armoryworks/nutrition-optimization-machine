-- 07_recipe_com_process_instructions.sql
-- This script processes a single batch of raw instruction data from
-- recipe.recipe_com_raw_instructions_exploded_staging, and inserts it into
-- the recipe."RecipeStep" table.
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
BEGIN
    RAISE NOTICE '--- Starting 07_recipe_com_process_instructions.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;

    -- Acquire a session-level advisory lock for this process to prevent concurrency issues.
    -- This lock will persist until explicitly unlocked or the session ends.
    PERFORM pg_advisory_lock(246813579::BIGINT);

    -- Select a batch of instruction lines that have not yet been processed
    -- The _offset and _limit are applied here
    -- Join with recipe."Recipe" to ensure we only process instructions for recipes already processed
    -- and order by source_link and instruction_step_number for consistent pagination.
    WITH unprocessed_instruction_lines AS (
        SELECT
            rie.source_link,
            rie.instruction_step_number,
            rie.raw_instruction_text,
            r."Id" AS recipe_id -- Get recipe_id here for direct use
        FROM recipe.recipe_com_raw_instructions_exploded_staging rie
        JOIN recipe."Recipe" r ON rie.source_link = r."SourceUrl"
        WHERE NOT EXISTS (
            SELECT 1
            FROM recipe."RecipeStep" rs
            WHERE rs."RecipeId" = r."Id" AND rs."StepNumber" = rie.instruction_step_number -- Corrected: Use StepNumber
        )
        ORDER BY rie.source_link, rie.instruction_step_number -- Crucial for consistent OFFSET/LIMIT
        OFFSET current_offset
        LIMIT current_limit
    )
    INSERT INTO recipe."RecipeStep" (
        "RecipeId",
        "StepNumber",
        "Summary", -- This is for a short summary, if needed.
        "Description", -- Corrected: inserting full text into "Description"
        "CreatedByPersonId",
        "CreatedDate",
        "LastModifiedByPersonId",
        "LastModifiedDate"
    )
    SELECT
        uil.recipe_id,
        uil.instruction_step_number,
        LEFT(uil.raw_instruction_text, 255), -- Short summary
        uil.raw_instruction_text, -- Insert raw_instruction_text directly into "Description"
        system_person_id,
        NOW(),
        system_person_id,
        NOW()
    FROM unprocessed_instruction_lines uil;

    GET DIAGNOSTICS processed_count = ROW_COUNT;

    RAISE NOTICE 'Processed % instruction records in this batch.', processed_count;

    -- Explicitly release the session-level advisory lock.
    PERFORM pg_advisory_unlock(246813579::BIGINT);

    RAISE NOTICE '--- 07_recipe_com_process_instructions.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'ERROR in 07_recipe_com_process_instructions.sql (Batch Offset: %): %', current_offset, SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        -- Attempt to release the lock even on error, important for session-level locks.
        PERFORM pg_advisory_unlock(246813579::BIGINT);
        -- Re-raise the error to cause the outer transaction (managed by BEGIN/COMMIT) to rollback
        RAISE;
END $$; -- The PL/pgSQL anonymous code block ends here

-- Commit the transaction for this batch.
-- If an error occurred in the PL/pgSQL block and was re-raised, the transaction will be aborted.
COMMIT;
