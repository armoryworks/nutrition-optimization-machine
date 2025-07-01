-- __staging_tables.sql
-- This script creates temporary staging tables for FDC data imports.
-- It should be executed first to ensure a clean environment for subsequent imports.

-- Drop existing temporary tables if they exist to ensure a clean slate
DROP TABLE IF EXISTS fdc_nutrient_staging;
DROP TABLE IF EXISTS fdc_food_staging;
DROP TABLE IF EXISTS fdc_food_nutrient_staging;

-- Drop duplicate report tables if they exist
DROP TABLE IF EXISTS fdc_nutrient_duplicates_report_staging;
DROP TABLE IF EXISTS fdc_food_duplicates_report_staging;
DROP TABLE IF EXISTS fdc_food_nutrient_duplicates_report_staging;


-- Create a staging table for FDC Nutrient data (matches your nutrient.csv header)
CREATE TEMPORARY TABLE IF NOT EXISTS fdc_nutrient_staging (
    id TEXT, -- FDC's nutrient ID (TEXT for robustness)
    name TEXT,
    unit_name TEXT,
    nutrient_nbr TEXT, -- Matches your CSV header, will be used for FdcId or similar
    rank TEXT -- Matches your CSV header
);

-- Create a staging table for FDC Food data (matches your food.csv header)
CREATE TEMPORARY TABLE IF NOT EXISTS fdc_food_staging (
    fdc_id TEXT, -- TEXT for robustness
    data_type TEXT,
    description TEXT,
    food_category_id TEXT, -- Changed to TEXT to handle long values
    publication_date TEXT -- Will cast to DATE later
);

-- Create a staging table for FDC Food Nutrient data (matches your food_nutrient.csv header)
CREATE TEMPORARY TABLE IF NOT EXISTS fdc_food_nutrient_staging (
    id TEXT, -- FDC food_nutrient ID (TEXT for robustness)
    fdc_id TEXT, -- Corresponds to food.fdc_id (TEXT for robustness)
    nutrient_id TEXT, -- Corresponds to nutrient.id (TEXT for robustness)
    amount TEXT, -- Will cast to DECIMAL
    data_points TEXT, -- Will cast to INT
    derivation_id TEXT, -- New column from your CSV
    min TEXT, -- Will cast to DECIMAL
    max TEXT, -- Will cast to DECIMAL
    median TEXT, -- Will cast to DECIMAL
    loq TEXT, -- New column from your CSV, will cast to DECIMAL
    footnote TEXT,
    min_year_acquired TEXT, -- New column from your CSV, will cast to INT
    percent_daily_value TEXT -- New column from your CSV, will cast to DECIMAL
);

-- Create temporary tables to store duplicate records for reporting
CREATE TEMPORARY TABLE IF NOT EXISTS fdc_nutrient_duplicates_report_staging (
    id TEXT,
    name TEXT,
    unit_name TEXT,
    nutrient_nbr TEXT,
    rank TEXT,
    duplicate_reason TEXT
);

CREATE TEMPORARY TABLE IF NOT EXISTS fdc_food_duplicates_report_staging (
    fdc_id TEXT,
    data_type TEXT,
    description TEXT,
    food_category_id TEXT,
    publication_date TEXT,
    duplicate_reason TEXT
);

CREATE TEMPORARY TABLE IF NOT EXISTS fdc_food_nutrient_duplicates_report_staging (
    id TEXT,
    fdc_id TEXT,
    nutrient_id TEXT,
    amount TEXT,
    data_points TEXT,
    derivation_id TEXT,
    min TEXT,
    max TEXT,
    median TEXT,
    loq TEXT,
    footnote TEXT,
    min_year_acquired TEXT,
    percent_daily_value TEXT,
    duplicate_reason TEXT
);
