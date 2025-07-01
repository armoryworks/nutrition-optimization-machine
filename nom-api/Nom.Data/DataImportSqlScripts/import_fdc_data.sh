#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Configuration ---
# Determine the directory where this script is located.
SCRIPT_DIR=$(dirname "$(readlink -f "$0")")

# Project names (relative to SOLUTION_ROOT, but adjusted here for this script's location)
NOM_API_PROJECT="Nom.Api"
NOM_DATA_PROJECT="Nom.Data"

# Path to appsettings.Development.json, relative to this script
APPSETTINGS_FILE="${SCRIPT_DIR}/../../${NOM_API_PROJECT}/appsettings.Development.json"
CONNECTION_STRING_NAME="NomConnection"

# Directory for your ETL SQL files (this script's directory)
SQL_SCRIPTS_DIR="${SCRIPT_DIR}"

# FDC CSVs path - relative to this script, in a 'Source' subdirectory
FDC_CSV_BASE_PATH="${SCRIPT_DIR}/Source"
FDC_CSV_FILENAMES=("food.csv" "nutrient.csv" "food_nutrient.csv")

# Placeholder used in SQL scripts that will be replaced by the actual FDC_CSV_BASE_PATH
# IMPORTANT: Your SQL files MUST have this placeholder WITHOUT single quotes for \copy command.
# E.g., \copy table FROM /path/to/your/downloaded/files/file.csv ...
CSV_PATH_PLACEHOLDER="/path/to/your/downloaded/files/"

# Order of ETL scripts: __staging_tables.sql MUST be first.
SQL_ETL_SCRIPTS=(
    "__staging_tables.sql"
    "01_fdc_setup_and_nutrients.sql"
    "02_fdc_ingredients.sql"
    "03_fdc_ingredient_nutrients.sql"
)

# --- Functions ---

get_connection_string_value() {
    if ! command -v jq &> /dev/null
    then
        echo "Error: 'jq' is not installed. Please install it (e.g., 'sudo dnf install jq' on Fedora)." >&2
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

parse_connection_string_part() {
    local connection_string="$1"
    local param_pattern="$2"
    
    # Removed DEBUG echo here for brevity
    local value=$(echo "$connection_string" | grep -ioP "${param_pattern}=\K[^;]+" | head -n 1)
    
    # Removed DEBUG echo here for brevity
    echo "$value"
}

check_status() {
    local last_status=$?
    local message="$1"
    if [ $last_status -ne 0 ]; then
        echo "Error: $message failed (exit code: $last_status)." >&2
        exit 1
    fi
}

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
            echo "Could not find all required FDC CSV files in: ${current_path}" >&2
            echo "Missing files: ${missing_files[*]}" >&2
            read -r -p "Please enter the correct directory path where the FDC CSV files are located (or 'q' to quit): " user_input
            
            if [[ "$user_input" == "q" || "$user_input" == "Q" ]]; then
                echo "Operation cancelled by user." >&2
                exit 1
            fi

            if [ ! -d "$user_input" ]; then
                echo "Invalid path: The entered path is not a directory. Please try again." >&2
                current_path="${HOME}"
            else
                current_path="$user_input"
            fi
            echo "Attempting to check: ${current_path}" >&2
        fi
    done
}


# --- Main Script Execution ---

echo "Starting FDC Data Import process..."

# Explicitly set ASPNETCORE_ENVIRONMENT to Development
export ASPNETCORE_ENVIRONMENT=Development

# 1. Extract connection string parameters for the application user (NomUser)
CONNECTION_STRING_VALUE=$(get_connection_string_value "$APPSETTINGS_FILE" "$CONNECTION_STRING_NAME")
check_status "Connection string extraction"

DB_NAME=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Database")
check_status "Database name extraction"

DB_HOST=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Host")
if [ -z "$DB_HOST" ]; then DB_HOST="localhost"; fi

