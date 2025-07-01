#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Configuration ---
# Determine the directory where the script is located.
SOLUTION_ROOT=$(dirname "$(readlink -f "$0")")

NOM_API_PROJECT="Nom.Api" # Name of your API project (startup project for ef commands)
NOM_DATA_PROJECT="Nom.Data" # Name of your Data project (where DbContext and Migrations reside)

NOM_API_DIR="${SOLUTION_ROOT}/${NOM_API_PROJECT}"
NOM_DATA_DIR="${SOLUTION_ROOT}/${NOM_DATA_PROJECT}"
APPSETTINGS_FILE="${NOM_API_DIR}/appsettings.Development.json" # Use appsettings.Development.json
CONNECTION_STRING_NAME="NomConnection" # Your specific connection string name (NomConnection)

# Define the Migrations directory path
MIGRATIONS_DIR="${NOM_DATA_DIR}/Migrations"

# --- Functions ---

get_connection_string_value() {
    # Check if jq is installed
    if ! command -v jq &> /dev/null
    then
        echo "Error: 'jq' is not installed. Please install it (e.g., 'sudo dnf install jq' on Fedora) or use the fallback grep/sed script." >&2
        return 1
    fi

    local CONNECTION_STRING_VALUE
    CONNECTION_STRING_VALUE=$(jq -r ".ConnectionStrings[\"$CONNECTION_STRING_NAME\"]" "$APPSETTINGS_FILE" 2>/dev/null)
    
    if [ -z "$CONNECTION_STRING_VALUE" ] || [ "$CONNECTION_STRING_VALUE" == "null" ]; then
        echo "Error: Could not extract connection string for '$CONNECTION_STRING_NAME' from $APPSETTINGS_FILE using jq." >&2
        echo "Please ensure the connection string is correctly defined in 'ConnectionStrings'." >&2
        return 1
    fi
    echo "$CONNECTION_STRING_VALUE"
}

get_db_name_from_string() {
    local connection_string="$1"
    local DB_NAME=$(echo "$connection_string" | grep -oP "Database=\K[^;]+")
    if [ -z "$DB_NAME" ]; then
        echo "Error: Could not extract database name from connection string: '$connection_string'." >&2
        echo "Please ensure the connection string includes 'Database=YOUR_DB_NAME;' (case-sensitive 'Database')." >&2
        return 1
    fi
    echo "$DB_NAME"
}

check_status() {
    if [ $? -ne 0 ]; then
        echo "Error: $1 failed." >&2
        exit 1
    fi
}

# --- Main Script Execution ---

echo "Starting database and migration reset process..."

# --- Verify the APPSETTINGS_FILE path ---
echo "Verifying appsettings.Development.json path: $APPSETTINGS_FILE"
if [ ! -f "$APPSETTINGS_FILE" ]; then
    echo "ERROR: File NOT FOUND at path: $APPSETTINGS_FILE" >&2
    echo "Please ensure the script is located correctly relative to your project structure," >&2
    echo "or adjust SOLUTION_ROOT, NOM_API_PROJECT, NOM_DATA_PROJECT variables." >&2
    exit 1
fi

# 1. Extract the full connection string value (for DB name only, dotnet ef uses its own config)
CONNECTION_STRING_VALUE=$(get_connection_string_value)
if [ $? -ne 0 ]; then
    echo "Connection string extraction failed. Exiting." >&2
    exit 1
fi

# 2. Extract database name from the connection string value
DB_NAME=$(get_db_name_from_string "$CONNECTION_STRING_VALUE")
if [ $? -ne 0 ]; then
    echo "Database name extraction failed. Exiting." >&2
    exit 1
fi
echo "Identified database name from '$CONNECTION_STRING_NAME': $DB_NAME"

# 3. Drop the database
echo "Attempting to drop database '$DB_NAME'..."
# Capture all output for conditional display
DB_DROP_OUTPUT=$(psql -U postgres -d postgres 2>&1 <<EOF
SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '$DB_NAME' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "$DB_NAME";
EOF
)
DB_DROP_EXIT_CODE=$?

if [ $DB_DROP_EXIT_CODE -ne 0 ] || echo "$DB_DROP_OUTPUT" | grep -qE "ERROR:|FATAL:|permission denied"; then
    echo "CRITICAL ERROR: Database termination and drop failed!" >&2
    echo "Full Output:" >&2
    echo "$DB_DROP_OUTPUT" >&2
    echo "ACTION REQUIRED: Ensure user 'postgres' is a SUPERUSER and has correct password/access." >&2
    exit 1
