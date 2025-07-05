#!/bin/bash

# import_recipes.sh
# This script orchestrates the import of recipe data from CSV files
# into the PostgreSQL database, reading connection details from appsettings.Development.json.
# It now utilizes modular, transactional, and resumable SQL scripts for robustness.

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Debugging: Print SCRIPT_DIR at the very beginning ---
SCRIPT_DIR=$(dirname "$(readlink -f "$0")")
echo "DEBUG: SCRIPT_DIR is: ${SCRIPT_DIR}"
# --- End Debugging ---

# --- Configuration ---
NOM_API_PROJECT="Nom.Api"
APPSETTINGS_FILE="${SCRIPT_DIR}/../../${NOM_API_PROJECT}/appsettings.Development.json"
CONNECTION_STRING_NAME="NomConnection"
SQL_SCRIPTS_DIR="${SCRIPT_DIR}"
SQL_STAGING_SCRIPT="__staging_tables_recipe.sql"
RECIPE_CSV_BASE_PATH="${SCRIPT_DIR}/Source"
RECIPE_CSV_FILENAME="Recipe.csv"
FDC_CSV_DIR_PLACEHOLDER="/path/to/your/downloaded/files/" # Example placeholder in FDC SQLs

# Batch processing configuration
BATCH_SIZE=100000 # Default batch size, user can override
PROGRESS_FILE="/tmp/recipe_import_progress.log"
STOP_FILE="/tmp/stop_recipe_import.flag"

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
        echo "ERROR: $message failed (exit code: $last_status)." >&2
        exit 1
    fi
}

# Function to find the directory containing required CSV files
find_csv_directory() {
    local current_path="$1"
    local csv_files=("$2")

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
        echo "WARNING: Directory '$dir' is not writable by the current user." >&2
        echo "Duplicate reports will not be exported to this directory. Please adjust permissions if needed." >&2
        return 1
    fi
    return 0
}

# Function to execute SQL scripts with dynamic paths if needed (for FDC data)
execute_sql_script_with_path_sub() {
    local script_name="$1"
    local description="$2"
    local csv_path_for_substitution="$3"

    echo "Executing $description ($script_name)..."
    local temp_sql_file=$(mktemp /tmp/temp_sql_script_XXXXXX.sql)

    sed "s|${FDC_CSV_DIR_PLACEHOLDER}|${csv_path_for_substitution}|g" "${SQL_SCRIPTS_DIR}/${script_name}" > "$temp_sql_file"

    # Removed redirection to /dev/null for debugging
    if psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -f "$temp_sql_file"; then
        echo "Successfully executed $script_name."
    else
        echo "ERROR: Failed to execute $script_name. Check the output above for details."
        rm "$temp_sql_file"
        exit 1
    fi
    rm "$temp_sql_file"
}

# Function to execute basic SQL scripts (now with offset/limit variables)
execute_sql_script() {
    local script_name="$1"
    local description="$2"
    local offset_val=${3:-0} # Default to 0 if not provided
    local limit_val=${4:-0}  # Default to 0 if not provided (0 means no limit for psql -v)

    echo "Executing $description ($script_name) with offset $offset_val and limit $limit_val..."
    if psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v _offset="$offset_val" -v _limit="$limit_val" -f "${SQL_SCRIPTS_DIR}/${script_name}"; then
        echo "Successfully executed $script_name."
    else
        echo "ERROR: Failed to execute $script_name. Check the output above for details."
        exit 1
    fi
}

