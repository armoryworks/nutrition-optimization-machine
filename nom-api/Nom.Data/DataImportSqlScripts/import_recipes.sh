#!/bin/bash

# import_recipes.sh
# This script orchestrates the import of recipe data from a CSV file
# into the PostgreSQL database, reading connection details from appsettings.Development.json.

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Debugging: Print SCRIPT_DIR at the very beginning ---
SCRIPT_DIR=$(dirname "$(readlink -f "$0")")
echo "DEBUG: SCRIPT_DIR is: ${SCRIPT_DIR}"
# --- End Debugging ---

# --- Configuration ---
# Project names (relative to SOLUTION_ROOT, but adjusted here for this script's location)
NOM_API_PROJECT="Nom.Api"

# Path to appsettings.Development.json, relative to this script
APPSETTINGS_FILE="${SCRIPT_DIR}/../../${NOM_API_PROJECT}/appsettings.Development.json"
CONNECTION_STRING_NAME="NomConnection"

# Directory for your ETL SQL files (this script's directory)
SQL_SCRIPTS_DIR="${SCRIPT_DIR}"

# Specific staging table script for recipes
SQL_STAGING_SCRIPT="__staging_tables_recipe.sql"

# Recipe CSVs path - located directly in the same directory as this script (Source/)
RECIPE_CSV_BASE_PATH="${SCRIPT_DIR}/Source"
RECIPE_CSV_FILENAMES=("Recipe.csv")
RECIPE_CSV_FULL_PATH="${RECIPE_CSV_BASE_PATH%/}/${RECIPE_CSV_FILENAMES[0]}" # Full path to the CSV

# Placeholder used in SQL scripts that will be replaced by the actual RECIPE_CSV_BASE_PATH
# IMPORTANT: Your SQL files MUST have this placeholder WITHOUT single quotes for \copy command.
# E.g., \copy table FROM /path/to/your/downloaded/files/file.csv ...
CSV_PATH_PLACEHOLDER="/path/to/your/downloaded/files/"

# Order of ETL SQL scripts for recipe import (excluding schema creation and raw data load)
SQL_ETL_SCRIPTS=(
    "04_recipe_com_temp_staging.sql"
    "05_recipe_com_transactional.sql"
)

# --- Functions ---

