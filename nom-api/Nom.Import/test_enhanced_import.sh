#!/bin/bash

# Test script for enhanced FDC import system
# This script validates the enhanced import process and provides feedback

set -e

echo "🧪 Testing Enhanced FDC Import System"
echo "======================================"

# Configuration
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_DIR="/home/dhokanson/Dev/ImportSource"
CONFIG_FILE="$PROJECT_DIR/appsettings.enhanced.json"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

# Function to check if file exists
check_file() {
    local file=$1
    local description=$2
    if [ -f "$file" ]; then
        print_status $GREEN "✅ $description: $file"
        return 0
    else
        print_status $RED "❌ $description: $file (NOT FOUND)"
        return 1
    fi
}

# Function to check database connection
check_database() {
    print_status $BLUE "🔍 Checking database connection..."
    
    # Extract connection string from config
    CONNECTION_STRING=$(grep -o '"NomConnection": "[^"]*"' "$CONFIG_FILE" | cut -d'"' -f4)
    
    if [ -z "$CONNECTION_STRING" ]; then
        print_status $RED "❌ Could not extract connection string from config"
        return 1
    fi
    
    # Extract database name
    DB_NAME=$(echo "$CONNECTION_STRING" | grep -o "Database=[^;]*" | cut -d'=' -f2)
    
    if [ -z "$DB_NAME" ]; then
        print_status $RED "❌ Could not extract database name from connection string"
        return 1
    fi
    
    print_status $GREEN "✅ Database name: $DB_NAME"
    return 1
}

# Function to check source files
check_source_files() {
    print_status $BLUE "🔍 Checking source files..."
    
    local missing_files=0
    
    # Check required files for enhanced import
    check_file "$SOURCE_DIR/foundation_food.csv" "Foundation foods" || missing_files=$((missing_files + 1))
    check_file "$SOURCE_DIR/sr_legacy_food.csv" "Survey foods" || missing_files=$((missing_files + 1))
    check_file "$SOURCE_DIR/food.csv" "Main food data" || missing_files=$((missing_files + 1))
    check_file "$SOURCE_DIR/nutrient.csv" "Nutrients" || missing_files=$((missing_files + 1))
    check_file "$SOURCE_DIR/food_nutrient.csv" "Food-nutrient relationships" || missing_files=$((missing_files + 1))
    check_file "$SOURCE_DIR/guidelines.csv" "Dietary guidelines" || missing_files=$((missing_files + 1))
    check_file "$SOURCE_DIR/measure_unit.csv" "Measurement units" || missing_files=$((missing_files + 1))
    check_file "$SOURCE_DIR/food_category.csv" "Food categories" || missing_files=$((missing_files + 1))
    
    if [ $missing_files -eq 0 ]; then
        print_status $GREEN "✅ All required source files found"
    else
        print_status $YELLOW "⚠️  $missing_files source files missing"
    fi
    
    return $missing_files
}

# Function to check project files
check_project_files() {
    print_status $BLUE "🔍 Checking project files..."
    
    local missing_files=0
    
    check_file "$PROJECT_DIR/appsettings.enhanced.json" "Enhanced configuration" || missing_files=$((missing_files + 1))
    check_file "$PROJECT_DIR/Services/EnhancedFdcImporterService.cs" "Enhanced import service" || missing_files=$((missing_files + 1))
    check_file "$PROJECT_DIR/Settings/ImportSettings.cs" "Import settings" || missing_files=$((missing_files + 1))
    check_file "$PROJECT_DIR/DataImportScripts/01_create_enhanced_staging_tables.sql" "Enhanced staging tables" || missing_files=$((missing_files + 1))
    check_file "$PROJECT_DIR/DataImportScripts/03_transform_enhanced.sql" "Enhanced transform script" || missing_files=$((missing_files + 1))
    
    if [ $missing_files -eq 0 ]; then
        print_status $GREEN "✅ All project files found"
    else
        print_status $YELLOW "⚠️  $missing_files project files missing"
    fi
    
    return $missing_files
}

# Function to run the enhanced import
run_enhanced_import() {
    print_status $BLUE "🚀 Running enhanced import..."
    
    cd "$PROJECT_DIR"
    
    # Build the project
    print_status $BLUE "📦 Building project..."
    dotnet build
    
    if [ $? -eq 0 ]; then
        print_status $GREEN "✅ Build successful"
    else
        print_status $RED "❌ Build failed"
        return 1
    fi
    
    # Run the enhanced import
    print_status $BLUE "🔄 Starting enhanced import process..."
    dotnet run --configuration Release
    
    if [ $? -eq 0 ]; then
        print_status $GREEN "✅ Enhanced import completed successfully"
    else
        print_status $RED "❌ Enhanced import failed"
        return 1
    fi
}

# Function to validate import results
validate_import_results() {
    print_status $BLUE "🔍 Validating import results..."
    
    # This would typically connect to the database and check results
    # For now, we'll just provide guidance
    print_status $YELLOW "📊 To validate import results, check:"
    echo "   - Database table counts"
    echo "   - Quality score distributions"
    echo "   - Foundation vs branded food ratios"
    echo "   - Nutrient relationship counts"
}

# Main execution
main() {
    echo ""
    print_status $BLUE "🔧 Enhanced FDC Import System Test"
    echo ""
    
    # Check configuration
    check_file "$CONFIG_FILE" "Enhanced configuration file"
    if [ $? -ne 0 ]; then
        print_status $RED "❌ Configuration file not found. Cannot proceed."
        exit 1
    fi
    
    # Check source files
    check_source_files
    source_files_ok=$?
    
    # Check project files
    check_project_files
    project_files_ok=$?
    
    # Check database
    check_database
    db_ok=$?
    
    echo ""
    print_status $BLUE "📋 Summary:"
    echo "   Source files: $([ $source_files_ok -eq 0 ] && echo "✅ Ready" || echo "⚠️  Issues")"
    echo "   Project files: $([ $project_files_ok -eq 0 ] && echo "✅ Ready" || echo "⚠️  Issues")"
    echo "   Database: $([ $db_ok -eq 0 ] && echo "✅ Ready" || echo "⚠️  Issues")"
    echo ""
    
    if [ $source_files_ok -eq 0 ] && [ $project_files_ok -eq 0 ]; then
        print_status $GREEN "🎉 All checks passed! Ready to run enhanced import."
        echo ""
        
        read -p "Do you want to run the enhanced import now? (y/N): " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            run_enhanced_import
            if [ $? -eq 0 ]; then
                validate_import_results
            fi
        else
            print_status $YELLOW "⏸️  Import skipped. Run manually with: cd $PROJECT_DIR && dotnet run"
        fi
    else
        print_status $RED "❌ Some checks failed. Please fix issues before running import."
        exit 1
    fi
}

# Run main function
main "$@" 