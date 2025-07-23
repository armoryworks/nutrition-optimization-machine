-- Copy data from CSV files into the staging tables
\copy "Staging_Food" FROM 'food.csv' WITH (FORMAT csv, HEADER true, NULL '');
\copy "Staging_Nutrient" FROM 'nutrient.csv' WITH (FORMAT csv, HEADER true, NULL '');
\copy "Staging_Food_Nutrient" FROM 'food_nutrient.csv' WITH (FORMAT csv, HEADER true, NULL '');

-- *** ADDED: Copy data from the new guidelines CSV ***
\copy "Staging_Guideline" FROM 'guidelines.csv' WITH (FORMAT csv, HEADER true, NULL '');