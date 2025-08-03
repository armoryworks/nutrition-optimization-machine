#!/bin/bash

# Integration Test Runner for NOM Mealie Integration
# This script runs comprehensive integration tests for all Mealie integration features

set -e

echo "🧪 Starting NOM Mealie Integration Tests"
echo "========================================"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if we're in the right directory
if [ ! -f "nom-api.sln" ]; then
    print_error "Please run this script from the nom-api directory"
    exit 1
fi

print_status "Building test project..."
dotnet build Nom.Api.Tests/Nom.Api.Tests.csproj

if [ $? -ne 0 ]; then
    print_error "Failed to build test project"
    exit 1
fi

print_success "Test project built successfully"

print_status "Running Recipe Management Integration Tests..."
dotnet test Nom.Api.Tests/Nom.Api.Tests.csproj --filter "FullyQualifiedName~RecipeManagementIntegrationTests" --verbosity normal

if [ $? -eq 0 ]; then
    print_success "Recipe Management tests passed"
else
    print_error "Recipe Management tests failed"
fi

print_status "Running Household Management Integration Tests..."
dotnet test Nom.Api.Tests/Nom.Api.Tests.csproj --filter "FullyQualifiedName~HouseholdManagementIntegrationTests" --verbosity normal

if [ $? -eq 0 ]; then
    print_success "Household Management tests passed"
else
    print_error "Household Management tests failed"
fi

print_status "Running Shopping List Integration Tests..."
dotnet test Nom.Api.Tests/Nom.Api.Tests.csproj --filter "FullyQualifiedName~ShoppingListIntegrationTests" --verbosity normal

if [ $? -eq 0 ]; then
    print_success "Shopping List tests passed"
else
    print_error "Shopping List tests failed"
fi

print_status "Running All Integration Tests..."
dotnet test Nom.Api.Tests/Nom.Api.Tests.csproj --verbosity normal

if [ $? -eq 0 ]; then
    print_success "All integration tests passed! 🎉"
else
    print_error "Some integration tests failed"
    exit 1
fi

echo ""
echo "📊 Test Summary:"
echo "================="
echo "✅ Recipe Management Integration Tests"
echo "✅ Household Management Integration Tests" 
echo "✅ Shopping List Integration Tests"
echo ""
echo "🎯 Mealie Integration Testing Complete!"
echo ""
echo "Next Steps:"
echo "1. Review any test failures and fix issues"
echo "2. Add more specific test cases as needed"
echo "3. Run performance tests for large datasets"
echo "4. Test authentication and authorization flows" 