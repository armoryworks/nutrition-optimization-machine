-- 05_6_recipe_com_split_ingredients.sql
-- This script takes the cleaned_ingredient_name from recipe.recipe_com_raw_ingredients_exploded_staging,
-- and attempts to split composite ingredient lines (e.g., "salt and pepper") into individual ingredient records.
-- It assumes the input cleaned_ingredient_name has already undergone aggressive cleaning by 05_5.
-- It inserts the refined, atomic ingredient records into recipe.recipe_com_final_ingredients_staging.
-- It expects _offset and _limit variables to be passed by the calling script via string injection.

-- Ensure script stops on first error
\set ON_ERROR_STOP on

SET client_min_messages TO WARNING;
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

-- Create the new final staging table if it doesn't exist
CREATE TABLE IF NOT EXISTS recipe.recipe_com_final_ingredients_staging
(
    source_link text COLLATE pg_catalog."default" NOT NULL,
    original_line_order integer NOT NULL, -- Reference back to the original exploded line
    split_sub_order integer NOT NULL,      -- Order of the ingredient within the split line (for composite lines)
    final_ingredient_name text COLLATE pg_catalog."default",
    quantity numeric(18,4),
    measurement_type_name text COLLATE pg_catalog."default",
    "CreatedByPersonId" bigint NOT NULL DEFAULT 1,
    "CreatedDateTime" timestamp with time zone NOT NULL DEFAULT now(),
    "LastModifiedByPersonId" bigint NOT NULL DEFAULT 1,
    "LastModifiedDateTime" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT recipe_com_final_ingredients_staging_pkey PRIMARY KEY (source_link, original_line_order, split_sub_order)
)
TABLESPACE pg_default;

ALTER TABLE IF EXISTS recipe.recipe_com_final_ingredients_staging
    OWNER to "NomUser";

-- Add 'CreatedByPersonId' column if it doesn't exist (idempotent)
DO $$
DECLARE
    col_exists BOOLEAN;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'recipe' AND table_name = 'recipe_com_final_ingredients_staging'
        AND column_name = 'CreatedByPersonId'
    ) INTO col_exists;
    IF NOT col_exists THEN
        RAISE NOTICE 'Adding column "CreatedByPersonId" to recipe.recipe_com_final_ingredients_staging';
        ALTER TABLE recipe.recipe_com_final_ingredients_staging ADD COLUMN "CreatedByPersonId" bigint NOT NULL DEFAULT 1;
    END IF;
END $$;

-- Add 'LastModifiedByPersonId' column if it doesn't exist (idempotent)
DO $$
DECLARE
    col_exists BOOLEAN;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'recipe' AND table_name = 'recipe_com_final_ingredients_staging'
        AND column_name = 'LastModifiedByPersonId'
    ) INTO col_exists;
    IF NOT col_exists THEN
        RAISE NOTICE 'Adding column "LastModifiedByPersonId" to recipe.recipe_com_final_ingredients_staging';
        ALTER TABLE recipe.recipe_com_final_ingredients_staging ADD COLUMN "LastModifiedByPersonId" bigint NOT NULL DEFAULT 1;
    END IF;
END $$;


-- Function to perform splitting of already cleaned ingredient names
-- THIS FUNCTION IS NOW DEFINED AT THE TOP-LEVEL OF THE SCRIPT
CREATE OR REPLACE FUNCTION split_cleaned_ingredient(
    p_cleaned_name TEXT,
    p_quantity DECIMAL(18,4),
    p_measurement_type_name TEXT
) RETURNS TABLE(
    split_ingredient_name_out TEXT,
    split_quantity_out DECIMAL(18,4),
    split_unit_name_out TEXT
) AS $$
DECLARE
    temp_name TEXT := TRIM(p_cleaned_name);
    split_parts TEXT[];
    part TEXT;
    original_quantity DECIMAL(18,4) := COALESCE(p_quantity, 1.0); -- Propagate original quantity
    original_unit TEXT := COALESCE(p_measurement_type_name, 'each'); -- Propagate original unit
    temp_sub_parts TEXT[] := '{}';
    sub_part TEXT;
BEGIN
    -- Initialize split_parts as an empty array
    split_parts := '{}';

    -- First, split by " and " (case-insensitive, with optional surrounding whitespace)
    -- This handles "salt and pepper"
    IF lower(temp_name) LIKE '% and %' THEN
        split_parts := REGEXP_SPLIT_TO_ARRAY(temp_name, '\s+and\s+', 'i');
    ELSE
        split_parts := ARRAY[temp_name]; -- If no " and ", treat the whole thing as one part for now
    END IF;

    -- Now, for each part, try to split by commas that seem to separate distinct items
    -- This is a heuristic. We're looking for ", word" not "word, word" or "word, minced"
    BEGIN -- This is a nested BEGIN block, NOT a DECLARE block.
        FOREACH part IN ARRAY split_parts LOOP
            -- Heuristic: Split by comma if it's not followed by a common descriptor (which should have been cleaned by 05_5)
            -- This is tricky, so a simple split by comma followed by whitespace might be best here if 05_5 is truly aggressive.
            -- Given 05_5 is now doing aggressive cleaning, we can be more direct.
            IF part LIKE '%,%' THEN
                temp_sub_parts := ARRAY_CAT(temp_sub_parts, REGEXP_SPLIT_TO_ARRAY(part, ',\s*'));
            ELSE
                temp_sub_parts := ARRAY_APPEND(temp_sub_parts, part);
            END IF;
        END LOOP;
        split_parts := temp_sub_parts; -- Update the main array
    END; -- End of inner BEGIN block

    -- Now iterate through the final split parts and return them
    FOREACH part IN ARRAY split_parts LOOP
        part := TRIM(part);
        IF part != '' THEN
            split_ingredient_name_out := part;
            split_quantity_out := original_quantity; -- Propagate original quantity
            split_unit_name_out := original_unit;    -- Propagate original unit
            RETURN NEXT;
        END IF;
    END LOOP;

    -- Fallback: If no parts were generated (e.g., input was empty after splitting), return original as a fallback
    -- This ensures that even if splitting removes everything, we still get a record.
    IF CARDINALITY(split_parts) = 0 OR (CARDINALITY(split_parts) = 1 AND TRIM(split_parts[1]) = '') THEN
        split_ingredient_name_out := TRIM(p_cleaned_name); -- Use the original cleaned name from 05_5
        split_quantity_out := p_quantity;
        split_unit_name_out := p_measurement_type_name;
        RETURN NEXT;
    END IF;

