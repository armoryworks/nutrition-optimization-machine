#!/bin/bash

# import_recipes.sh
# This script orchestrates the import of recipe data from CSV files
# into the PostgreSQL database, reading connection details from appsettings.Development.json.
# It now utilizes modular, transactional, and resumable SQL scripts for robustness.
# This version adds interactive stage selection with a countdown and an optional global debug limit.

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Debugging: Print SCRIPT_DIR at the very beginning ---
SCRIPT_DIR=$(dirname "$(readlink -f "$0")")
echo "DEBUG: SCRIPT_DIR is: ${SCRIPT_DIR}"
# --- End Debugging ---

# --- Configuration ---
NOM_API_PROJECT="Nom.Api"
APPSETTINGS_FILE="${SCRIPT_DIR}/../../${NOM_API_PROJECT}/appsettings.Development.json"
SQL_SCRIPTS_DIR="${SCRIPT_DIR}" # CORRECTED PATH: Assumes script is run from SQL scripts directory
RECIPE_CSV_BASE_PATH="${SCRIPT_DIR}/Source" # Default source directory for Recipe.csv
RECIPE_CSV_FILENAME="Recipe.csv"
FDC_CSV_DIR_PLACEHOLDER="/path/to/your/downloaded/files/" # Example placeholder in FDC SQLs (if applicable)

# Batch processing configuration
BATCH_SIZE=100000 # Default batch size for normal operation
PROGRESS_FILE="/tmp/recipe_import_progress.log"
STOP_FILE="/tmp/stop_recipe_import.flag"

# --- Global Debug Limit Configuration ---
GLOBAL_DEBUG_RECORD_LIMIT_DEFAULT=10000
GLOBAL_DEBUG_RECORD_LIMIT=0 # Will be set by user input, 0 means no limit
DEBUG_LIMIT_ENABLED=false # Default to false

# --- Define Stages and their mapping ---
# Stage definitions: (name, script_file, total_records_query_stage, [optional] psql_extra_args)
# Note: The order here defines the processing order.
stages_array=(
    "raw_explosion 04_recipe_com_raw_to_temp.sql raw_staging"
    "recipes 05_recipe_com_process_recipes.sql recipes"
    "parse_ingredient_details 05_5_recipe_com_parse_ingredient_details.sql parse_ingredient_details -v client_min_messages=NOTICE"
    "split_ingredients 05_6_recipe_com_split_ingredients.sql split_ingredients"
    "ingredients 06_recipe_com_process_ingredients_fuzzy.sql ingredients"
    "instructions 07_recipe_com_process_instructions.sql instructions"
)

# Create an associative array for easy lookup of stage index by name
declare -A STAGE_INDEX_MAP
for i in "${!stages_array[@]}"; do
    stage_name=$(echo "${stages_array[$i]}" | awk '{print $1}')
    STAGE_INDEX_MAP["$stage_name"]=$i
done

# --- Functions ---

# Function to get a connection string value from appsettings.Development.json using jq
get_connection_string_value() {
    local CONNECTION_STRING_NAME="NomConnection" # Hardcoded as per previous user script
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
    local csv_files=("$2") # This is now an array, expecting RECIPE_CSV_FILENAME

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

    if PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v client_min_messages=NOTICE -f "$temp_sql_file"; then
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
    local psql_extra_args="${5:-}" # Optional: extra psql arguments

    echo "Executing $description ($script_name) with offset $offset_val and limit $limit_val..."

    # --- DIAGNOSTIC STEP: Print the content of the SQL file before execution ---
    echo "--- Content of ${SQL_SCRIPTS_DIR}/${script_name} ---"
    cat "${SQL_SCRIPTS_DIR}/${script_name}"
    echo "--- End Content of ${SQL_SCRIPTS_DIR}/${script_name} ---"
    # --- END DIAGNOSTIC STEP ---

    # Add -v client_min_messages=NOTICE to psql command by default, or use provided extra args
    if PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v _offset="$offset_val" -v _limit="$limit_val" -v client_min_messages=NOTICE ${psql_extra_args} -f "${SQL_SCRIPTS_DIR}/${script_name}"; then
        echo "Successfully executed $script_name."
    else
        echo "ERROR: Failed to execute $script_name. Check the output above for details."
        exit 1
    fi
}