# Function to get counts for different stages
get_count() {
    local count_type="$1" # "total_staging_recipes", "unprocessed_recipes", "unprocessed_ingredients", "unprocessed_instructions"
    local count_query=""
    local psql_result=""

    case "$count_type" in
        "total_staging_recipes")
            count_query="SELECT COUNT(*) FROM recipe.recipe_com_recipe_staging;"
            ;;
        "unprocessed_recipes")
            count_query="SELECT COUNT(s.link) FROM recipe.recipe_com_recipe_staging s WHERE NOT EXISTS (SELECT 1 FROM recipe.\"Recipe\" r WHERE r.\"SourceUrl\" = s.link);"
            ;;
        "unprocessed_ingredients")
            # Corrected: Use ri."RawLine" for comparison with rie.raw_ingredient_text
            # Assuming rie.raw_ingredient_text is the source for ri."RawLine"
            count_query="SELECT COUNT(rie.source_link) FROM recipe.recipe_com_raw_ingredients_exploded_staging rie JOIN recipe.\"Recipe\" r ON rie.source_link = r.\"SourceUrl\" WHERE NOT EXISTS (SELECT 1 FROM recipe.\"RecipeIngredient\" ri WHERE ri.\"RecipeId\" = r.\"Id\" AND ri.\"RawLine\" = rie.raw_ingredient_text);"
            ;;
        "unprocessed_instructions")
            # Corrected: Use rs."StepNumber" for comparison with rie.instruction_step_number
            count_query="SELECT COUNT(rie.source_link) FROM recipe.recipe_com_raw_instructions_exploded_staging rie JOIN recipe.\"Recipe\" r ON rie.source_link = r.\"SourceUrl\" WHERE NOT EXISTS (SELECT 1 FROM recipe.\"RecipeStep\" rs WHERE rs.\"RecipeId\" = r.\"Id\" AND rs.\"StepNumber\" = rie.instruction_step_number);"
            ;;
        *)
            echo "Error: Unknown count type: $count_type" >&2
            return 1
            ;;
    esac

    psql_result=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -A -c "$count_query" 2> /tmp/psql_error.log | tr -d '\n' | xargs)
    local psql_status=$?

    if [ "$psql_status" -ne 0 ]; then
        echo "WARNING: psql command failed for count type '$count_type'. Check /tmp/psql_error.log for details. Returning 0." >&2
        echo "0"
        return 0
    fi

    if [[ -z "$psql_result" || ! "$psql_result" =~ ^[0-9]+$ ]]; then
        echo "WARNING: Unexpected psql output for count type '$count_type': '$psql_result'. Returning 0." >&2
        echo "0"
    else
        echo "$psql_result"
    fi
}


