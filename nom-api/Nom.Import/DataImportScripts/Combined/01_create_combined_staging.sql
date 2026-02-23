-- Combined USDA + OFF Staging Tables
-- Matches output CSVs from prepare_combined_import.js

DROP TABLE IF EXISTS "Staging_Combined_Food" CASCADE;
DROP TABLE IF EXISTS "Staging_Combined_Food_Nutrient" CASCADE;
DROP TABLE IF EXISTS "Staging_Combined_Alias" CASCADE;
DROP TABLE IF EXISTS "Staging_Combined_Packaging" CASCADE;

-- Staging: one row per ingredient
CREATE TABLE "Staging_Combined_Food" (
    fdc_id TEXT NOT NULL,
    description TEXT NOT NULL,
    data_type TEXT NOT NULL,
    source_priority INTEGER NOT NULL
);
CREATE INDEX idx_scf_fdc_id ON "Staging_Combined_Food" (fdc_id);
CREATE INDEX idx_scf_description ON "Staging_Combined_Food" (LOWER(TRIM(description)));

-- Staging: one row per (ingredient, nutrient)
CREATE TABLE "Staging_Combined_Food_Nutrient" (
    fdc_id TEXT NOT NULL,
    nutrient_id BIGINT NOT NULL,
    amount NUMERIC NOT NULL,
    measurement_id BIGINT NOT NULL
);
CREATE INDEX idx_scfn_fdc_id ON "Staging_Combined_Food_Nutrient" (fdc_id);

-- Staging: aliases (original names + OFF tags)
CREATE TABLE "Staging_Combined_Alias" (
    fdc_id TEXT NOT NULL,
    alias_name TEXT NOT NULL,
    source_context TEXT
);
CREATE INDEX idx_sca_fdc_id ON "Staging_Combined_Alias" (fdc_id);

-- Staging: OFF-derived packaging
CREATE TABLE "Staging_Combined_Packaging" (
    ingredient_pattern TEXT NOT NULL,
    package_name TEXT NOT NULL,
    package_size NUMERIC NOT NULL,
    package_size_unit TEXT NOT NULL,
    size_category TEXT NOT NULL,
    size_in_base_units NUMERIC NOT NULL,
    is_default BOOLEAN NOT NULL DEFAULT TRUE,
    source TEXT NOT NULL DEFAULT 'off-etl'
);