# Function to get counts for different stages
get_count() {
    local stage_name="$1" # e.g., "recipes", "ingredients", "parse_ingredient_details"
    local count_query=""
    local psql_result=""

    case "$stage_name" in
        "raw_staging")
            # For raw_staging, we count all records as the source for explosion
            count_query="SELECT COUNT(*) FROM recipe.recipe_com_raw_staging;"
            ;;
        "raw_explosion")
            # For raw_explosion, we count records in raw_staging that have not yet been exploded
            # This means checking if their corresponding entries exist in recipe_com_recipe_staging
            # (which is populated by the raw_explosion stage).
            count_query="SELECT COUNT(rs.link) FROM recipe.recipe_com_raw_staging rs LEFT JOIN recipe.recipe_com_recipe_staging rcs ON rs.link = rcs.link WHERE rcs.link IS NULL;"
            ;;
        "recipes")
            # Count recipes from recipe_com_recipe_staging that have not been inserted into the final Recipe table
            count_query="SELECT COUNT(rcs.link) FROM recipe.recipe_com_recipe_staging rcs LEFT JOIN recipe.\"Recipe\" r ON rcs.link = r.\"SourceUrl\" WHERE r.\"SourceUrl\" IS NULL;"
            ;;
        "parse_ingredient_details")
            # Count records in raw_ingredients_exploded_staging that haven't been parsed yet
            count_query="SELECT COUNT(*) FROM recipe.recipe_com_raw_ingredients_exploded_staging WHERE cleaned_ingredient_name IS NULL OR cleaned_ingredient_name = '';"
            ;;
        "split_ingredients")
            # Count records in raw_ingredients_exploded_staging that have been parsed (by 05_5)
            # but not yet processed by the splitting stage (i.e., not in final_ingredients_staging)
            count_query="SELECT COUNT(*) FROM recipe.recipe_com_raw_ingredients_exploded_staging rie WHERE rie.cleaned_ingredient_name IS NOT NULL AND rie.cleaned_ingredient_name != '' AND NOT EXISTS (SELECT 1 FROM recipe.recipe_com_final_ingredients_staging f WHERE f.source_link = rie.source_link AND f.original_line_order = rie.line_order);"
            ;;
        "ingredients")
            # Count records in final_ingredients_staging that haven't been processed into RecipeIngredient yet.
            # This is now based on the 'is_processed' flag in recipe.recipe_com_final_ingredients_staging.
            count_query="SELECT COUNT(*) FROM recipe.recipe_com_final_ingredients_staging WHERE is_processed = FALSE;"
            ;;
        "instructions")
            count_query="SELECT COUNT(*) FROM recipe.recipe_com_raw_instructions_exploded_staging rie JOIN recipe.\"Recipe\" r ON rie.source_link = r.\"SourceUrl\" WHERE NOT EXISTS (SELECT 1 FROM recipe.\"RecipeStep\" rs WHERE rs.\"RecipeId\" = r.\"Id\" AND rs.\"StepNumber\" = rie.instruction_step_number);"
            ;;
        *)
            echo "Error: Unknown stage '$stage_name' for total records count." >&2
            return 1
            ;;
    esac

    psql_result=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -A -c "$count_query" 2> /tmp/psql_error.log | tr -d '\n' | xargs)
    local psql_status=$?

    if [ "$psql_status" -ne 0 ]; then
        echo "WARNING: psql command failed for count type '$stage_name'. Check /tmp/psql_error.log for details. Returning 0." >&2
        echo "0"
        return 0
    fi

    if [[ -z "$psql_result" || ! "$psql_result" =~ ^[0-9]+$ ]]; then
        echo "WARNING: Unexpected psql output for count type '$stage_name': '$psql_result'. Returning 0." >&2
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

# Function to check if a stop flag exists
check_stop_flag() {
    if [ -f "$STOP_FILE" ]; then
        echo "INFO: Stop flag '$STOP_FILE' detected. Stopping import process gracefully."
        exit 0
    fi
}