# Function to get a connection string value from appsettings.Development.json using jq
get_connection_string_value() {
    if ! command -v jq &> /dev/null
    then
        echo "Error: 'jq' is not installed. Please install it (e.g., 'sudo dnf install jq' on Fedora, 'brew install jq' on macOS, 'sudo apt-get install jq' on Debian/Ubuntu)." >&2
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

# Function to parse a specific part from a PostgreSQL connection string
parse_connection_string_part() {
    local connection_string="$1"
    local param_pattern="$2"

    local value=$(echo "$connection_string" | grep -ioP "${param_pattern}=\K[^;]+" | head -n 1)
    echo "$value"
}

# Function to check the exit status of the last command
check_status() {
    local last_status=$?
    local message="$1"
    if [ $last_status -ne 0 ]; then
        echo "Error: $message failed (exit code: $last_status)." >&2
        exit 1
    fi
}

# Function to find the directory containing required CSV files
find_csv_directory() {
    local current_path="$1"
    local csv_files=("${@:2}")

    while true; do
        local missing_files=()
        for filename in "${csv_files[@]}"; do
            if [ ! -f "${current_path}/${filename}" ]; then
                missing_files+=("${filename}")
            fi
        done

        if [ ${#missing_files[@]} -eq 0 ]; then
            echo "${current_path}"
            return 0
        else
            echo -e "\n--- CSV File Search ---" >&2
            echo "Could not find all required CSV files in: ${current_path}" >&2
            echo "Missing files: ${missing_files[*]}" >&2
            read -r -p "Please enter the correct directory path where the CSV files are located (or 'q' to quit): " user_input

            if [[ "$user_input" == "q" || "$user_input" == "Q" ]]; then
                echo "Operation cancelled by user." >&2
                exit 1
            fi

            if [ ! -d "$user_input" ]; then
                echo "Invalid path: The entered path is not a directory. Please try again." >&2
                current_path="${HOME}" # Reset to HOME or a known safe path
            else
                current_path="$user_input"
            fi
            echo "Attempting to check: ${current_path}" >&2
        fi
    done
}

# Function to check if a directory is writable by the current user
check_write_permissions() {
    local dir="$1"
    if [ ! -w "$dir" ]; then
        echo "ERROR: Directory '$dir' is not writable by the current user." >&2
        echo "Please ensure you have write permissions to this directory to generate duplicate reports." >&2
        echo "You can try: 'chmod u+w \"$dir\"'" >&2
        # We will not exit here, but the COPY TO commands will fail later.
        return 1
    fi
    return 0
}


# --- Main Process ---
echo "--- Starting Recipe Data Import ---"

# Explicitly set ASPNETCORE_ENVIRONMENT to Development
export ASPNETCORE_ENVIRONMENT=Development

# 1. Extract connection string parameters
CONNECTION_STRING_VALUE=$(get_connection_string_value)
check_status "Connection string extraction"

DB_NAME=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Database")
check_status "Database name extraction"

DB_HOST=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Host")
if [ -z "$DB_HOST" ]; then DB_HOST="localhost"; fi

DB_PORT=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Port")
if [ -z "$DB_PORT" ]; then DB_PORT="5432"; fi

# Use "UserId" as the pattern for extracting the database user
DB_USER=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "UserId")
if [ -z "$DB_USER" ]; then
    echo "Error: Database user (UserId) could not be extracted from the connection string." >&2
    echo "Please ensure your connection string includes 'UserId=your_user'." >&2
    exit 1
fi

DB_PASSWORD=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Password")
if [ -z "$DB_PASSWORD" ]; then
    echo "Error: Database password could not be extracted from the connection string." >&2
    exit 1
fi

# Set PGPASSWORD for the application user
export PGPASSWORD="$DB_PASSWORD"

echo "Identified database name: $DB_NAME"
echo "Identified database host: $DB_HOST"
echo "Identified database port: $DB_PORT"
echo "Identified database user: $DB_USER"

# 2. Find Recipe CSV directory
echo -e "\n--- Locating Recipe CSV files ---"
ACTUAL_RECIPE_CSV_BASE_PATH=$(find_csv_directory "$RECIPE_CSV_BASE_PATH" "${RECIPE_CSV_FILENAMES[@]}")
check_status "Recipe CSV directory location"

# Ensure path ends with a single slash
ACTUAL_RECIPE_CSV_BASE_PATH="${ACTUAL_RECIPE_CSV_BASE_PATH%/}/"

echo "Using Recipe CSVs from: ${ACTUAL_RECIPE_CSV_BASE_PATH}"

# 3. Check write permissions for output reports
echo -e "\n--- Checking Write Permissions for Output Reports ---"
check_write_permissions "$ACTUAL_RECIPE_CSV_BASE_PATH" || true # Continue even if check fails, but log error

# 4. Clear the malformed lines log from previous runs
echo -e "\n--- Clearing old malformed_recipe_lines.log ---"
> "/tmp/malformed_recipe_lines.log" # Truncate the file to zero length
echo "Cleared /tmp/malformed_recipe_lines.log"


# 5. Ensure Schemas Exist (for both EF-managed and staging tables)
echo -e "\n--- Ensuring Schemas Exist ---"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -c "CREATE SCHEMA IF NOT EXISTS reference; CREATE SCHEMA IF NOT EXISTS recipe; CREATE SCHEMA IF NOT EXISTS nutrition; CREATE SCHEMA IF NOT EXISTS identity;" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "Schema existence check"


# 6. Create Recipe Staging Tables (__staging_tables_recipe.sql)
echo -e "\n--- Creating Recipe Staging Tables ---"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -f "${SQL_SCRIPTS_DIR}/${SQL_STAGING_SCRIPT}" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "Recipe staging table creation"


# 7. Load Raw Recipe CSV Data (using \copy directly in psql -c)
# This uses the client-side \copy, which does not require server file permissions.
echo -e "\n--- Loading Raw Recipe CSV Data ---"
echo "Verifying CSV file existence and permissions:"
ls -l "${RECIPE_CSV_FULL_PATH}" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log" # Add ls -l for debugging

echo "Executing \copy command for raw data load..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -c "\\copy recipe.recipe_com_raw_staging (blank_col, title, ingredients, directions, link, source, ner) FROM '${RECIPE_CSV_FULL_PATH}' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8', DELIMITER ',');" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "Raw CSV data loading"
echo "Raw CSV data load command executed. Check output above for details."


# 8. Execute remaining ETL SQL scripts (Consolidated psql call)
echo -e "\n--- Starting Recipe Data Processing (Consolidated) ---"

CONSOLIDATED_SQL_FILE=$(mktemp /tmp/consolidated_etl_processing_XXXXXX.sql)

# Add initial setup commands to the consolidated file
echo "SET client_min_messages TO WARNING;" >> "$CONSOLIDATED_SQL_FILE"
echo "SET search_path TO public, recipe, reference, nutrition, identity;" >> "$CONSOLIDATED_SQL_FILE"
echo "BEGIN;" >> "$CONSOLIDATED_SQL_FILE" # Start a transaction

# Concatenate remaining SQL scripts, applying sed substitution to each
for script_name in "${SQL_ETL_SCRIPTS[@]}"; do
    original_script_path="${SQL_SCRIPTS_DIR}/${script_name}"
    if [ ! -f "$original_script_path" ]; then
        echo "WARNING: SQL ETL script not found: $original_script_path. Skipping." >&2
        continue
    fi
    echo "  - Preparing script: $script_name"

    # For these scripts, substitute the CSV_PATH_PLACEHOLDER for output reports
    sed "s|${CSV_PATH_PLACEHOLDER}|${ACTUAL_RECIPE_CSV_BASE_PATH}|g" "$original_script_path" >> "$CONSOLIDATED_SQL_FILE"
    echo -e "\n-- End of ${script_name}\n" >> "$CONSOLIDATED_SQL_FILE" # Add a separator for readability
done

echo "COMMIT;" >> "$CONSOLIDATED_SQL_FILE" # Commit the transaction

echo "Executing consolidated processing script..."
# Execute the consolidated script. Output is tee'd to the log file.
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -f "$CONSOLIDATED_SQL_FILE" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
ETL_EXIT_CODE=${PIPESTATUS[0]} # Get the exit code of psql, not tee

if [ $ETL_EXIT_CODE -ne 0 ] || grep -qE "ERROR:|FATAL:|permission denied" "/tmp/malformed_recipe_lines.log"; then
    echo "CRITICAL ERROR: Consolidated ETL processing failed!" >&2
    echo "Full Output (from /tmp/malformed_recipe_lines.log):" >&2
    cat "/tmp/malformed_recipe_lines.log" >&2
    echo "ACTION REQUIRED: Please inspect the full output above for details." >&2
    echo "Ensure all \\copy commands in SQL are for output (TO) and paths are NOT quoted." >&2
    exit 1
else
    echo "Consolidated ETL processing executed successfully."
fi

# Clean up the consolidated temporary script
rm "$CONSOLIDATED_SQL_FILE"

echo "\n--- Recipe Data Import Completed ---"
echo "Check for duplicate reports in: ${ACTUAL_RECIPE_CSV_BASE_PATH}"
