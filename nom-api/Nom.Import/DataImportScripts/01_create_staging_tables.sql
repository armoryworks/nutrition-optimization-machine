-- File: nom-api/Nom.Import/DataImportSqlScripts/01_create_staging_tables.sql
-- Description: Creates temporary staging tables to hold the raw data from the USDA FDC CSV files.
-- This provides a buffer to clean and transform data before it enters the final application tables.

-- Drop tables if they exist to ensure a clean run
DROP TABLE IF EXISTS "Staging_Food";
DROP TABLE IF EXISTS "Staging_Nutrient";
DROP TABLE IF EXISTS "Staging_Food_Nutrient";

-- Staging table for food.csv
CREATE TABLE "Staging_Food" (
    fdc_id INT PRIMARY KEY,
    data_type TEXT NOT NULL,
    description TEXT NOT NULL,
    food_category_description TEXT, 
    publication_date TEXT
);

-- Staging table for nutrient.csv
CREATE TABLE "Staging_Nutrient" (
    id INT PRIMARY KEY,
    name TEXT NOT NULL,
    unit_name VARCHAR(10) NOT NULL,
    nutrient_nbr TEXT,
    rank TEXT 
);

-- Staging table for food_nutrient.csv
CREATE TABLE "Staging_Food_Nutrient" (
    id INT PRIMARY KEY,
    fdc_id INT NOT NULL,
    nutrient_id INT NOT NULL,
    -- CORRECTED: Changed all nullable numeric/integer columns to TEXT to handle empty strings from the CSV.
    amount TEXT,
    data_points TEXT,
    derivation_id TEXT,
    min TEXT,
    max TEXT,
    median TEXT,
    loq TEXT,
    footnote TEXT,
    min_year_acqured TEXT,
    extra_column TEXT
);