# Function to perform a batch processing stage
process_stage() {
    local stage_name="$1"
    local sql_script_name="$2" # Use script name, not full path here
    local total_records_query_stage="$3" # The stage name to pass to get_count for total records
    local psql_extra_args="${4:-}" # Optional: extra psql arguments for this stage

    local current_offset=$(read_progress "$stage_name")
    local total_records_to_process # This will be the fixed total for recipes, dynamic for others

    # Get the total number of records for this stage
    total_records_to_process=$(get_count "$total_records_query_stage")
    if [ $? -ne 0 ]; then
        echo "ERROR: Could not get total records for stage '$stage_name'. Aborting." >&2
        exit 1
    fi

    echo -e "\n--- Starting $stage_name processing (Script: $sql_script_name) ---"
    echo "Total records to process for $stage_name: $total_records_to_process"
    echo "Starting from offset: $current_offset"

    # If the total records to process is 0, skip the stage
    if [ "$total_records_to_process" -eq 0 ]; then
        echo "No $stage_name records to process. Skipping stage."
        return 0
    fi

    # Crucial: If current_offset is greater than or equal to total_records_to_process,
    # it means we've overshot due to previous partial runs or data changes.
    # Reset offset to 0 and re-process from start for this stage.
    # This ensures we don't miss anything.
    if [ "$current_offset" -ge "$total_records_to_process" ]; then
        echo "INFO: Current offset ($current_offset) is greater than or equal to total records to process ($total_records_to_process)."
        echo "INFO: This stage appears to be complete or overshot. Resetting offset to 0 and re-evaluating."
        current_offset=0
        write_progress "$stage_name" "$current_offset" # Update progress file immediately
        # Re-evaluate total_records_to_process if needed, though for 'raw_staging' it's fixed
        if [ "$total_records_query_stage" != "raw_staging" ]; then
            total_records_to_process=$(get_count "$total_records_query_stage")
            if [ "$total_records_to_process" -eq 0 ]; then
                echo "No $stage_name records to process after reset. Skipping stage."
                return 0
            fi
        fi
        echo "Re-evaluated total records for $stage_name: $total_records_to_process"
    fi

    # Determine the effective total limit for this stage based on GLOBAL_DEBUG_RECORD_LIMIT
    local effective_total_limit=$total_records_to_process
    if [ "$DEBUG_LIMIT_ENABLED" = true ]; then
        # Apply the global limit only to the 'raw_explosion' stage's effective total.
        # Subsequent stages will naturally be limited by the data produced by raw_explosion.
        if [ "$stage_name" == "raw_explosion" ]; then
            effective_total_limit=$GLOBAL_DEBUG_RECORD_LIMIT
        fi
        # Ensure effective_total_limit doesn't exceed the actual total available for any stage
        if (( effective_total_limit > total_records_to_process )); then
            effective_total_limit=$total_records_to_process
        fi
    fi
    echo "Effective total limit for $stage_name: $effective_total_limit"


    while true; do
        check_stop_flag

        # Calculate how many records are still left to process from the effective total limit for this stage
        local remaining_in_this_run=$((effective_total_limit - current_offset))

        # Debugging for recipes stage (or any stage where issues occur)
        if [ "$stage_name" == "recipes" ]; then
            echo "DEBUG: Inside recipes loop - Iteration Start:"
            echo "DEBUG:   current_offset=$current_offset"
            echo "DEBUG:   effective_total_limit=$effective_total_limit"
            echo "DEBUG:   remaining_in_this_run=$remaining_in_this_run"
        fi

        if [ "$remaining_in_this_run" -le 0 ]; then
            echo "All $stage_name records processed up to effective limit."
            break
        fi

        local batch_limit=$BATCH_SIZE
        if [ "$remaining_in_this_run" -lt "$BATCH_SIZE" ]; then
            batch_limit="$remaining_in_this_run"
        fi
        # If the effective total limit is smaller than the batch size, adjust batch_limit for the last batch
        if [ "$DEBUG_LIMIT_ENABLED" = true ] && [ "$stage_name" == "raw_explosion" ] && (( batch_limit > (GLOBAL_DEBUG_RECORD_LIMIT - current_offset) )); then
            batch_limit=$((GLOBAL_DEBUG_RECORD_LIMIT - current_offset))
        fi


        # Debugging for recipes stage
        if [ "$stage_name" == "recipes" ]; then
            echo "DEBUG:   batch_limit=$batch_limit"
            echo "DEBUG: Inside recipes loop - Executing SQL with Offset $current_offset, Limit $batch_limit"
        fi

        # If batch_limit becomes 0 or negative due to limit calculations, break
        if (( batch_limit <= 0 )); then
            echo "Calculated batch_limit is zero or negative ($batch_limit). Breaking loop for $stage_name."
            break
        fi

        echo "Processing $stage_name batch: Offset $current_offset, Limit $batch_limit"
        # Pass extra psql arguments if provided
        execute_sql_script "$sql_script_name" "processing $stage_name" "$current_offset" "$batch_limit" "${psql_extra_args}"
        check_status "Batch processing for $stage_name (Offset: $current_offset)"

        # Update offset for the next batch
        current_offset=$((current_offset + batch_limit))

        # Write progress after each successful batch
        write_progress "$stage_name" "$current_offset"
        echo "Progress for $stage_name saved: $current_offset"

        # For stages that process dynamically decreasing counts (ingredients, instructions, parse_ingredient_details, split_ingredients)
        # This includes 'raw_explosion' which processes raw_staging records into other tables.
        # The total_records_to_process for raw_staging is fixed, so we only re-fetch for dynamic stages.
        # We need to re-fetch 'total_records_to_process' to ensure 'effective_total_limit' is re-evaluated correctly.
        local old_total_records_to_process=$total_records_to_process
        total_records_to_process=$(get_count "$total_records_query_stage")
        echo "DEBUG: New actual total_records_to_process for $stage_name (re-fetched): $total_records_to_process"

        # Re-evaluate effective_total_limit after re-fetching actual total
        effective_total_limit=$total_records_to_process
        if [ "$DEBUG_LIMIT_ENABLED" = true ]; then
            if [ "$stage_name" == "raw_explosion" ]; then
                effective_total_limit=$GLOBAL_DEBUG_RECORD_LIMIT
            fi
            if (( effective_total_limit > total_records_to_process )); then
                effective_total_limit=$total_records_to_process
            fi
        fi
        echo "DEBUG: New effective total limit for $stage_name: $effective_total_limit"

        # The loop termination condition should be based on current_offset reaching effective_total_limit
        # or total_records_to_process becoming 0 (if no more records are left to process at all).
        if [ "$current_offset" -ge "$effective_total_limit" ] || [ "$total_records_to_process" -eq 0 ]; then
             echo "Finished processing all $stage_name records up to effective limit."
             break
        fi
    done
    echo "--- $stage_name processing completed or halted. ---"
}


