@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM  refresh_db_and_migration.bat
REM  Windows equivalent of refresh_db_and_migration.sh
REM  Orchestrates across Docker containers to reset the database
REM  and regenerate EF Core migrations from scratch.
REM ============================================================

set DB_NAME=nom_dev
set DB_USER=nom
set DB_PASSWORD=dev_password
set POSTGRES_CONTAINER=nom_postgres_dev
set API_CONTAINER=nom_api_dev
set MIGRATIONS_DIR=%~dp0Nom.Data\Migrations

echo ============================================================
echo  Database and Migration Reset Process
echo ============================================================

REM --- Step 1: Drop the database ---
echo.
echo [1/8] Terminating active connections and dropping database '%DB_NAME%'...
docker exec %POSTGRES_CONTAINER% psql -U %DB_USER% -d postgres -c "SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '%DB_NAME%' AND pid <> pg_backend_pid();" 2>nul
docker exec %POSTGRES_CONTAINER% psql -U %DB_USER% -d postgres -c "DROP DATABASE IF EXISTS \"%DB_NAME%\";"
if errorlevel 1 (
    echo ERROR: Failed to drop database. Check PostgreSQL container is running.
    exit /b 1
)
echo Database '%DB_NAME%' dropped.

REM --- Step 2: Recreate database ---
echo.
echo [2/8] Creating database '%DB_NAME%'...
docker exec %POSTGRES_CONTAINER% psql -U %DB_USER% -d postgres -c "CREATE DATABASE \"%DB_NAME%\" OWNER \"%DB_USER%\";"
if errorlevel 1 (
    echo ERROR: Failed to create database.
    exit /b 1
)
echo Database '%DB_NAME%' created with owner '%DB_USER%'.

REM --- Step 3: Delete migrations folder ---
echo.
echo [3/8] Deleting migrations folder...
if exist "%MIGRATIONS_DIR%" (
    rmdir /s /q "%MIGRATIONS_DIR%"
)
mkdir "%MIGRATIONS_DIR%"
echo Migrations folder reset.

REM --- Step 3.5: Ensure dotnet-ef tool is installed ---
echo.
echo [3.5/8] Ensuring dotnet-ef tool is installed in container...
docker exec %API_CONTAINER% sh -c "export PATH=\"$PATH:$HOME/.dotnet/tools\" && dotnet ef --version" >nul 2>&1
if errorlevel 1 (
    echo Installing dotnet-ef tool...
    docker exec %API_CONTAINER% sh -c "dotnet tool install --global dotnet-ef"
)
echo dotnet-ef ready.

REM --- Step 4: Build the solution ---
echo.
echo [4/8] Cleaning build artifacts and building solution...
docker exec %API_CONTAINER% sh -c "cd /src && dotnet build nom-api.sln --nologo -v quiet"
if errorlevel 1 (
    echo ERROR: Build failed.
    exit /b 1
)
echo Build succeeded.

REM --- Step 5: Generate InitialCreate migration ---
echo.
echo [5/8] Generating InitialCreate migration...
docker exec %API_CONTAINER% sh -c "export PATH=\"$PATH:$HOME/.dotnet/tools\" && cd /src && dotnet ef migrations add InitialCreate --context ApplicationDbContext --project Nom.Data --startup-project Nom.Api --no-build"
if errorlevel 1 (
    echo ERROR: Migration generation failed.
    exit /b 1
)
echo Migration generated.