# Function to read progress from file
read_progress() {
    local stage_name="$1"
    local progress_value="0" # Default to 0

    if [ -f "$PROGRESS_FILE" ]; then
        progress_value=$(grep "^${stage_name}:" "$PROGRESS_FILE" | cut -d':' -f2 | tr -d '\n' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')
    fi

    if [[ -z "$progress_value" || ! "$progress_value" =~ ^[0-9]+$ ]]; then
        echo "0"
    else
        echo "$progress_value"
    fi
}

# Function to write progress to file
write_progress() {
    local stage_name="$1"
    local offset="$2"
    # Remove old entry and add new one
    sed -i "/^${stage_name}:/d" "$PROGRESS_FILE" 2>/dev/null || true
    echo "${stage_name}:${offset}" >> "$PROGRESS_FILE"
}

# Function to perform a batch processing stage
process_stage() {
    local stage_name="$1"
    local sql_script="$2"
    local current_offset=$(read_progress "$stage_name")
    local total_records_to_process # This will be the fixed total for recipes, dynamic for others

    # Determine total_records_to_process based on stage
    if [ "$stage_name" == "recipes" ]; then
        total_records_to_process=$(get_count "total_staging_recipes")
    else
        # For ingredients and instructions, total_records_to_process is the current unprocessed count
        total_records_to_process=$(get_count "unprocessed_${stage_name}")
    fi

    echo -e "\n--- Starting $stage_name processing (Script: $sql_script) ---"
    echo "Total records to process for $stage_name: $total_records_to_process"
    echo "Starting from offset: $current_offset"

    # If the total records to process is 0, skip the stage
    if [ "$total_records_to_process" -eq 0 ]; then
        echo "No $stage_name records to process. Skipping stage."
        return 0
    fi

    # Crucial: If current_offset is greater than total_records_to_process,
    # it means we've overshot due to previous partial runs or data changes.
    # Reset offset to 0 and re-process from start for this stage.
    # This ensures we don't miss anything.
    if [ "$current_offset" -ge "$total_records_to_process" ]; then
        echo "INFO: Current offset ($current_offset) is greater than or equal to total records to process ($total_records_to_process)."
        echo "INFO: This stage appears to be complete or overshot. Resetting offset to 0 and re-evaluating."
        current_offset=0
        write_progress "$stage_name" "$current_offset" # Update progress file immediately
        # Re-evaluate total_records_to_process if needed, though for 'recipes' it's fixed.
        if [ "$stage_name" != "recipes" ]; then
            total_records_to_process=$(get_count "unprocessed_${stage_name}")
            if [ "$total_records_to_process" -eq 0 ]; then
                echo "No $stage_name records to process after reset. Skipping stage."
                return 0
            fi
        fi
    fi


    while true; do
        if [ -f "$STOP_FILE" ]; then
            echo "Stop file detected. Halting $stage_name import."
            break
        fi

        # Calculate how many records are still left to process from the original total
        local remaining_in_this_run=$((total_records_to_process - current_offset))

        # Debugging for recipes stage (or any stage where issues occur)
        if [ "$stage_name" == "recipes" ]; then
            echo "DEBUG: Inside recipes loop - Iteration Start:"
            echo "DEBUG:   current_offset=$current_offset"
            echo "DEBUG:   total_records_to_process=$total_records_to_process (fixed total)"
            echo "DEBUG:   remaining_in_this_run=$remaining_in_this_run"
        fi

        if [ "$remaining_in_this_run" -le 0 ]; then
            echo "All $stage_name records processed."
            break
        fi

        local batch_limit=$BATCH_SIZE
        if [ "$remaining_in_this_run" -lt "$BATCH_SIZE" ]; then
            batch_limit="$remaining_in_this_run"
        fi

        # Debugging for recipes stage
        if [ "$stage_name" == "recipes" ]; then
            echo "DEBUG:   batch_limit=$batch_limit"
            echo "DEBUG: Inside recipes loop - Executing SQL with Offset $current_offset, Limit $batch_limit"
        fi

        echo "Processing $stage_name batch: Offset $current_offset, Limit $batch_limit"
        execute_sql_script "$sql_script" "processing $stage_name" "$current_offset" "$batch_limit"
        check_status "Batch processing for $stage_name (Offset: $current_offset)"

        # Update offset for the next batch
        current_offset=$((current_offset + batch_limit))

        # Write progress after each successful batch
        write_progress "$stage_name" "$current_offset"
        echo "Progress for $stage_name saved: $current_offset / $total_records_to_process"

        # For ingredients and instructions, re-fetch the unprocessed count to see if we're done
        # For recipes, total_records_to_process is fixed, so this re-fetch is not needed for loop control
        if [ "$stage_name" != "recipes" ]; then
            total_records_to_process=$(get_count "unprocessed_${stage_name}")
            echo "DEBUG: New total_records_to_process for $stage_name (re-fetched): $total_records_to_process"
        fi

        # The loop termination condition should be based on current_offset catching up to the fixed total
        # For dynamic stages (ingredients/instructions), it will rely on total_records_to_process becoming 0
        if [ "$current_offset" -ge "$total_records_to_process" ]; then
             echo "Finished processing all $stage_name records."
             break
        fi
    done
    echo "--- $stage_name processing completed or halted. ---"
}


# --- Main Process ---
echo "Starting Recipe Data Import ---"

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
    echo "Error: Database application user (UserId) could not be extracted from the connection string." >&2
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
ACTUAL_RECIPE_CSV_BASE_PATH=$(find_csv_directory "$RECIPE_CSV_BASE_PATH" "$RECIPE_CSV_FILENAME")
check_status "Recipe CSV directory location"

# Ensure path ends with a single slash
ACTUAL_RECIPE_CSV_BASE_PATH="${ACTUAL_RECIPE_CSV_BASE_PATH%/}/"

echo "Using Recipe CSVs from: ${ACTUAL_RECIPE_CSV_BASE_PATH}"

# 3. Check write permissions for output reports (using the determined CSV base path)
echo -e "\n--- Checking Write Permissions for Output Reports ---"
check_write_permissions "$ACTUAL_RECIPE_CSV_BASE_PATH" || true # Continue even if check fails, but log error

# 4. Clear the malformed lines log from previous runs
echo -e "\n--- Clearing old malformed_recipe_lines.log ---"
> "/tmp/malformed_recipe_lines.log" # Truncate the file to zero length
echo "Cleared /tmp/malformed_recipe_lines.log"

# 5. Ensure Schemas Exist (run only once)
echo -e "\n--- Ensuring Schemas Exist ---"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=0 -c "CREATE SCHEMA IF NOT EXISTS recipe; CREATE SCHEMA IF NOT EXISTS nutrient; CREATE SCHEMA IF NOT EXISTS reference; CREATE SCHEMA IF NOT EXISTS audit; CREATE SCHEMA IF NOT EXISTS plan; CREATE SCHEMA IF NOT EXISTS shopping; CREATE SCHEMA IF NOT EXISTS person; CREATE SCHEMA IF NOT EXISTS auth;" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "Schema creation"

# 6. Create Recipe Staging Tables (specifically recipe.recipe_com_raw_staging)
echo -e "\n--- Creating Recipe Raw Staging Table (${SQL_STAGING_SCRIPT}) ---"
execute_sql_script "$SQL_STAGING_SCRIPT" "creating raw recipe staging table"
check_status "creating raw recipe staging table"

# 7. Load Raw Recipe CSV Data into recipe.recipe_com_raw_staging (run only once)
echo -e "\n--- Loading Raw Recipe CSV Data ---"
RAW_CSV_FULL_PATH="${ACTUAL_RECIPE_CSV_BASE_PATH}${RECIPE_CSV_FILENAME}"
echo "Verifying CSV file existence and permissions:"
ls -l "$RAW_CSV_FULL_PATH"
if [ ! -f "$RAW_CSV_FULL_PATH" ]; then
    echo "ERROR: Raw Recipe CSV file not found at: $RAW_CSV_FULL_PATH. Exiting." >&2
    exit 1
fi

# Check if recipe.recipe_com_raw_staging is empty before copying
if [ "$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -A -c "SELECT COUNT(*) FROM recipe.recipe_com_raw_staging;" 2>/dev/null | xargs)" -eq 0 ]; then
    echo "recipe.recipe_com_raw_staging is empty. Populating from $RAW_CSV_FULL_PATH..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -c "\\copy recipe.recipe_com_raw_staging (blank_col, title, ingredients, directions, link, source, ner) FROM '${RAW_CSV_FULL_PATH}' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
    check_status "Raw CSV data load"
    echo "Raw CSV data load command executed. Check output above for details."
else
    echo "recipe.recipe_com_raw_staging already populated. Skipping raw data load."
fi

# 8. Run 04_recipe_com_raw_to_temp.sql (Initial Raw Data Explosion)
echo -e "\n--- Starting Initial Raw Data Explosion (04_recipe_com_raw_to_temp.sql) ---"
execute_sql_script "04_recipe_com_raw_to_temp.sql" "initial raw data explosion"
check_status "initial raw data explosion"
echo "Initial raw data explosion completed."

# 9. Add is_processed columns to exploded staging tables (idempotent)
echo "--- Checking and adding 'is_processed' columns to exploded staging tables... ---"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -c "ALTER TABLE recipe.recipe_com_raw_ingredients_exploded_staging ADD COLUMN IF NOT EXISTS is_processed BOOLEAN DEFAULT FALSE;" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "adding is_processed to raw_ingredients_exploded_staging"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -c "ALTER TABLE recipe.recipe_com_raw_instructions_exploded_staging ADD COLUMN IF NOT EXISTS is_processed BOOLEAN DEFAULT FALSE;" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "adding is_processed to raw_instructions_exploded_staging"
echo "--- 'is_processed' column checks complete. ---"

# --- Batch Processing Stages ---

# Stage 1: Process Recipes (05_recipe_com_process_recipes.sql)
process_stage "recipes" "05_recipe_com_process_recipes.sql"

# Add diagnostic counts here to verify recipe processing
echo -e "\n--- Diagnostic Counts After Recipe Processing Stage ---"
RECIPE_TABLE_COUNT=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -A -c "SELECT COUNT(*) FROM recipe.\"Recipe\";" 2>/dev/null | xargs)
STAGING_RECIPE_COUNT=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -A -c "SELECT COUNT(*) FROM recipe.recipe_com_recipe_staging;" 2>/dev/null | xargs)
echo "Records in recipe.\"Recipe\" (final table): $RECIPE_TABLE_COUNT"
echo "Records in recipe.recipe_com_recipe_staging (source for recipe processing): $STAGING_RECIPE_COUNT"
echo "-----------------------------------------------------"


# Stage 2: Process Ingredients (06_recipe_com_process_ingredients.sql)
process_stage "ingredients" "06_recipe_com_process_ingredients.sql"

# Stage 3: Process Instructions (07_recipe_com_process_instructions.sql)
process_stage "instructions" "07_recipe_com_process_instructions.sql"

echo -e "\n--- Recipe Data Import Process Completed Successfully! ---"

# Unset PGPASSWORD for security
unset PGPASSWORD
