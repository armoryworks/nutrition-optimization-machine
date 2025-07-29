-- Enhanced staging tables for comprehensive data import with quality scoring

-- Drop existing staging tables if they exist
DROP TABLE IF EXISTS "Staging_Food_Enhanced";
DROP TABLE IF EXISTS "Staging_Nutrient_Enhanced";
DROP TABLE IF EXISTS "Staging_Food_Nutrient_Enhanced";
DROP TABLE IF EXISTS "Staging_Guideline";
DROP TABLE IF EXISTS "Staging_Measure_Unit";
DROP TABLE IF EXISTS "Staging_Food_Category";
DROP TABLE IF EXISTS "Staging_Foundation_Food";
DROP TABLE IF EXISTS "Staging_Survey_Food";
DROP TABLE IF EXISTS "Staging_Branded_Food";
DROP TABLE IF EXISTS "Staging_Recipe";
DROP TABLE IF EXISTS "Staging_Food_Portion";

-- Create enhanced staging table for food data with quality scoring
CREATE TABLE "Staging_Food_Enhanced" (
    fdc_id TEXT,
    data_type TEXT,
    description TEXT,
    food_category_id TEXT,
    publication_date TEXT,
    quality_score NUMERIC DEFAULT 0.5,
    data_points INTEGER,
    min_year_acquired INTEGER,
    brand_owner TEXT,
    brand_name TEXT,
    ingredients TEXT,
    serving_size TEXT,
    serving_size_unit TEXT,
    household_serving_fulltext TEXT,
    branded_food_category TEXT,
    short_description TEXT
);

-- Create enhanced staging table for nutrient data with quality scoring
CREATE TABLE "Staging_Nutrient_Enhanced" (
    id TEXT,
    name TEXT,
    unit_name TEXT,
    nutrient_nbr TEXT,
    rank TEXT,
    quality_score NUMERIC DEFAULT 0.5
);

-- Create enhanced staging table for food-nutrient relationships with quality scoring
CREATE TABLE "Staging_Food_Nutrient_Enhanced" (
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
    percent_daily_value TEXT,
    quality_score NUMERIC DEFAULT 0.5
);

-- Create staging table for dietary guidelines
CREATE TABLE "Staging_Guideline" (
    "NutrientName" TEXT,
    "GoalTypeName" TEXT,
    "RecommendedAmount" TEXT,
    "MaxAmount" TEXT,
    "UnitName" TEXT
);

-- Create staging table for measurement units
CREATE TABLE "Staging_Measure_Unit" (
    id TEXT,
    name TEXT
);

-- Create staging table for food categories
CREATE TABLE "Staging_Food_Category" (
    id TEXT,
    code TEXT,
    description TEXT
);

-- Create staging table for foundation foods
CREATE TABLE "Staging_Foundation_Food" (
    fdc_id TEXT,
    NDB_number TEXT,
    footnote TEXT
);

-- Create staging table for survey foods (SR legacy foods)
CREATE TABLE "Staging_Survey_Food" (
    fdc_id TEXT,
    NDB_number TEXT
);

-- Create staging table for branded foods
CREATE TABLE "Staging_Branded_Food" (
    fdc_id TEXT,
    brand_owner TEXT,
    brand_name TEXT,
    subbrand_name TEXT,
    gtin_upc TEXT,
    ingredients TEXT,
    not_a_significant_source_of TEXT,
    serving_size TEXT,
    serving_size_unit TEXT,
    household_serving_fulltext TEXT,
    branded_food_category TEXT,
    data_source TEXT,
    package_weight TEXT,
    modified_date TEXT,
    available_date TEXT,
    market_country TEXT,
    discontinued_date TEXT,
    preparation_state_code TEXT,
    trade_channel TEXT,
    short_description TEXT,
    material_code TEXT
);

-- Create staging table for recipes
CREATE TABLE "Staging_Recipe" (
    id TEXT,
    title TEXT,
    ingredients TEXT,
    directions TEXT,
    link TEXT,
    source TEXT,
    NER TEXT
);

-- Create staging table for food portions
CREATE TABLE "Staging_Food_Portion" (
    id TEXT,
    fdc_id TEXT,
    seq_num TEXT,
    amount TEXT,
    measure_unit_id TEXT,
    portion_description TEXT,
    modifier TEXT,
    gram_weight TEXT,
    data_points TEXT,
    footnote TEXT,
    min_year_acquired TEXT
);

-- Create indexes for better performance during import
CREATE INDEX IF NOT EXISTS idx_staging_food_enhanced_fdc_id ON "Staging_Food_Enhanced" (fdc_id);
CREATE INDEX IF NOT EXISTS idx_staging_food_enhanced_quality ON "Staging_Food_Enhanced" (quality_score);
CREATE INDEX IF NOT EXISTS idx_staging_food_enhanced_type ON "Staging_Food_Enhanced" (data_type);
CREATE INDEX IF NOT EXISTS idx_staging_nutrient_enhanced_id ON "Staging_Nutrient_Enhanced" (id);
CREATE INDEX IF NOT EXISTS idx_staging_nutrient_enhanced_quality ON "Staging_Nutrient_Enhanced" (quality_score);
CREATE INDEX IF NOT EXISTS idx_staging_food_nutrient_enhanced_fdc_id ON "Staging_Food_Nutrient_Enhanced" (fdc_id);
CREATE INDEX IF NOT EXISTS idx_staging_food_nutrient_enhanced_nutrient_id ON "Staging_Food_Nutrient_Enhanced" (nutrient_id);
CREATE INDEX IF NOT EXISTS idx_staging_food_nutrient_enhanced_quality ON "Staging_Food_Nutrient_Enhanced" (quality_score); 