# --- Main Process ---
echo "Starting Recipe Data Import ---"

# Prompt for debug limit
echo -e "\n--- Debug Limit Option ---"
read -r -p "Enable global debug record limit (e.g., process only first 10000 source recipes)? (y/N): " enable_debug_limit_choice
if [[ "$enable_debug_limit_choice" =~ ^[Yy]$ ]]; then
    DEBUG_LIMIT_ENABLED=true
    read -r -p "Enter total number of source records to process (default: ${GLOBAL_DEBUG_RECORD_LIMIT_DEFAULT}): " user_limit_input
    if [[ "$user_limit_input" =~ ^[0-9]+$ ]] && (( user_limit_input > 0 )); then
        GLOBAL_DEBUG_RECORD_LIMIT=$user_limit_input
    else
        GLOBAL_DEBUG_RECORD_LIMIT=$GLOBAL_DEBUG_RECORD_LIMIT_DEFAULT
    fi
    echo "Global debug limit enabled: Processing first ${GLOBAL_DEBUG_RECORD_LIMIT} source records."
else
    echo "Global debug limit disabled: Processing all records."
fi


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

# 6. Create Recipe Staging Tables (specifically recipe.recipe_com_raw_staging and others)
echo -e "\n--- Creating Recipe Raw Staging Table (__staging_tables_recipe.sql) ---"
execute_sql_script "__staging_tables_recipe.sql" "creating raw recipe staging table"
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
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v client_min_messages=NOTICE -c "\\copy recipe.recipe_com_raw_staging (blank_col, title, ingredients, directions, link, source, ner) FROM '${RAW_CSV_FULL_PATH}' WITH (FORMAT CSV, HEADER TRUE, ENCODING 'UTF8');" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
    check_status "Raw CSV data load"
    echo "Raw CSV data load command executed. Check output above for details."
else
    echo "recipe.recipe_com_raw_staging already populated. Skipping raw data load."
fi

# 8. Add is_processed columns to exploded staging tables (idempotent)
echo "--- Checking and adding 'is_processed' columns to exploded staging tables... ---"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v client_min_messages=NOTICE -c "ALTER TABLE recipe.recipe_com_raw_ingredients_exploded_staging ADD COLUMN IF NOT EXISTS is_processed BOOLEAN DEFAULT FALSE;" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "adding is_processed to raw_ingredients_exploded_staging"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v client_min_messages=NOTICE -c "ALTER TABLE recipe.recipe_com_raw_instructions_exploded_staging ADD COLUMN IF NOT EXISTS is_processed BOOLEAN DEFAULT FALSE;" 2>&1 | tee -a "/tmp/malformed_recipe_lines.log"
check_status "adding is_processed to raw_instructions_exploded_staging"
echo "--- 'is_processed' column checks complete. ---"

# 9. Add parsing columns to raw_ingredients_exploded_staging (before any parsing or counting)
echo -e "\n--- Ensuring Parsing Columns Exist (04_5_recipe_com_add_parsing_columns.sql) ---"
execute_sql_script "04_5_recipe_com_add_parsing_columns.sql" "adding parsing columns"
check_status "adding parsing columns"
echo "Parsing columns check and addition completed."

