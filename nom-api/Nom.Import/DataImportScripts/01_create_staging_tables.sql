-- File: nom-api/Nom.Import/DataImportScripts/01_create_staging_tables.sql
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
    food_category_id INT,
    publication_date TEXT
);

-- Staging table for nutrient.csv
CREATE TABLE "Staging_Nutrient" (
    id INT PRIMARY KEY,
    name TEXT NOT NULL,
    unit_name VARCHAR(10) NOT NULL,
    nutrient_nbr NUMERIC,
    rank INT
);

-- Staging table for food_nutrient.csv
CREATE TABLE "Staging_Food_Nutrient" (
    id INT PRIMARY KEY,
    fdc_id INT NOT NULL,
    nutrient_id INT NOT NULL,
    amount NUMERIC(18, 8),
    data_points INT,
    derivation_id INT,
    min NUMERIC,
    max NUMERIC,
    median NUMERIC,
    loq NUMERIC,
    footnote TEXT,
    min_year_acqured TEXT
);