else
    echo "Database '$DB_NAME' dropped (if it existed and was terminated)."
fi


# 4. Delete the migrations folder
echo "Deleting migrations folder: $MIGRATIONS_DIR"
rm -rf "$MIGRATIONS_DIR"
check_status "Deleting migrations folder"

if [ -d "$MIGRATIONS_DIR" ]; then
    echo "ERROR: Migrations directory '$MIGRATIONS_DIR' still exists after rm -rf. Check permissions or if locked." >&2
    exit 1
else
    echo "Migrations folder confirmed deleted."
fi

mkdir -p "$MIGRATIONS_DIR"
check_status "Recreating migrations folder"
echo "Migrations folder recreated."

# Removed verbose check here, relying on dotnet ef to fail if folder isn't clean.
# if ls -A "$MIGRATIONS_DIR"/*_InitialCreate.cs &>/dev/null; then
#     echo "ERROR: Found existing *_InitialCreate.cs files in $MIGRATIONS_DIR AFTER deletion and recreation." >&2
#     echo "This indicates a serious problem with file deletion. Please check permissions." >&2
#     ls -l "$MIGRATIONS_DIR" >&2
#     exit 1
# else
#     echo "DEBUG: Migrations folder confirmed empty of *_InitialCreate.cs files."
# fi


# Navigate to the API project directory for dotnet ef commands
echo "Navigating to API project directory: $NOM_API_DIR"
cd "$NOM_API_DIR"
check_status "Change directory to NOM_API_DIR"

echo "Running dotnet restore..."
dotnet restore
check_status "dotnet restore"
echo "Running dotnet build..."
dotnet build
check_status "dotnet build"

# 5. Run dotnet ef migrations add InitialCreate
echo "Generating new InitialCreate migration..."
# Capture output for conditional display
MIGRATION_ADD_OUTPUT=$(dotnet ef migrations add InitialCreate --context ApplicationDbContext --project "../${NOM_DATA_PROJECT}" --startup-project . 2>&1)
MIGRATION_ADD_EXIT_CODE=$?

if [ $MIGRATION_ADD_EXIT_CODE -ne 0 ]; then
    echo "CRITICAL ERROR: Generating migration failed!" >&2
    echo "Full Output:" >&2
    echo "$MIGRATION_ADD_OUTPUT" >&2
    exit 1
else
    echo "Migration generated successfully."
fi


# 6. Modify the InitialCreate.cs
echo "Modifying InitialCreate.cs to add custom operations..."
# Get the latest migration file, which should be the newly created InitialCreate
MIGRATION_FILE=$(find "${MIGRATIONS_DIR}" -maxdepth 1 -name "*_InitialCreate.cs" -printf '%T@ %p\n' | sort -nr | head -n 1 | cut -f2- -d' ')

if [ -z "$MIGRATION_FILE" ]; then
    echo "Error: Could not find the generated InitialCreate migration file in ${MIGRATIONS_DIR}." >&2
    exit 1
fi
echo "Found migration file: $MIGRATION_FILE"

# Add 'using Nom.Data;' at the top of the file
sed -i '1s/^/using Nom.Data;\n/' "$MIGRATION_FILE"
check_status "Adding using Nom.Data;"

# Insert ApplyCustomUpOperations() before the closing brace of the Up method
sed -i '/protected override void Up(MigrationBuilder migrationBuilder)/,/^        }/ s/^        }/            migrationBuilder.ApplyCustomUpOperations();\n        }/' "$MIGRATION_FILE"
check_status "Adding ApplyCustomUpOperations()"

# Insert ApplyCustomDownOperations() at the beginning of the Down method's body
sed -i '/protected override void Down(MigrationBuilder migrationBuilder)/{n;s/{/{ \n            migrationBuilder.ApplyCustomDownOperations();/}' "$MIGRATION_FILE"
check_status "Adding ApplyCustomDownOperations()"


# 7. Run dotnet ef database update
echo "Applying database migration..."
# Capture output for conditional display
DB_UPDATE_OUTPUT=$(dotnet ef database update --context ApplicationDbContext --project "../${NOM_DATA_PROJECT}" --startup-project . 2>&1)
DB_UPDATE_EXIT_CODE=$?

if [ $DB_UPDATE_EXIT_CODE -ne 0 ]; then
    echo "CRITICAL ERROR: Applying database migration failed!" >&2
    echo "Full Output:" >&2
    echo "$DB_UPDATE_OUTPUT" >&2
    exit 1
else
    echo "Database migration applied successfully."
fi

echo "Database and migration reset complete!"
echo "You can now run your API project."