REM --- Step 6: Patch migration with custom seed operations ---
echo.
echo [6/8] Patching migration with ApplyCustomUpOperations/ApplyCustomDownOperations...
docker exec %API_CONTAINER% sh -c "cd /src/Nom.Data/Migrations && MIGRATION_FILE=$(ls *_InitialCreate.cs 2>/dev/null | head -1) && if [ -z \"$MIGRATION_FILE\" ]; then echo 'ERROR: Migration file not found'; exit 1; fi && sed -i '1s/^/using Nom.Data.CustomMigration;\n/' \"$MIGRATION_FILE\" && sed -i '/protected override void Up(MigrationBuilder migrationBuilder)/,/^        }/ s/^        }/            migrationBuilder.ApplyCustomUpOperations();\n        }/' \"$MIGRATION_FILE\" && sed -i '/protected override void Down(MigrationBuilder migrationBuilder)/{n;s/{/{ \n            migrationBuilder.ApplyCustomDownOperations();/}' \"$MIGRATION_FILE\" && echo 'Patched: '$MIGRATION_FILE"
if errorlevel 1 (
    echo ERROR: Migration patching failed.
    exit /b 1
)
echo Migration patched.

REM --- Step 7: Rebuild after patching ---
echo.
echo [7/8] Rebuilding after migration patch...
docker exec %API_CONTAINER% sh -c "cd /src && dotnet build nom-api.sln --nologo -v quiet"
if errorlevel 1 (
    echo ERROR: Build failed after patching.
    exit /b 1
)
echo Build succeeded.

REM --- Step 8: Apply migration to database ---
echo.
echo [8/8] Applying migration to database...
docker exec %API_CONTAINER% sh -c "export PATH=\"$PATH:$HOME/.dotnet/tools\" && cd /src && dotnet ef database update --context ApplicationDbContext --project Nom.Data --startup-project Nom.Api --no-build --connection 'Host=postgres-dev;Database=%DB_NAME%;Username=%DB_USER%;Password=%DB_PASSWORD%'"
if errorlevel 1 (
    echo ERROR: Database migration failed.
    exit /b 1
)

REM ============================================================
REM  INGREDIENT IMPORT (Steps 9-12)
REM ============================================================

set REPO_ROOT=%~dp0..
set DATA_DIR=%REPO_ROOT%\data-analysis
set USDA_SOURCE_DIR=%DATA_DIR%\usda-source
set OFF_DIR=%DATA_DIR%\off
set ETL_OUTPUT_DIR=%DATA_DIR%\etl\output
if not defined USDA_VERSION set USDA_VERSION=FoodData_Central_csv_2025-12-18
set USDA_URL=https://fdc.nal.usda.gov/fdc-datasets/%USDA_VERSION%.zip
set OFF_URL=https://static.openfoodfacts.org/data/openfoodfacts-products.jsonl.gz

REM --- Step 9: Download USDA data if missing ---
echo.
echo [9/12] Checking USDA source data...
if not exist "%USDA_SOURCE_DIR%\food.csv" (
    echo USDA data not found. Downloading...
    if not exist "%USDA_SOURCE_DIR%" mkdir "%USDA_SOURCE_DIR%"
    set USDA_ZIP=%TEMP%\%USDA_VERSION%.zip
    if not exist "!USDA_ZIP!" (
        echo   Downloading %USDA_URL%...
        powershell -Command "Invoke-WebRequest -Uri '%USDA_URL%' -OutFile '!USDA_ZIP!'"
        if errorlevel 1 (
            echo ERROR: USDA download failed.
            exit /b 1
        )
    )
    echo   Extracting to %USDA_SOURCE_DIR%...
    powershell -Command "Expand-Archive -Path '!USDA_ZIP!' -DestinationPath '%USDA_SOURCE_DIR%' -Force"
    REM The zip may contain a subdirectory — flatten if needed
    if exist "%USDA_SOURCE_DIR%\%USDA_VERSION%\food.csv" (
        xcopy /Y /E "%USDA_SOURCE_DIR%\%USDA_VERSION%\*" "%USDA_SOURCE_DIR%\" >nul
        rmdir /s /q "%USDA_SOURCE_DIR%\%USDA_VERSION%"
    )
    if errorlevel 1 (
        echo ERROR: USDA extraction failed.
        exit /b 1
    )
    echo USDA data ready.
) else (
    echo USDA data found.
)

