-- 05_5_recipe_com_parse_ingredient_details.sql
-- This script parses quantity, measurement type, and cleaned ingredient name from raw_text
-- in recipe.recipe_com_raw_ingredients_exploded_staging using a comprehensive parsing function.
-- It expects _offset and _limit variables to be passed by the calling script.
-- This script runs as a single, atomic transaction, with PL/pgSQL logic inside a DO block.

-- Set client_min_messages to WARNING to suppress verbose DEBUG notices during normal operation.
-- Only WARNINGs and higher will be displayed.
SET client_min_messages TO WARNING;
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

-- Set custom session variables using psql's -v variables.
SET nom.current_offset = :_offset;
SET nom.current_limit = :_limit;

-- Start a transaction for this single batch.
BEGIN;

DO $$
DECLARE
    system_person_id BIGINT := 1;
    processed_count INT := 0;
    error_count INT := 0;
    current_offset INT := current_setting('nom.current_offset')::INT;
    current_limit INT := current_setting('nom.current_limit')::INT;
    -- Variables to hold data for the current row being processed
    v_source_link TEXT;
    v_line_order INT;
    v_raw_text TEXT; -- This variable will hold the content of the 'ingredient_line' column
    parsed_qty DECIMAL(18,4);
    parsed_unit TEXT;
    parsed_cleaned_name TEXT;
    -- For error handling
    error_message TEXT;
    error_context TEXT;
BEGIN
    -- These notices will still show due to client_min_messages being WARNING, as they are general info messages.
    RAISE NOTICE '--- Starting 05_5_recipe_com_parse_ingredient_details.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;
    RAISE NOTICE 'DEBUG: Current client_min_messages setting: %', current_setting('client_min_messages');


    -- Acquire a session-level advisory lock for this process.
    -- This helps prevent multiple concurrent runs of *this specific script* from processing the same batch.
    PERFORM pg_advisory_lock(530919876::BIGINT); -- New distinct lock ID for this script

    -- Iterate through the batch of records, selecting only unprocessed ones.
    -- FOR UPDATE SKIP LOCKED ensures concurrency by skipping rows locked by other transactions.
    FOR v_source_link, v_line_order, v_raw_text IN
        SELECT
            source_link,
            line_order,
            ingredient_line -- CORRECTED: Select 'ingredient_line' column
        FROM recipe.recipe_com_raw_ingredients_exploded_staging
        WHERE (cleaned_ingredient_name IS NULL OR cleaned_ingredient_name = '') -- Check for unparsed names
          AND is_processed = FALSE -- Ensure it hasn't been marked as processed (even if errored)
        ORDER BY source_link, line_order
        OFFSET current_offset
        LIMIT CASE WHEN current_limit > 0 THEN current_limit ELSE NULL END -- Apply limit only if > 0
        FOR UPDATE SKIP LOCKED -- Use SKIP LOCKED to allow concurrent runs if needed
    LOOP
        BEGIN
            -- This specific DEBUG notice is now suppressed by SET client_min_messages TO WARNING;
            -- RAISE NOTICE 'DEBUG 05_5: Attempting to parse: source_link=%, line_order=%, raw_text="%".',
            --               v_source_link, v_line_order, v_raw_text;

            -- Call the comprehensive parsing function (using the _v2 version)
            SELECT quantity_out, unit_name_out, cleaned_name_out
            INTO parsed_qty, parsed_unit, parsed_cleaned_name
            FROM parse_ingredient_line_comprehensive_v2(v_raw_text); -- Call the V2 function

            -- Update the current record with parsed details
            UPDATE recipe.recipe_com_raw_ingredients_exploded_staging
            SET
                quantity = parsed_qty,
                measurement_type_name = parsed_unit,
                cleaned_ingredient_name = parsed_cleaned_name,
                is_processed = TRUE, -- Mark as successfully processed
                "LastModifiedDateTime" = NOW()
            WHERE source_link = v_source_link
              AND line_order = v_line_order;

            processed_count := processed_count + 1;
            -- This specific DEBUG notice is now suppressed by SET client_min_messages TO WARNING;
            -- RAISE NOTICE 'DEBUG 05_5: Successfully parsed: "%" -> Qty: %, Unit: %, Cleaned: "%"', v_raw_text, parsed_qty, parsed_unit, parsed_cleaned_name;

        EXCEPTION
            WHEN OTHERS THEN
                -- Capture the error message for logging
                GET STACKED DIAGNOSTICS error_message = MESSAGE_TEXT,
                                        error_context = PG_EXCEPTION_CONTEXT;
                -- This will still show as it's a WARNING
                RAISE WARNING 'Error parsing ingredient line: source_link=%, line_order=%, raw_text="%". Error: %',
                              v_source_link, v_line_order, v_raw_text, error_message;
                
                -- This will still show as it's a NOTICE, but it's explicitly for malformed lines
                RAISE NOTICE 'MALFORMED_LINE: source_link=%, line_order=%, raw_text="%", error="%".', v_source_link, v_line_order, v_raw_text, error_message;

                -- Update the record to mark it as processed with an error, so it's skipped in future runs
                UPDATE recipe.recipe_com_raw_ingredients_exploded_staging
                SET
                    cleaned_ingredient_name = 'PARSE_ERROR', -- Special value to indicate parsing failed
                    quantity = NULL,
                    measurement_type_name = NULL,
                    is_processed = TRUE, -- Mark as processed to prevent re-attempting
                    "LastModifiedDateTime" = NOW()
                WHERE source_link = v_source_link
                  AND line_order = v_line_order;
                
                error_count := error_count + 1;

        END; -- End of inner BEGIN/EXCEPTION block for single row processing
    END LOOP;

    RAISE NOTICE 'Processed % raw ingredient records for parsing in this batch. Encountered % errors.', processed_count, error_count;

    -- Explicitly release the session-level advisory lock.
    PERFORM pg_advisory_unlock(530919876::BIGINT);

    RAISE NOTICE '--- 05_5_recipe_com_parse_ingredient_details.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        -- This block catches critical errors that occur outside the inner loop (e.g., lock acquisition failure)
        GET STACKED DIAGNOSTICS error_message = MESSAGE_TEXT,
                                error_context = PG_EXCEPTION_CONTEXT;
        RAISE EXCEPTION 'CRITICAL ERROR in 05_5_recipe_com_parse_ingredient_details.sql (Batch Offset: %): %. Context: %', current_offset, error_message, error_context;
        PERFORM pg_advisory_unlock(530919876::BIGINT); -- Ensure lock is released even on critical error
        RAISE; -- Re-raise the exception after logging and cleanup
END $$;

COMMIT;
