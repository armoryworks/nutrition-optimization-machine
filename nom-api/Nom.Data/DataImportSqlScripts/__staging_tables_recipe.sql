-- __staging_tables_recipe.sql
-- This script creates or recreates all necessary staging tables for the recipe import process.
-- It is designed to be idempotent, meaning it can be run multiple times without issues.

SET client_min_messages TO WARNING;
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

BEGIN;

-- Drop tables if they exist to ensure a clean slate for recreation
-- Using CASCADE to ensure dependent objects (like foreign keys) are also dropped
DROP TABLE IF EXISTS recipe.recipe_com_final_ingredients_staging CASCADE;
DROP TABLE IF EXISTS recipe.recipe_com_raw_ingredients_exploded_staging CASCADE;
DROP TABLE IF EXISTS recipe.recipe_com_raw_instructions_exploded_staging CASCADE;
DROP TABLE IF EXISTS recipe.recipe_com_recipe_staging CASCADE;
DROP TABLE IF EXISTS recipe.recipe_com_raw_staging CASCADE;

-- 1. Raw staging table for initial CSV import
CREATE TABLE IF NOT EXISTS recipe.recipe_com_raw_staging (
    id BIGSERIAL PRIMARY KEY,
    blank_col TEXT, -- This column seems to be an artifact of the CSV structure, often empty
    title TEXT,
    ingredients TEXT,
    directions TEXT,
    link TEXT UNIQUE, -- Assuming link is unique for each recipe
    source TEXT,
    ner TEXT, -- Named Entity Recognition data, if available
    "CreatedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);
COMMENT ON TABLE recipe.recipe_com_raw_staging IS 'Staging table for raw recipe data imported directly from CSV.';

-- 2. Staging table for recipes after initial cleaning and before insertion into main Recipe table
CREATE TABLE IF NOT EXISTS recipe.recipe_com_recipe_staging (
    id BIGSERIAL PRIMARY KEY,
    title TEXT NOT NULL,
    link TEXT UNIQUE NOT NULL,
    source TEXT,
    "CreatedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);
COMMENT ON TABLE recipe.recipe_com_recipe_staging IS 'Staging table for cleaned recipe header data before insertion into main Recipe table.';

-- 3. Staging table for exploded raw ingredient lines
CREATE TABLE IF NOT EXISTS recipe.recipe_com_raw_ingredients_exploded_staging (
    id BIGSERIAL PRIMARY KEY,
    source_link TEXT NOT NULL, -- Link to the original recipe
    line_order INT NOT NULL, -- Original order of the ingredient line in the recipe
    ingredient_line TEXT NOT NULL, -- The original raw ingredient line
    -- Columns for parsed data (added by 04_5_recipe_com_add_parsing_columns.sql)
    quantity DECIMAL(18,4),
    measurement_type_name TEXT,
    cleaned_ingredient_name TEXT,
    -- Flag to track processing status for batch operations
    is_processed BOOLEAN DEFAULT FALSE,
    "CreatedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT fk_raw_ing_exp_source_link FOREIGN KEY (source_link) REFERENCES recipe.recipe_com_raw_staging (link) ON DELETE CASCADE,
    -- Explicitly define a UNIQUE constraint for ON CONFLICT clause in 04_recipe_com_raw_to_temp.sql
    CONSTRAINT uq_raw_ing_exp UNIQUE (source_link, line_order)
);
COMMENT ON TABLE recipe.recipe_com_raw_ingredients_exploded_staging IS 'Staging table for individual raw ingredient lines, exploded from the main recipe text.';
CREATE INDEX IF NOT EXISTS idx_raw_ing_exp_source_link ON recipe.recipe_com_raw_ingredients_exploded_staging (source_link);
CREATE INDEX IF NOT EXISTS idx_raw_ing_exp_is_processed ON recipe.recipe_com_raw_ingredients_exploded_staging (is_processed);


-- 4. Staging table for exploded raw instruction steps
CREATE TABLE IF NOT EXISTS recipe.recipe_com_raw_instructions_exploded_staging (
    id BIGSERIAL PRIMARY KEY,
    source_link TEXT NOT NULL, -- Link to the original recipe
    instruction_step_number INT NOT NULL, -- Step number in the original instructions
    instruction_text TEXT NOT NULL, -- The original raw instruction text
    is_processed BOOLEAN DEFAULT FALSE,
    "CreatedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT fk_raw_inst_exp_source_link FOREIGN KEY (source_link) REFERENCES recipe.recipe_com_raw_staging (link) ON DELETE CASCADE,
    -- Explicitly define a UNIQUE constraint for ON CONFLICT clause
    CONSTRAINT uq_raw_inst_exp UNIQUE (source_link, instruction_step_number)
);
COMMENT ON TABLE recipe.recipe_com_raw_instructions_exploded_staging IS 'Staging table for individual raw instruction steps, exploded from the main recipe text.';
CREATE INDEX IF NOT EXISTS idx_raw_inst_exp_source_link ON recipe.recipe_com_raw_instructions_exploded_staging (source_link);
CREATE INDEX IF NOT EXISTS idx_raw_inst_exp_is_processed ON recipe.recipe_com_raw_instructions_exploded_staging (is_processed);


-- 5. NEW: Staging table for final, split ingredient lines before fuzzy matching
-- This table holds one ingredient per row, ready for lookup against the canonical Ingredient table.
CREATE TABLE IF NOT EXISTS recipe.recipe_com_final_ingredients_staging (
    id BIGSERIAL PRIMARY KEY,
    source_link TEXT NOT NULL, -- Link to the original recipe
    original_line_order INT NOT NULL, -- Original order of the ingredient line from raw_ingredients_exploded_staging
    split_sub_order INT NOT NULL, -- Order within the split original line (e.g., for "salt and pepper", pepper might be 2)
    final_ingredient_name TEXT NOT NULL, -- The single, cleaned ingredient name
    quantity DECIMAL(18,4),
    measurement_type_name TEXT,
    is_processed BOOLEAN DEFAULT FALSE, -- Flag to track if this final ingredient has been processed (matched/inserted)
    "CreatedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "LastModifiedDateTime" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT uq_final_ing_stage UNIQUE (source_link, original_line_order, split_sub_order),
    CONSTRAINT fk_final_ing_stage_source_link FOREIGN KEY (source_link) REFERENCES recipe.recipe_com_raw_staging (link) ON DELETE CASCADE
);
COMMENT ON TABLE recipe.recipe_com_final_ingredients_staging IS 'Staging table for individual, split, and cleaned ingredient names, ready for fuzzy matching.';
CREATE INDEX IF NOT EXISTS idx_final_ing_stage_source_link ON recipe.recipe_com_final_ingredients_staging (source_link);
CREATE INDEX IF NOT EXISTS idx_final_ing_stage_is_processed ON recipe.recipe_com_final_ingredients_staging (is_processed);


COMMIT;
