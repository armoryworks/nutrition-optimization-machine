-- Drop existing staging tables if they exist
DROP TABLE IF EXISTS "Staging_Food";
DROP TABLE IF EXISTS "Staging_Nutrient";
DROP TABLE IF EXISTS "Staging_Food_Nutrient";
DROP TABLE IF EXISTS "Staging_Guideline"; -- Added for guidelines

-- Create staging table for food data
CREATE TABLE "Staging_Food" (
    fdc_id TEXT,
    data_type TEXT,
    description TEXT,
    food_category_id TEXT,
    publication_date TEXT
);

-- Create staging table for nutrient data
CREATE TABLE "Staging_Nutrient" (
    id TEXT,
    name TEXT,
    unit_name TEXT,
    nutrient_nbr TEXT,
    rank TEXT
);

-- Create staging table for the food-nutrient link
CREATE TABLE "Staging_Food_Nutrient" (
    id TEXT,
    fdc_id TEXT,
    nutrient_id BIGINT,
    amount TEXT,
    data_points TEXT,
    derivation_id TEXT,
    min TEXT,
    max TEXT,
    median TEXT,
    loq TEXT,
    footnote TEXT,
    min_year_acquired TEXT,
    "percent_daily_value" TEXT
);

-- *** ADDED: Create staging table for guideline data ***
CREATE TABLE "Staging_Guideline" (
    "NutrientName" TEXT,
    "GoalTypeName" TEXT,
    "RecommendedAmount" TEXT,
    "MaxAmount" TEXT,
    "UnitName" TEXT
);