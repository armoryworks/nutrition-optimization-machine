-- __staging_tables_recipe.sql
-- This script creates the necessary staging tables for the recipe ETL process.
-- It does NOT perform any data loading.

-- Drop existing tables if they exist to ensure a clean slate for each run
-- Drop dependent tables first, or use CASCADE
DROP TABLE IF EXISTS recipe_com_ingredient_parsed_staging CASCADE;
DROP TABLE IF EXISTS recipe_com_instruction_parsed_staging CASCADE;
DROP TABLE IF EXISTS recipe_com_recipe_duplicates_report_staging CASCADE;
DROP TABLE IF EXISTS recipe_com_ingredient_duplicates_report_staging CASCADE;
DROP TABLE IF EXISTS recipe_com_instruction_duplicates_report_staging CASCADE;

-- Explicitly drop and create recipe.recipe_com_raw_staging
DROP TABLE IF EXISTS recipe.recipe_com_raw_staging CASCADE;
CREATE TABLE recipe.recipe_com_raw_staging (
    blank_col TEXT, -- First column is often blank in the source CSV
    title TEXT,
    ingredients TEXT,
    directions TEXT,
    link TEXT,
    source TEXT,
    ner TEXT
);

-- Explicitly drop and create recipe.recipe_com_recipe_staging
DROP TABLE IF EXISTS recipe.recipe_com_recipe_staging CASCADE;
CREATE TABLE recipe.recipe_com_recipe_staging (
    id SERIAL PRIMARY KEY, -- Internal ID for staging
    source_link TEXT NOT NULL UNIQUE,
    recipe_name TEXT NOT NULL,
    source_site TEXT,
    raw_ingredients_json TEXT, -- Store original JSON string
    raw_directions_json TEXT, -- Store original JSON string
    original_csv_index TEXT -- To link back to the original CSV line if needed
);

-- Staging table for parsed ingredients
CREATE TABLE recipe_com_ingredient_parsed_staging (
    id SERIAL PRIMARY KEY,
    source_link TEXT NOT NULL, -- Link to recipe.recipe_com_recipe_staging
    line_order INT NOT NULL,
    ingredient_raw_text TEXT,
    ner_ingredient_name TEXT, -- Name identified by NER
    parsed_amount DECIMAL(10, 4),
    parsed_unit_name TEXT,
    final_ingredient_name TEXT NOT NULL, -- Cleaned name for ingredient matching
    final_measurement_type_id BIGINT,
    CONSTRAINT fk_source_link_ingredient FOREIGN KEY (source_link) REFERENCES recipe.recipe_com_recipe_staging (source_link)
);

-- Staging table for parsed instructions
CREATE TABLE recipe_com_instruction_parsed_staging (
    id SERIAL PRIMARY KEY,
    source_link TEXT NOT NULL, -- Link to recipe.recipe_com_recipe_staging
    instruction_step_number SMALLINT NOT NULL,
    instruction_text TEXT NOT NULL,
    summary_text VARCHAR(255), -- A short summary of the instruction
    CONSTRAINT fk_source_link_instruction FOREIGN KEY (source_link) REFERENCES recipe.recipe_com_recipe_staging (source_link)
);

-- Duplicate Reports Staging Tables
CREATE TABLE recipe_com_recipe_duplicates_report_staging (
    source_link TEXT,
    recipe_name TEXT,
    source_site TEXT,
    duplicate_reason TEXT
);

CREATE TABLE recipe_com_ingredient_duplicates_report_staging (
    source_link TEXT,
    ingredient_raw_text TEXT,
    ner_ingredient_name TEXT,
    final_ingredient_name TEXT,
    duplicate_reason TEXT
);

CREATE TABLE recipe_com_instruction_duplicates_report_staging (
    source_link TEXT,
    instruction_step_number SMALLINT,
    instruction_text TEXT,
    duplicate_reason TEXT
);