REM --- Step 10: Download OFF data if missing ---
echo.
echo [10/12] Checking Open Food Facts data...
if not exist "%OFF_DIR%\openfoodfacts-products.jsonl.gz" (
    echo OFF data not found. Downloading (~10GB, this will take a while^)...
    if not exist "%OFF_DIR%" mkdir "%OFF_DIR%"
    powershell -Command "Invoke-WebRequest -Uri '%OFF_URL%' -OutFile '%OFF_DIR%\openfoodfacts-products.jsonl.gz'"
    if errorlevel 1 (
        echo ERROR: OFF download failed.
        exit /b 1
    )
    echo OFF data ready.
) else (
    echo OFF data found.
)

REM --- Step 11: Run ETL ---
echo.
echo [11/12] Running combined ETL (USDA + OFF -^> CSVs^)...
where node >nul 2>nul
if errorlevel 1 (
    echo WARNING: Node.js not found. Skipping ETL and import.
    echo Install Node.js to enable ingredient import.
    echo Database and migration reset complete (without ingredient import^).
    exit /b 0
)

set USDA_BASE=%USDA_SOURCE_DIR%
node "%DATA_DIR%\etl\prepare_combined_import.js"
if errorlevel 1 (
    echo ERROR: ETL processing failed.
    exit /b 1
)

if not exist "%ETL_OUTPUT_DIR%\combined_food.csv" (
    echo ERROR: ETL did not produce expected output files.
    exit /b 1
)
echo ETL complete.

REM --- Step 11.5: Run retail packaging lookup (optional) ---
set PACKAGING_LOOKUP_SQL=%ETL_OUTPUT_DIR%\packaging_lookup.sql
if not exist "%PACKAGING_LOOKUP_SQL%" (
    echo.
    echo [11.5/12] Running retail packaging lookup (Open Food Facts^)...
    if not defined PACKAGING_LIMIT set PACKAGING_LIMIT=100
    node "%DATA_DIR%\etl\lookup_packaging.js" --limit=%PACKAGING_LIMIT%
    REM Non-fatal: packaging lookup is optional
    if errorlevel 1 (
        echo WARNING: Packaging lookup had errors (non-fatal, continuing^).
    )
) else (
    echo.
    echo [11.5/12] Retail packaging lookup already exists, skipping.
    echo   Delete %PACKAGING_LOOKUP_SQL% to regenerate.
)

REM --- Step 12: Run C# ingredient import ---
echo.
echo [12/12] Running ingredient import into database...
set IMPORT_CONN=Host=localhost;Port=5432;Database=%DB_NAME%;UserID=%DB_USER%;Password=%DB_PASSWORD%

dotnet run --project "%~dp0Nom.Import" -- "--ConnectionStrings:NomConnection=%IMPORT_CONN%" "--ImportSettings:SourceDirectory=%ETL_OUTPUT_DIR%"
if errorlevel 1 (
    echo ERROR: Ingredient import failed.
    exit /b 1
)

REM --- Step 12.5: Import packaging lookup results ---
if exist "%PACKAGING_LOOKUP_SQL%" (
    echo.
    echo [12.5/12] Importing retail packaging lookup results...
    docker cp "%PACKAGING_LOOKUP_SQL%" %POSTGRES_CONTAINER%:/tmp/packaging_lookup.sql
    docker exec %POSTGRES_CONTAINER% psql -U %DB_USER% -d %DB_NAME% -f /tmp/packaging_lookup.sql
    if errorlevel 1 (
        echo WARNING: Packaging SQL import had errors (non-fatal^).
    ) else (
        echo Packaging lookup results imported.
    )
    docker exec %POSTGRES_CONTAINER% rm -f /tmp/packaging_lookup.sql
)

echo.
echo ============================================================
echo  Database reset + migration + ingredient import complete!
echo  You can now run your API project.
echo ============================================================