END;
$$ LANGUAGE plpgsql IMMUTABLE;


-- Main batch processing logic starts here, within its own transaction and DO block
BEGIN;

DO $$
DECLARE
    processed_count INT := 0;
    -- These placeholders will be replaced by the shell script before execution
    current_offset INT := __OFFSET_PLACEHOLDER__;
    current_limit INT := __LIMIT_PLACEHOLDER__;
    -- Declare error variables for this DO block
    error_message TEXT;
    error_context TEXT;
BEGIN
    RAISE NOTICE '--- Starting 05_6_recipe_com_split_ingredients.sql (Processing Batch Offset: %, Limit: %) ---', current_offset, current_limit;

    -- Acquire a session-level advisory lock for this process to prevent concurrency issues.
    PERFORM pg_advisory_lock(530919877::BIGINT); -- New distinct lock ID for this script

    -- Select a batch of records from the raw_ingredients_exploded_staging table
    -- that have been parsed (cleaned_ingredient_name is not NULL/empty)
    -- and have NOT yet been processed by this splitting stage (i.e., not in final_ingredients_staging).
    WITH batch_to_process AS (
        SELECT
            rie.source_link,
            rie.line_order,
            rie.cleaned_ingredient_name,
            rie.quantity,
            rie.measurement_type_name
        FROM recipe.recipe_com_raw_ingredients_exploded_staging rie
        WHERE rie.cleaned_ingredient_name IS NOT NULL AND rie.cleaned_ingredient_name != ''
          AND NOT EXISTS (
                SELECT 1 FROM recipe.recipe_com_final_ingredients_staging f
                WHERE f.source_link = rie.source_link AND f.original_line_order = rie.line_order
              )
        ORDER BY rie.source_link, rie.line_order
        OFFSET current_offset
        LIMIT CASE WHEN current_limit > 0 THEN current_limit ELSE NULL END -- Apply limit only if > 0
        FOR UPDATE SKIP LOCKED
    )
    INSERT INTO recipe.recipe_com_final_ingredients_staging (
        source_link,
        original_line_order,
        split_sub_order,
        final_ingredient_name,
        quantity,
        measurement_type_name,
        "CreatedByPersonId",
        "CreatedDateTime",
        "LastModifiedByPersonId",
        "LastModifiedDateTime"
    )
    SELECT
        btp.source_link,
        btp.line_order,
        ROW_NUMBER() OVER (PARTITION BY btp.source_link, btp.line_order ORDER BY (SELECT 1)) AS split_sub_order,
        split.split_ingredient_name_out,
        split.split_quantity_out,
        split.split_unit_name_out,
        1, -- Assuming system_person_id is 1
        NOW(),
        1,
        NOW()
    FROM batch_to_process btp
    CROSS JOIN LATERAL split_cleaned_ingredient(btp.cleaned_ingredient_name, btp.quantity, btp.measurement_type_name) AS split;

    GET DIAGNOSTICS processed_count = ROW_COUNT;

    RAISE NOTICE 'Inserted % refined ingredient records into final staging in this batch.', processed_count;

    -- Mark the original raw_ingredients_exploded_staging records as processed by the splitting stage.
    -- This update should only happen for records that successfully generated at least one entry
    -- in recipe.recipe_com_final_ingredients_staging in this batch.
    UPDATE recipe.recipe_com_raw_ingredients_exploded_staging AS target
    SET is_processed = TRUE, "LastModifiedDateTime" = NOW()
    FROM (
        SELECT DISTINCT source_link, original_line_order
        FROM recipe.recipe_com_final_ingredients_staging
        WHERE "CreatedDateTime" >= (NOW() - INTERVAL '5 seconds') -- Heuristic: recently inserted in this batch
    ) AS recently_processed
    WHERE target.source_link = recently_processed.source_link
      AND target.line_order = recently_processed.original_line_order
      AND target.is_processed = FALSE; -- Only update if not already processed

    PERFORM pg_advisory_unlock(530919877::BIGINT);

    RAISE NOTICE '--- 05_6_recipe_com_split_ingredients.sql (Batch Offset: %) completed successfully ---', current_offset;

EXCEPTION
    WHEN OTHERS THEN
        GET STACKED DIAGNOSTICS error_message = MESSAGE_TEXT;
        RAISE EXCEPTION 'ERROR in 05_6_recipe_com_split_ingredients.sql (Batch Offset: %): %', current_offset, error_message;
        PERFORM pg_advisory_unlock(530919877::BIGINT);
        RAISE;
END $$;

COMMIT;