# --- DIAGNOSTIC STEP: Check for existence of 'regexp_quote_literal' function in the database ---
echo -e "\n--- Checking for existence of 'regexp_quote_literal' function in the database ---"
if PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -A -c "SELECT proname FROM pg_proc WHERE proname = 'regexp_quote_literal';" | grep -q "regexp_quote_literal"; then
    echo "WARNING: Function 'regexp_quote_literal' *still exists* in the database. This is unexpected."
else
    echo "INFO: Function 'regexp_quote_literal' does NOT exist in the database. This is expected."
fi
# --- END DIAGNOSTIC STEP ---


# 10. Define comprehensive parsing functions (run only once)
echo -e "\n--- Defining Comprehensive Parsing Functions (04_6_recipe_com_parsing_functions.sql) ---"
execute_sql_script "04_6_recipe_com_parsing_functions.sql" "defining parsing functions"
check_status "defining parsing functions"
echo "Comprehensive parsing functions defined."

# 11. NEW STEP: Reset cleaned_ingredient_name in raw_ingredients_exploded_staging to force re-parsing by 05_5
echo -e "\n--- Resetting cleaned_ingredient_name in raw_ingredients_exploded_staging to force re-parsing ---"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v client_min_messages=NOTICE -c "UPDATE recipe.recipe_com_raw_ingredients_exploded_staging SET cleaned_ingredient_name = NULL, quantity = NULL, measurement_type_name = NULL, is_processed = FALSE, \"LastModifiedDateTime\" = NOW();"
check_status "resetting raw_ingredients_exploded_staging parsing columns"
echo "Reset complete. All ingredient lines will be re-parsed by 05_5."

# 12. NEW STEP: Reset is_processed in recipe.recipe_com_final_ingredients_staging to force re-processing by 06_recipe_com_process_ingredients_fuzzy
echo -e "\n--- Resetting is_processed in recipe_com_final_ingredients_staging to force re-processing ---"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -v client_min_messages=NOTICE -c "UPDATE recipe.recipe_com_final_ingredients_staging SET is_processed = FALSE, \"LastModifiedDateTime\" = NOW();"
check_status "resetting final_ingredients_staging is_processed column"
echo "Reset complete. All final ingredient lines will be re-processed by the fuzzy matching stage."


# --- Interactive Stage Selection ---
echo -e "\n--- Select Starting Stage ---"
echo "Enter the number or name of the stage to start from, or press Enter for default (Start from beginning)."
echo "Default will start from 'raw_explosion'."
echo ""
echo "Available Stages:"
for i in "${!stages_array[@]}"; do
    stage_name=$(echo "${stages_array[$i]}" | awk '{print $1}')
    echo "  $((i+1)). $stage_name"
done
echo ""

START_STAGE_INDEX=0 # Default to the first stage (raw_explosion)
read -t 10 -p "Starting in 10 seconds... Enter your choice: " user_choice || true # `|| true` prevents `set -e` from exiting on timeout

if [ -z "$user_choice" ]; then
    echo -e "\nNo input. Starting from default stage: raw_explosion"
else
    # Try to match by number
    if [[ "$user_choice" =~ ^[0-9]+$ ]] && (( user_choice >= 1 && user_choice <= ${#stages_array[@]} )); then
        START_STAGE_INDEX=$((user_choice - 1))
        echo "Starting from stage $((START_STAGE_INDEX+1)): $(echo "${stages_array[$START_STAGE_INDEX]}" | awk '{print $1}')"
    # Try to match by name
    elif [[ -n "${STAGE_INDEX_MAP[$user_choice]}" ]]; then
        START_STAGE_INDEX="${STAGE_INDEX_MAP[$user_choice]}"
        echo "Starting from stage $((START_STAGE_INDEX+1)): $user_choice"
    else
        echo "Invalid input '$user_choice'. Starting from default stage: raw_explosion"
        START_STAGE_INDEX=0
    fi
fi

# --- Batch Processing Stages ---
# Loop through stages starting from the determined index
for (( i=START_STAGE_INDEX; i<${#stages_array[@]}; i++ )); do
    stage_info="${stages_array[$i]}"
    read -r stage_name script_file total_records_query_stage psql_extra_args <<< "$stage_info" # Read optional args
    process_stage "$stage_name" "$script_file" "$total_records_query_stage" "$psql_extra_args"
done

echo -e "\n--- Recipe Data Import Process Completed Successfully! ---"

# Unset PGPASSWORD for security
unset PGPASSWORD
