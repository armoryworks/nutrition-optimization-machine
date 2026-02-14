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
docker exec %API_CONTAINER% sh -c "cd /src/Nom.Data/Migrations && MIGRATION_FILE=$(ls *_InitialCreate.cs 2>/dev/null | head -1) && if [ -z \"$MIGRATION_FILE\" ]; then echo 'ERROR: Migration file not found'; exit 1; fi && sed -i '1s/^/using Nom.Data;\n/' \"$MIGRATION_FILE\" && sed -i '/protected override void Up(MigrationBuilder migrationBuilder)/,/^        }/ s/^        }/            migrationBuilder.ApplyCustomUpOperations();\n        }/' \"$MIGRATION_FILE\" && sed -i '/protected override void Down(MigrationBuilder migrationBuilder)/{n;s/{/{ \n            migrationBuilder.ApplyCustomDownOperations();/}' \"$MIGRATION_FILE\" && echo 'Patched: '$MIGRATION_FILE"
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

echo.
echo ============================================================
echo  Database and migration reset complete!
echo  You can now run your API project.
echo ============================================================