DB_PORT=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Port")
if [ -z "$DB_PORT" ]; then DB_PORT="5432"; fi

DB_USER=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "UserID")
if [ -z "$DB_USER" ]; then
    echo "Error: Database user (UserID) could not be extracted from the connection string: '$CONNECTION_STRING_VALUE'." >&2
    echo "Please ensure your connection string includes 'UserID=your_user'." >&2
    exit 1
fi

DB_PASSWORD=$(parse_connection_string_part "$CONNECTION_STRING_VALUE" "Password")
if [ -z "$DB_PASSWORD" ]; then
    echo "Error: Database password could not be extracted from the connection string: '$CONNECTION_STRING_VALUE'." >&2
    exit 1
fi

# Set PGPASSWORD for the application user
export PGPASSWORD="$DB_PASSWORD"

echo "Identified database name: $DB_NAME"
echo "Identified database host: $DB_HOST"
echo "Identified database port: $DB_PORT"
echo "Identified database application user: $DB_USER"


# 2. Find FDC CSV directory
echo -e "\n--- Locating FDC CSV files ---"
# Start search in the designated FDC_CSV_BASE_PATH (which is now the 'Source' subdirectory)
ACTUAL_FDC_CSV_BASE_PATH=$(find_csv_directory "$FDC_CSV_BASE_PATH" "${FDC_CSV_FILENAMES[@]}")
check_status "FDC CSV directory location"

# Ensure path ends with a single slash
ACTUAL_FDC_CSV_BASE_PATH="${ACTUAL_FDC_CSV_BASE_PATH%/}/"

echo "Using FDC CSVs from: ${ACTUAL_FDC_CSV_BASE_PATH}"

# 3. Concatenate and execute all ETL SQL scripts in a single psql session
echo -e "\n--- Starting FDC Data Import (Consolidated) ---"

# Create a single temporary file for all concatenated SQL
CONSOLIDATED_SQL_FILE=$(mktemp /tmp/consolidated_etl_script_XXXXXX.sql)

# Concatenate all SQL scripts, applying sed substitution to each
for script_name in "${SQL_ETL_SCRIPTS[@]}"; do
    original_script_path="${SQL_SCRIPTS_DIR}/${script_name}"
    if [ ! -f "$original_script_path" ]; then
        echo "WARNING: SQL ETL script not found: $original_script_path. Skipping." >&2
        continue
    fi
    echo "  - Preparing script: $script_name"
    sed "s|${CSV_PATH_PLACEHOLDER}|${ACTUAL_FDC_CSV_BASE_PATH}|g" "$original_script_path" >> "$CONSOLIDATED_SQL_FILE"
    echo -e "\n-- End of ${script_name}\n" >> "$CONSOLIDATED_SQL_FILE" # Add a separator for readability
done

echo "Executing consolidated ETL script..."
# Execute the consolidated SQL file in a single psql session
# Capture all output for conditional display
ETL_OUTPUT=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$CONSOLIDATED_SQL_FILE" 2>&1)
ETL_EXIT_CODE=$?

if [ $ETL_EXIT_CODE -ne 0 ] || echo "$ETL_OUTPUT" | grep -qE "ERROR:|FATAL:|permission denied"; then
    echo "CRITICAL ERROR: Consolidated ETL script failed!" >&2
    echo "Full Output:" >&2
    echo "$ETL_OUTPUT" >&2
    echo "ACTION REQUIRED: Please inspect the full output above for details." >&2
    echo "Ensure all \\copy commands are outside DO $$...$$ blocks and paths are NOT quoted in your SQL files." >&2
    exit 1
else
    echo "Consolidated ETL script executed successfully."
    # Optionally, print a summary or specific success messages from ETL_OUTPUT if desired
    # For now, just print the success message.
fi

rm "$CONSOLIDATED_SQL_FILE"

echo "\nFDC Data Import complete!"
echo "You can now run your API project."
