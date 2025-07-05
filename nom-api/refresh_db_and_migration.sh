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

get_db_user_from_string() {
    local connection_string="$1"
    local DB_USER=$(echo "$connection_string" | grep -oP "UserID=\K[^;]+" | head -n 1) # Corrected: Use "UserID" as pattern
    if [ -z "$DB_USER" ]; then
        echo "Error: Could not extract database user (UserID) from connection string: '$connection_string'." >&2
        echo "Please ensure the connection string includes 'UserID=your_user;'." >&2
        return 1
    fi
    echo "$DB_USER"
}

get_db_password_from_string() {
    local connection_string="$1"
    local DB_PASSWORD=$(echo "$connection_string" | grep -oP "Password=\K[^;]+" | head -n 1)
    if [ -z "$DB_PASSWORD" ]; then
        echo "Error: Could not extract database password from connection string: '$connection_string'." >&2
        echo "Please ensure the connection string includes 'Password=your_password;'." >&2
        return 1
    fi
    echo "$DB_PASSWORD"
}

get_db_host_from_string() {
    local connection_string="$1"
    local DB_HOST=$(echo "$connection_string" | grep -oP "Host=\K[^;]+" | head -n 1)
    if [ -z "$DB_HOST" ]; then
        echo "localhost" # Default to localhost if not specified
    else
        echo "$DB_HOST"
    fi
}

get_db_port_from_string() {
    local connection_string="$1"
    local DB_PORT=$(echo "$connection_string" | grep -oP "Port=\K[^;]+" | head -n 1)
    if [ -z "$DB_PORT" ]; then
        echo "5432" # Default to 5432 if not specified
    else
        echo "$DB_PORT"
    fi
}


check_status() {
    local last_status=$?
    local message="$1"
    if [ $last_status -ne 0 ]; then
        echo "Error: $message failed (exit code: $last_status)." >&2
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
check_status "Connection string extraction"

# 2. Extract database name, user, and password from the connection string value
DB_NAME=$(get_db_name_from_string "$CONNECTION_STRING_VALUE")
check_status "Database name extraction"
echo "Identified database name from '$CONNECTION_STRING_NAME': $DB_NAME"

DB_USER=$(get_db_user_from_string "$CONNECTION_STRING_VALUE")
check_status "Database user extraction"

DB_PASSWORD=$(get_db_password_from_string "$CONNECTION_STRING_VALUE")
check_status "Database password extraction"

DB_HOST=$(get_db_host_from_string "$CONNECTION_STRING_VALUE")
check_status "Database host extraction"

DB_PORT=$(get_db_port_from_string "$CONNECTION_STRING_VALUE")
check_status "Database port extraction"


# IMPORTANT: PGPASSWORD for the 'postgres' superuser is now managed via ~/.pgpass.
# Ensure ~/.pgpass exists and has 0600 permissions, with an entry like:
# localhost:5432:postgres:postgres:your_postgres_superuser_password
echo "DEBUG: Relying on ~/.pgpass for postgres user password."


# 3. Drop the database
echo "Attempting to drop database '$DB_NAME'..."

# Temporarily enable shell debugging to see the psql command execution
set -x
# psql will automatically use ~/.pgpass if permissions are correct
psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -d postgres -v ON_ERROR_STOP=1 <<EOF
SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '$DB_NAME' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "$DB_NAME";
EOF
set +x # Disable shell debugging

DB_DROP_EXIT_CODE=$? # Capture exit code of the direct psql command

if [ $DB_DROP_EXIT_CODE -ne 0 ]; then
    echo "CRITICAL ERROR: Database termination and drop failed!" >&2
    echo "ACTION REQUIRED: Ensure user 'postgres' is a SUPERUSER and has correct password/access." >&2
    echo "Verify ~/.pgpass exists with 0600 permissions and contains the correct entry for 'postgres' user." >&2
    echo "If using peer authentication, you might need to run 'sudo -u postgres psql' or adjust pg_hba.conf." >&2
    exit 1
else
    echo "Database '$DB_NAME' dropped (if it existed and was terminated)."
fi

# 4. Create/Ensure Application User and Grant Privileges
echo "Ensuring application user '$DB_USER' exists and has privileges on '$DB_NAME'..."
set -x
psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -d postgres -v ON_ERROR_STOP=1 <<EOF
DO
\$do\$
BEGIN
   IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '$DB_USER') THEN
      CREATE ROLE "$DB_USER" WITH LOGIN PASSWORD '$DB_PASSWORD';
   ELSE
      ALTER ROLE "$DB_USER" WITH PASSWORD '$DB_PASSWORD';
   END IF;
END
\$do\$;
CREATE DATABASE "$DB_NAME" OWNER "$DB_USER";
GRANT ALL PRIVILEGES ON DATABASE "$DB_NAME" TO "$DB_USER";
\q
EOF
set +x

DB_USER_SETUP_EXIT_CODE=$?
if [ $DB_USER_SETUP_EXIT_CODE -ne 0 ]; then
    echo "CRITICAL ERROR: Database user setup or database creation/privilege grant failed!" >&2
    echo "ACTION REQUIRED: Ensure user 'postgres' has CREATE ROLE and CREATE DATABASE privileges." >&2
    echo "Also, verify the application user's password in appsettings.Development.json is correct." >&2
    exit 1
else
    echo "Application user '$DB_USER' ensured and database '$DB_NAME' created with owner '$DB_USER'."
    echo "Privileges granted to '$DB_USER' on '$DB_NAME'."
fi


# 5. Delete the migrations folder
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

# 6. Run dotnet ef migrations add InitialCreate
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


# 7. Modify the InitialCreate.cs
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


# 8. Run dotnet ef database update
echo "Applying database migration..."
# Set PGPASSWORD for the application user for dotnet ef commands
# This password comes from appsettings.Development.json and is extracted by the script.
export PGPASSWORD="$DB_PASSWORD"

# Execute dotnet ef database update directly to stream output
# This will show all verbose output from EF Core directly to the console.
dotnet ef database update --context ApplicationDbContext --project "../${NOM_DATA_PROJECT}" --startup-project . --verbose

DB_UPDATE_EXIT_CODE=$? # Capture exit code of the direct ef command

# Unset PGPASSWORD immediately after use for security
unset PGPASSWORD

if [ $DB_UPDATE_EXIT_CODE -ne 0 ]; then
    echo "CRITICAL ERROR: Applying database migration failed!" >&2
    # The full output from dotnet ef should already be streamed above.
    echo "ACTION REQUIRED: Review the detailed output above for EF Core errors." >&2
    echo "Verify the connection string in appsettings.Development.json for user '$DB_USER' and database '$DB_NAME'." >&2
    echo "Ensure the database user '$DB_USER' has correct permissions on '$DB_NAME'." >&2
    exit 1
else
    echo "Database migration applied successfully."
fi

echo "Database and migration reset complete!"
echo "You can now run your API project."
