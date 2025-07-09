-- 06_recipe_com_process_ingredients_fuzzy.sql
-- This script processes cleaned ingredient names from recipe.recipe_com_final_ingredients_staging,
-- attempting to match them to existing ingredients in recipe."Ingredient" using fuzzy matching (Levenshtein, Jaro-Winkler)
-- and a regex fallback. It then inserts or updates records in recipe."RecipeIngredient".
-- It expects _offset and _limit variables to be passed by the calling script.

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
    current_offset INT := current_setting('nom.current_offset')::INT;
    current_limit INT := current_setting('nom.current_limit')::INT;
BEGIN
    RAISE NOTICE '--- Starting 06_recipe_com_process_ingredients_fuzzy.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;

    -- Acquire a session-level advisory lock for this process
    PERFORM pg_advisory_lock(530919878::BIGINT); -- New distinct lock ID for this script

    -- CTE to select a batch of records from final_ingredients_staging that are not yet processed
    WITH batch_to_process AS (
        SELECT
            fis.source_link,
            fis.original_line_order,
            fis.split_sub_order,
            fis.final_ingredient_name,
            fis.quantity,
            fis.measurement_type_name
        FROM recipe.recipe_com_final_ingredients_staging fis
        WHERE fis.is_processed = FALSE -- Only process records not yet marked as processed
        ORDER BY fis.source_link, fis.original_line_order, fis.split_sub_order
        OFFSET current_offset
        LIMIT current_limit
        FOR UPDATE SKIP LOCKED -- Use SKIP LOCKED to allow concurrent runs if needed
    ),
    -- CTE for fuzzy matching against recipe."Ingredient"
    ingredient_fuzzy_match AS (
        SELECT
            btp.source_link,
            btp.original_line_order,
            btp.split_sub_order,
            btp.final_ingredient_name,
            btp.quantity,
            btp.measurement_type_name,
            ing."Id" AS matched_ingredient_id,
            ing."Name" AS matched_ingredient_name
        FROM
            batch_to_process btp
        LEFT JOIN LATERAL (
            SELECT
                i."Id",
                i."Name",
                LEVENSHTEIN(LOWER(TRIM(btp.final_ingredient_name)), LOWER(TRIM(i."Name"))) AS levenshtein_dist,
                JARO_WINKLER_SIMILARITY(LOWER(TRIM(btp.final_ingredient_name)), LOWER(TRIM(i."Name"))) AS jaro_winkler_sim
            FROM
                recipe."Ingredient" i
            WHERE
                -- Adjust these thresholds as needed for your data
                LEVENSHTEIN(LOWER(TRIM(btp.final_ingredient_name)), LOWER(TRIM(i."Name"))) <= 3
                OR JARO_WINKLER_SIMILARITY(LOWER(TRIM(btp.final_ingredient_name)), LOWER(TRIM(i."Name"))) >= 0.8
            ORDER BY
                levenshtein_dist ASC, jaro_winkler_sim DESC
            LIMIT 1
        ) AS ing ON TRUE
    ),
    -- CTE for regex fallback if fuzzy match failed
    ingredient_final_selection AS (
        SELECT
            fm.source_link,
            fm.original_line_order,
            fm.split_sub_order,
            fm.final_ingredient_name,
            fm.quantity,
            fm.measurement_type_name,
            COALESCE(fm.matched_ingredient_id,
                (SELECT i_regex."Id" FROM recipe."Ingredient" i_regex
                 WHERE LOWER(i_regex."Name") ~ ('\m' || LOWER(TRIM(REGEXP_REPLACE(fm.final_ingredient_name, '\s*\([^)]*\)\s*', '', 'g'))) || '\M')
                 LIMIT 1)
            ) AS final_ingredient_id,
            COALESCE(fm.matched_ingredient_name,
                (SELECT i_regex."Name" FROM recipe."Ingredient" i_regex
                 WHERE LOWER(i_regex."Name") ~ ('\m' || LOWER(TRIM(REGEXP_REPLACE(fm.final_ingredient_name, '\s*\([^)]*\)\s*', '', 'g'))) || '\M')
                 LIMIT 1)
            ) AS final_ingredient_name_from_db -- Name from DB for the selected ID
        FROM
            ingredient_fuzzy_match fm
    ),
    -- Get Recipe IDs for the batch
    recipe_ids AS (
        SELECT
            r."Id" AS recipe_id,
            r."SourceUrl" AS source_link
        FROM recipe."Recipe" r
        WHERE r."SourceUrl" IN (SELECT DISTINCT source_link FROM batch_to_process)
    )
    -- MERGE statement to insert or update RecipeIngredient records
    INSERT INTO recipe."RecipeIngredient" (
        "RecipeId",
        "IngredientId",
        "IngredientNameRaw",
        "Quantity",
        "MeasurementType",
        "CreatedByPersonId",
        "CreatedDateTime",
        "LastModifiedByPersonId",
        "LastModifiedDateTime"
    )
    SELECT
        ri.recipe_id,
        ifs.final_ingredient_id,
        ifs.final_ingredient_name, -- Use the original cleaned name from staging
        ifs.quantity,
        (SELECT "Id" FROM reference."MeasurementType" WHERE "Name" = ifs.measurement_type_name LIMIT 1), -- Lookup MeasurementType ID
        system_person_id,
        NOW(),
        system_person_id,
        NOW()
    FROM ingredient_final_selection ifs
    JOIN recipe_ids ri ON ifs.source_link = ri.source_link
    WHERE ifs.final_ingredient_id IS NOT NULL -- Only insert if an ingredient ID was found
    ON CONFLICT ("RecipeId", "IngredientId", "IngredientNameRaw") DO UPDATE SET -- Adjust ON CONFLICT constraint if needed
        "Quantity" = EXCLUDED."Quantity",
        "MeasurementType" = EXCLUDED."MeasurementType",
        "LastModifiedByPersonId" = EXCLUDED."LastModifiedByPersonId",
        "LastModifiedDateTime" = EXCLUDED."LastModifiedDateTime";

    GET DIAGNOSTICS processed_count = ROW_COUNT;

    RAISE NOTICE 'Processed and merged % ingredient records into RecipeIngredient in this batch.', processed_count;

    -- Mark the processed records in recipe.recipe_com_final_ingredients_staging as processed
    UPDATE recipe.recipe_com_final_ingredients_staging AS target
    SET
        is_processed = TRUE,
        "LastModifiedDateTime" = NOW()
    FROM ingredient_final_selection ifs
    JOIN recipe_ids ri ON ifs.source_link = ri.source_link
    WHERE target.source_link = ifs.source_link
      AND target.original_line_order = ifs.original_line_order
      AND target.split_sub_order = ifs.split_sub_order
      AND ifs.final_ingredient_id IS NOT NULL; -- Only mark as processed if an ingredient ID was found

    PERFORM pg_advisory_unlock(530919878::BIGINT);

    RAISE NOTICE '--- 06_recipe_com_process_ingredients_fuzzy.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'ERROR in 06_recipe_com_process_ingredients_fuzzy.sql (Batch Offset: %): %', current_offset, SQLERRM;
        PERFORM pg_advisory_unlock(530919878::BIGINT);
        RAISE;
END $$;

COMMIT;
