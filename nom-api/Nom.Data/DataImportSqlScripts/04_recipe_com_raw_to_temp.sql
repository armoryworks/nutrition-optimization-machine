-- 04_recipe_com_raw_to_temp.sql
-- This script handles the initial explosion of raw recipe data into temporary staging tables.
-- It performs the following steps:
-- 1. Checks for and creates necessary temporary staging tables.
-- 2. Populates recipe.recipe_com_recipe_staging with basic recipe metadata.
-- 3. Explodes raw ingredient JSON into recipe_com_raw_ingredients_exploded_staging (no parsing yet).
-- 4. Explodes raw directions JSON into recipe_com_raw_instructions_exploded_staging (no parsing yet).
-- Each step is wrapped in a transaction and includes checks for resumability.

DO $$
DECLARE
    -- Placeholder for system_person_id (assuming it's seeded as 1L)
    system_person_id BIGINT := 1;
BEGIN
    RAISE NOTICE '--- Starting 04_recipe_com_raw_to_temp.sql ---';

    -- Step 1: Create Temporary Staging Tables if they do not exist
    -- This ensures the script is resumable and idempotent for table creation.
    BEGIN
        RAISE NOTICE 'Step 1: Checking and creating temporary staging tables...';
        CREATE TABLE IF NOT EXISTS recipe.recipe_com_recipe_staging (
            link TEXT PRIMARY KEY,
            title TEXT,
            directions TEXT,
            ingredients TEXT,
            source TEXT,
            "CreatedByPersonId" BIGINT NOT NULL DEFAULT 1,
            "CreatedDateTime" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            "LastModifiedByPersonId" BIGINT NOT NULL DEFAULT 1,
            "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS recipe.recipe_com_raw_ingredients_exploded_staging (
            source_link TEXT NOT NULL,
            line_order INT NOT NULL,
            raw_ingredient_text TEXT,
            "CreatedByPersonId" BIGINT NOT NULL DEFAULT 1,
            "CreatedDateTime" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            "LastModifiedByPersonId" BIGINT NOT NULL DEFAULT 1,
            "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            PRIMARY KEY (source_link, line_order)
        );

        CREATE TABLE IF NOT EXISTS recipe.recipe_com_raw_instructions_exploded_staging (
            source_link TEXT NOT NULL,
            instruction_step_number INT NOT NULL,
            raw_instruction_text TEXT,
            "CreatedByPersonId" BIGINT NOT NULL DEFAULT 1,
            "CreatedDateTime" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            "LastModifiedByPersonId" BIGINT NOT NULL DEFAULT 1,
            "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            PRIMARY KEY (source_link, instruction_step_number)
        );
        RAISE NOTICE 'Temporary staging tables checked/created.';
    END;

    -- Step 2: Populate recipe.recipe_com_recipe_staging (Basic Recipe Details)
    -- This step inserts main recipe metadata from the raw source table.
    -- It is resumable by checking if the target table is already populated.
    BEGIN
        RAISE NOTICE 'Step 2: Populating recipe.recipe_com_recipe_staging...';
        IF (SELECT COUNT(*) FROM recipe.recipe_com_recipe_staging) = 0 THEN
            INSERT INTO recipe.recipe_com_recipe_staging (title, directions, ingredients, link, source)
            SELECT
                TRIM(title),
                TRIM(directions),
                TRIM(ingredients),
                TRIM(link),
                TRIM(source)
            FROM recipe.recipe_com_raw_staging
            WHERE TRIM(link) IS NOT NULL AND TRIM(link) != ''
            ON CONFLICT (link) DO NOTHING;
            RAISE NOTICE 'Populated recipe.recipe_com_recipe_staging.';
        ELSE
            RAISE NOTICE 'recipe.recipe_com_recipe_staging already populated. Skipping.';
        END IF;
    EXCEPTION
        WHEN OTHERS THEN
            RAISE NOTICE 'ERROR in Step 2 (Populating recipe.recipe_com_recipe_staging): %', SQLERRM;
            RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
            RAISE; -- Re-raise the error
    END;

    -- Step 3: Explode Raw Ingredient JSON into recipe_com_raw_ingredients_exploded_staging
    -- This step breaks down the ingredients JSON array into individual rows.
    -- No parsing of individual ingredient text occurs here.
    BEGIN
        RAISE NOTICE 'Step 3: Exploding raw ingredient JSON into recipe_com_raw_ingredients_exploded_staging...';
        -- Check if the table is already populated for all source links present in raw_staging
        IF NOT EXISTS (SELECT 1 FROM recipe.recipe_com_raw_ingredients_exploded_staging WHERE source_link IN (SELECT link FROM recipe.recipe_com_raw_staging)) THEN
            INSERT INTO recipe.recipe_com_raw_ingredients_exploded_staging (source_link, line_order, raw_ingredient_text)
            SELECT
                r.link,
                (idx - 1) AS line_order, -- Array index is 1-based, convert to 0-based
                TRIM(ingredient_text.value)
            FROM recipe.recipe_com_raw_staging r,
                jsonb_array_elements_text(REPLACE(r.ingredients, '\u0000', '')::jsonb) WITH ORDINALITY AS ingredient_text(value, idx) -- Sanitize null chars
            WHERE TRIM(ingredient_text.value) IS NOT NULL AND TRIM(ingredient_text.value) != ''
            ON CONFLICT (source_link, line_order) DO NOTHING; -- Ensure idempotency for individual ingredient lines
            RAISE NOTICE 'Exploded raw ingredients into recipe_com_raw_ingredients_exploded_staging.';
        ELSE
            RAISE NOTICE 'recipe_com_raw_ingredients_exploded_staging already populated for existing links. Skipping.';
        END IF;
    EXCEPTION
        WHEN OTHERS THEN
            RAISE NOTICE 'ERROR in Step 3 (Exploding raw ingredients): %', SQLERRM;
            RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
            RAISE; -- Re-raise the error
    END;

    -- Step 4: Explode Raw Directions JSON into recipe_com_raw_instructions_exploded_staging
    -- This step breaks down the directions JSON array into individual rows.
    -- No parsing of individual instruction text occurs here.
    BEGIN
        RAISE NOTICE 'Step 4: Exploding raw directions JSON into recipe_com_raw_instructions_exploded_staging...';
        -- Check if the table is already populated for all source links present in raw_staging
        IF NOT EXISTS (SELECT 1 FROM recipe.recipe_com_raw_instructions_exploded_staging WHERE source_link IN (SELECT link FROM recipe.recipe_com_raw_staging)) THEN
            INSERT INTO recipe.recipe_com_raw_instructions_exploded_staging (source_link, instruction_step_number, raw_instruction_text)
            SELECT
                r.link,
                (idx - 1) AS instruction_step_number, -- Array index is 1-based, convert to 0-based
                TRIM(instruction_text.value)
            FROM recipe.recipe_com_raw_staging r,
                jsonb_array_elements_text(REPLACE(r.directions, '\u0000', '')::jsonb) WITH ORDINALITY AS instruction_text(value, idx) -- Sanitize null chars
            WHERE TRIM(instruction_text.value) IS NOT NULL AND TRIM(instruction_text.value) != ''
            ON CONFLICT (source_link, instruction_step_number) DO NOTHING; -- Ensure idempotency for individual instruction lines
            RAISE NOTICE 'Exploded raw instructions into recipe_com_raw_instructions_exploded_staging.';
        ELSE
            RAISE NOTICE 'recipe_com_raw_instructions_exploded_staging already populated for existing links. Skipping.';
        END IF;
    EXCEPTION
        WHEN OTHERS THEN
            RAISE NOTICE 'ERROR in Step 4 (Exploding raw instructions): %', SQLERRM;
            RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
            RAISE; -- Re-raise the error
    END;

    RAISE NOTICE '--- 04_recipe_com_raw_to_temp.sql completed successfully ---';

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'CRITICAL ERROR in 04_recipe_com_raw_to_temp.sql: %', SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        RAISE; -- Re-raise the original error, causing rollback of the entire DO block
END $$;
