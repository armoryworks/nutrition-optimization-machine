#!/bin/bash

# NOM Integration Test Runner
# This script runs the comprehensive integration smoke test with proper setup

set -e  # Exit on any error

echo "🧪 NOM Integration Test Runner"
echo "================================"

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
if [ ! -f "package.json" ] || [ ! -d "cypress" ]; then
    print_error "This script must be run from the nom-test directory"
    exit 1
fi

# Check if dependencies are installed
if [ ! -d "node_modules" ]; then
    print_status "Installing dependencies..."
    npm install
    if [ $? -ne 0 ]; then
        print_error "Failed to install dependencies"
        exit 1
    fi
    print_success "Dependencies installed successfully"
else
    print_status "Dependencies already installed"
fi

# Check if backend API is accessible
print_status "Checking backend API connectivity..."
if curl -s -f "http://localhost:5000/health" > /dev/null 2>&1; then
    print_success "Backend API is accessible"
elif curl -s -f "http://localhost:5000/api/health" > /dev/null 2>&1; then
    print_success "Backend API is accessible (alternative endpoint)"
else
    print_warning "Backend API is not accessible on localhost:5000"
    print_warning "Make sure the .NET API is running before proceeding"
    read -p "Continue anyway? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_error "Test execution cancelled"
        exit 1
    fi
fi

# Check if frontend is accessible
print_status "Checking frontend connectivity..."
if curl -s -f "http://localhost:4200" > /dev/null 2>&1; then
    print_success "Frontend is accessible"
else
    print_warning "Frontend is not accessible on localhost:4200"
    print_warning "Make sure the Angular app is running before proceeding"
    read -p "Continue anyway? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_error "Test execution cancelled"
        exit 1
    fi
fi

# Set environment variables if not already set
if [ -z "$CYPRESS_TEST_PASSWORD" ]; then
    export CYPRESS_TEST_PASSWORD="TestPassword123!"
    print_status "Set default test password: $CYPRESS_TEST_PASSWORD"
fi

# Display test configuration
echo
print_status "Test Configuration:"
echo "  - Backend API: http://localhost:5000"
echo "  - Frontend: http://localhost:4200"
echo "  - Test Password: $CYPRESS_TEST_PASSWORD"
echo "  - Test File: cypress/e2e/integration-smoke.cy.js"
echo

# Ask for confirmation
read -p "Ready to run integration tests? (Y/n): " -n 1 -r
echo
if [[ $REPLY =~ ^[Nn]$ ]]; then
    print_status "Test execution cancelled"
    exit 0
fi

# Run the integration test
echo
print_status "Starting integration smoke test..."
echo "This test will:"
echo "  1. Register a new test user"
echo "  2. Create diverse ingredients with nutrients"
echo "  3. Create recipes using those ingredients"
echo "  4. Create a meal plan"
echo "  5. Generate a randomized meal plan"
echo "  6. Generate a shopping list from the meal plan"
echo "  7. Verify the meal plan schedule"
echo

# Run the test
if npm run test:integration; then
    echo
    print_success "Integration test completed successfully! 🎉"
    print_success "All user journey steps validated successfully"
else
    echo
    print_error "Integration test failed! ❌"
    print_error "Check the test output above for details"
    exit 1
fi

echo
print_status "Test execution completed"
print_status "Check cypress/screenshots/ for any failure screenshots"
print_status "Check cypress/videos/ for test execution recordings (if enabled)"
