#!/bin/bash

# verify_sql_file_content.sh
# This script reads and prints specific lines from 05_6_recipe_com_split_ingredients.sql
# to help debug persistent syntax errors, highlighting line 67.

# Exit immediately if a command exits with a non-zero status.
set -e

SCRIPT_DIR=$(dirname "$(readlink -f "$0")")
SQL_FILE_NAME="05_6_recipe_com_split_ingredients.sql"
SQL_FILE_PATH="${SCRIPT_DIR}/${SQL_FILE_NAME}"

START_LINE=70 # Start reading from line 60
END_LINE=90   # End reading at line 75
TARGET_LINE=83 # The line we are specifically interested in

echo "Attempting to read file: ${SQL_FILE_PATH}"

if [ ! -f "$SQL_FILE_PATH" ]; then
    echo "Error: File not found at ${SQL_FILE_PATH}" >&2
    exit 1
fi

echo -e "\n--- Content of ${SQL_FILE_PATH} (Lines ${START_LINE}-${END_LINE}) ---"

# Read lines from the SQL file and process them
# Use `sed` to extract the lines, then `awk` to add line numbers and highlight the target line
sed -n "${START_LINE},${END_LINE}p" "$SQL_FILE_PATH" | awk -v start="${START_LINE}" -v target="${TARGET_LINE}" '{
    line_num = NR + start - 1;
    if (line_num == target) {
        print ">>> " line_num ": " $0;
    } else {
        print "    " line_num ": " $0;
    }
}'
echo "--- End Content ---"

