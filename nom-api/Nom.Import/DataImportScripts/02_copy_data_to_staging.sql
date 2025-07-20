-- File: nom-api/Nom.Import/DataImportScripts/02_copy_data_to_staging.sql
-- Description: Uses the PostgreSQL server-side COPY command to efficiently bulk-load data
-- from the specified CSV files into the staging tables.

-- IMPORTANT: This script uses the server-side COPY command. The PostgreSQL server process
-- must have read access to the directory and files specified. The user executing this
-- command must be a superuser or have the 'pg_read_server_files' role.

-- The {SourceDirectory} placeholder will be replaced by the C# import application
-- with the path from your appsettings.json (e.g., '/home/dhokanson/Dev/ImportSource/').

COPY "Staging_Food" (fdc_id, data_type, description, food_category_id, publication_date) FROM '{SourceDirectory}food.csv' WITH (FORMAT CSV, HEADER true);

COPY "Staging_Nutrient" (id, name, unit_name, nutrient_nbr, rank) FROM '{SourceDirectory}nutrient.csv' WITH (FORMAT CSV, HEADER true);

COPY "Staging_Food_Nutrient" (id, fdc_id, nutrient_id, amount, data_points, derivation_id, min, max, median, loq, footnote, min_year_acqured) FROM '{SourceDirectory}food_nutrient.csv' WITH (FORMAT CSV, HEADER true);
