-- __staging_tables_recipe.sql
-- This script creates all necessary *staging* tables
-- for the recipe import process. It is designed to be idempotent.
-- Permanent transactional tables (e.g., recipe."Recipe", reference."Group")
-- are assumed to be managed by the DbContext/migrations.

-- Set search path to ensure correct schema usage
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

-- Create schemas if they don't exist (these are typically also managed by DbContext,
-- but harmless to have here as IF NOT EXISTS)
CREATE SCHEMA IF NOT EXISTS recipe;
CREATE SCHEMA IF NOT EXISTS reference;
CREATE SCHEMA IF NOT EXISTS nutrient;
CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS plan;
CREATE SCHEMA IF NOT EXISTS shopping;
CREATE SCHEMA IF NOT EXISTS person;
CREATE SCHEMA IF NOT EXISTS auth;

-- Create recipe.recipe_com_raw_staging table
-- This table holds the raw data directly from the CSV before any processing.
CREATE TABLE IF NOT EXISTS recipe.recipe_com_raw_staging (
    blank_col TEXT, -- This column often appears as an empty column in some CSV exports
    title TEXT,
    ingredients TEXT,
    directions TEXT,
    link TEXT UNIQUE, -- Assuming link is a unique identifier for raw recipes
    source TEXT,
    ner TEXT
);
