-- 04_5_recipe_com_add_parsing_columns.sql
-- This script adds the necessary nullable columns for parsing results
-- to the recipe.recipe_com_raw_ingredients_exploded_staging table.
-- It is designed to be idempotent (will not fail if columns already exist).

-- Set client_min_messages to WARNING to avoid excessive output from notices if not needed
SET client_min_messages TO WARNING;
SET search_path TO public, recipe;

-- Start a transaction for this script.
BEGIN;

DO $$
DECLARE
    col_exists BOOLEAN;
BEGIN
    RAISE NOTICE '--- Starting 04_5_recipe_com_add_parsing_columns.sql (Adding parsing columns) ---';

    -- Acquire a session-level advisory lock for this process
    PERFORM pg_advisory_lock(456789012::BIGINT); -- New distinct lock ID for this script

    -- Add 'cleaned_ingredient_name' column if it doesn't exist
    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'recipe' AND table_name = 'recipe_com_raw_ingredients_exploded_staging'
        AND column_name = 'cleaned_ingredient_name'
    ) INTO col_exists;
    IF NOT col_exists THEN
        RAISE NOTICE 'Adding column cleaned_ingredient_name to recipe.recipe_com_raw_ingredients_exploded_staging';
        ALTER TABLE recipe.recipe_com_raw_ingredients_exploded_staging ADD COLUMN cleaned_ingredient_name TEXT;
    END IF;

    -- Add 'quantity' column if it doesn't exist
    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'recipe' AND table_name = 'recipe_com_raw_ingredients_exploded_staging'
        AND column_name = 'quantity'
    ) INTO col_exists;
    IF NOT col_exists THEN
        RAISE NOTICE 'Adding column quantity to recipe.recipe_com_raw_ingredients_exploded_staging';
        ALTER TABLE recipe.recipe_com_raw_ingredients_exploded_staging ADD COLUMN quantity DECIMAL(18,4);
    END IF;

    -- Add 'measurement_type_name' column if it doesn't exist
    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'recipe' AND table_name = 'recipe_com_raw_ingredients_exploded_staging'
        AND column_name = 'measurement_type_name'
    ) INTO col_exists;
    IF NOT col_exists THEN
        RAISE NOTICE 'Adding column measurement_type_name to recipe.recipe_com_raw_ingredients_exploded_staging';
        ALTER TABLE recipe.recipe_com_raw_ingredients_exploded_staging ADD COLUMN measurement_type_name TEXT;
    END IF;

    RAISE NOTICE '--- 04_5_recipe_com_add_parsing_columns.sql completed successfully ---';

EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'ERROR in 04_5_recipe_com_add_parsing_columns.sql: %', SQLERRM;
        PERFORM pg_advisory_unlock(456789012::BIGINT);
        RAISE;
END $$;

COMMIT;
