# Smoke Testing Documentation

## Overview

Smoke testing is a critical component of the NOM (Nutritional Optimization Machine) quality assurance strategy. This document outlines the comprehensive testing approach that ensures system stability, functionality, and user experience across all major features.

## 🎯 **What is Smoke Testing?**

Smoke testing is a preliminary testing process that verifies the basic functionality of an application works correctly before more detailed testing begins. In the context of NOM, smoke tests serve as:

- **Integration Tests**: Verify that different system components work together correctly
- **Regression Tests**: Ensure that new changes don't break existing functionality
- **User Journey Tests**: Validate complete user workflows from start to finish
- **System Health Checks**: Confirm the application is in a testable state

## 🧪 **Types of Smoke Tests in NOM**

### 1. **Integration Smoke Tests** ✅ IMPLEMENTED

**Purpose**: Verify that multiple system components work together seamlessly

**Current Implementation**: `cypress/e2e/integration-smoke.cy.js`

**Test Coverage**:

- Complete user journey from registration to shopping list generation
- Cross-domain functionality (auth → ingredients → recipes → meal planning → shopping)
- API integration and data flow validation
- End-to-end workflow verification

**Key Test Scenarios**:

```javascript
// Full user journey test
it("should complete full user journey from registration to shopping list generation", () => {
  // 1. User registration and authentication
  // 2. Ingredient creation with diverse nutrients
  // 3. Recipe creation using ingredients
  // 4. Meal plan creation
  // 5. Randomized meal plan generation
  // 6. Shopping list generation from meal plan
  // 7. Verification of meal plan schedule
});
```

### 2. **API Smoke Tests** ✅ IMPLEMENTED

**Purpose**: Verify backend API endpoints are functioning correctly

**Current Implementation**: `cypress/e2e/api.cy.js`

**Test Coverage**:

- Authentication endpoints (register, login, logout)
- User management endpoints
- Recipe management endpoints
- Privacy and consent endpoints
- Error handling and validation

**Key Test Scenarios**:

```javascript
describe("API Tests", () => {
  it("should register a new user via API", () => {
    // User registration validation
  });

  it("should login user via API", () => {
    // Authentication validation
  });

  it("should handle API errors gracefully", () => {
    // Error handling validation
  });
});
```

### 3. **Authentication Smoke Tests** ✅ IMPLEMENTED

**Purpose**: Verify user authentication and authorization flows

**Current Implementation**: `cypress/e2e/auth.cy.js`

**Test Coverage**:

- User registration workflow
- Login/logout functionality
- Password validation
- Session management
- Route protection

### 4. **Onboarding Smoke Tests** ✅ IMPLEMENTED

**Purpose**: Verify user onboarding and profile completion workflows

**Current Implementation**: `cypress/e2e/onboarding.cy.js`

**Test Coverage**:

- Multi-step onboarding process
- Profile data collection
- Dietary restriction setup
- Multi-participant onboarding
- Data validation and submission

### 5. **Recipe Management Smoke Tests** ✅ IMPLEMENTED

**Purpose**: Verify recipe creation, editing, and management functionality

**Current Implementation**: `cypress/e2e/recipes.cy.js`

**Test Coverage**:

- Recipe creation workflow
- Ingredient management
- Recipe editing and versioning
- Curation workflow integration
- Search and filtering

## 🚀 **Running Smoke Tests**

### **Prerequisites**

1. **Backend API Running**: Ensure the .NET API is running on `http://localhost:5000`
2. **Frontend Running**: Ensure Angular app is running on `http://localhost:4200`
3. **Database**: PostgreSQL database with test data available
4. **Dependencies**: Install Cypress dependencies with `npm install`

### **Test Execution Commands**

```bash
# Run all smoke tests
npm run test

# Run specific smoke test categories
npm run test:integration    # Integration smoke tests
npm run test:api           # API smoke tests
npm run test:auth          # Authentication tests
npm run test:onboarding    # Onboarding tests
npm run test:recipes       # Recipe management tests

# Run tests with UI
npm run test:headed

# Run tests in watch mode
npm run test:open
```

### **Environment Configuration**

The tests use environment variables for configuration:

```javascript
// cypress.config.js
env: {
  apiUrl: 'http://localhost:5000', // .NET API server
  testPassword: 'TestPassword123!' // Default test password
}
```

**Custom Environment Variables**:

```bash
# Set custom test password
export CYPRESS_TEST_PASSWORD="CustomTestPassword123!"

# Run with custom environment
CYPRESS_TEST_PASSWORD="CustomPass123!" npm run test:integration
```

## 🔧 **Test Data Management**

### **Data Generation Strategy**

**No Hardcoded Data**: All tests generate unique data for each run:

```javascript
beforeEach(() => {
  // Generate unique test credentials for each test run
  testUser = {
    email: `test-${Date.now()}-${Math.random()
      .toString(36)
      .substring(2, 8)}@example.com`,
    password: Cypress.env("TEST_PASSWORD") || "TestPassword123!",
    fullName: `Test User ${Math.random().toString(36).substring(2, 8)}`,
  };
});
```

**Diverse Test Data**: Tests create realistic, varied test data:

```javascript
const ingredients = [
  {
    name: "Chicken Breast",
    description: "Lean protein source",
    nutrients: [
      { name: "Protein", amount: 31, unit: "g" },
      { name: "Fat", amount: 3.6, unit: "g" },
      { name: "Iron", amount: 1.0, unit: "mg" },
    ],
  },
  // ... more diverse ingredients
];
```

### **Data Cleanup**

**Automatic Cleanup**: Tests clean up after themselves:

```javascript
afterEach(() => {
  // Clean up test data after each test
  if (testShoppingList?.id) {
    cy.apiRequest("DELETE", `/api/shopping/${testShoppingList.id}`);
  }
  // ... cleanup other test data
});
```

**Test Isolation**: Each test runs in isolation with fresh data

## 📊 **Test Coverage and Validation**

### **Integration Test Coverage**

| Component                | Test Coverage | Status                                   |
| ------------------------ | ------------- | ---------------------------------------- |
| User Authentication      | ✅ Complete   | Registration, login, session management  |
| Ingredient Management    | ✅ Complete   | Creation, nutrients, categorization      |
| Recipe Management        | ✅ Complete   | Creation, editing, versioning            |
| Meal Planning            | ✅ Complete   | Creation, randomization, scheduling      |
| Shopping Lists           | ✅ Complete   | Generation, categorization, optimization |
| Cross-Domain Integration | ✅ Complete   | End-to-end workflows                     |

### **Validation Points**

**Data Integrity**:

- Verify created entities have proper IDs and relationships
- Ensure data consistency across related entities
- Validate business rule compliance

**API Response Validation**:

- HTTP status codes (200, 201, 400, 401, 404, 500)
- Response body structure and content
- Error message accuracy and helpfulness

**Business Logic Validation**:

- Meal type constraints in randomization
- Shopping list item merging and categorization
- Recipe ingredient relationships
- User permission enforcement

## 🛠 **Custom Cypress Commands**

### **Authentication Commands**

```javascript
// Register and authenticate user
cy.registerAndAuthenticateUser(userData);

// Login with existing credentials
cy.login(email, password);

// Logout current user
cy.logout();
```

### **Data Creation Commands**

```javascript
// Create diverse ingredients with nutrients
cy.createDiverseIngredients();

// Create recipes using ingredients
cy.createRecipesWithIngredients(ingredients);

// Create meal plan
cy.createMealPlan(recipes);

// Generate randomized meal plan
cy.generateRandomizedMealPlan(mealPlanId, recipes, mealType);

// Generate shopping list from meal plan
cy.generateShoppingListFromMealPlan(mealPlanId);
```

### **Validation Commands**

```javascript
// Verify meal plan schedule
cy.verifyMealPlanSchedule(mealPlanId);

// Check API response structure
cy.validateApiResponse(response, expectedStructure);

// Verify business rule compliance
cy.verifyBusinessRules(entity, ruleSet);
```

## 🔍 **Troubleshooting and Debugging**

### **Common Issues**

1. **API Connection Failures**:

   - Verify backend is running on correct port
   - Check firewall and network settings
   - Validate API endpoint URLs

2. **Database Connection Issues**:

   - Ensure PostgreSQL is running
   - Verify connection string configuration
   - Check database permissions

3. **Test Data Conflicts**:
   - Run `cy.clearTestData()` before tests
   - Ensure unique data generation
   - Check cleanup procedures

### **Debugging Strategies**

**Cypress Debug Mode**:

```bash
# Run tests with debug output
DEBUG=cypress:* npm run test:integration

# Pause on failures
npm run test:integration -- --headed --no-exit
```

**API Request Logging**:

```javascript
// Enable API request logging
Cypress.Commands.add("logApiRequest", (method, endpoint, body) => {
  cy.log(`API ${method}: ${endpoint}`);
  if (body) cy.log("Request Body:", body);
});
```

**Test Data Inspection**:

```javascript
// Log test data for debugging
cy.log("Created Ingredients:", testIngredients);
cy.log("Created Recipes:", testRecipes);
cy.log("Created Meal Plan:", testMealPlan);
```

## 📈 **Performance and Scalability**

### **Test Execution Performance**

**Parallel Execution**: Tests can run in parallel for faster execution:

```bash
# Run tests in parallel (requires Cypress Cloud or similar)
cypress run --parallel --record
```

**Test Data Optimization**: Efficient data creation and cleanup:

```javascript
// Batch API requests where possible
cy.createIngredientsBatch(ingredientList);

// Parallel cleanup operations
cy.cleanupTestData(testData);
```

### **Scalability Considerations**

**Test Data Volume**: Tests handle realistic data volumes:

```javascript
// Create sufficient test data for realistic scenarios
expect(ingredients).to.have.length.at.least(8);
expect(recipes).to.have.length.at.least(4);
expect(meals).to.have.length.at.least(7);
```

**API Load Testing**: Tests validate system performance under load:

```javascript
// Test with multiple concurrent users
cy.createMultipleUsers(userCount);

// Test with large datasets
cy.createLargeRecipeCollection(recipeCount);
```

## 🔒 **Security and Privacy Testing**

### **Authentication Security**

**Token Validation**: Verify JWT token security:

```javascript
it("should reject invalid authentication tokens", () => {
  cy.apiRequest("GET", "/api/recipe/user", null, {
    Authorization: "Bearer invalid-token",
  }).then((response) => {
    expect(response.status).to.eq(401);
  });
});
```

**Permission Enforcement**: Validate user permission checks:

```javascript
it("should enforce user permissions correctly", () => {
  // Test that users can only access their own data
  // Test that admin functions require proper authorization
});
```

### **Data Privacy**

**GDPR Compliance**: Verify privacy feature functionality:

```javascript
it("should handle data subject rights correctly", () => {
  // Test data export functionality
  // Test data deletion requests
  // Test consent management
});
```

## 📋 **Test Maintenance and Updates**

### **Regular Maintenance Tasks**

1. **Update Test Data**: Refresh test data to match current business rules
2. **API Endpoint Updates**: Update tests when API endpoints change
3. **Business Rule Validation**: Ensure tests reflect current business logic
4. **Performance Monitoring**: Track test execution times and optimize

### **Version Compatibility**

**API Versioning**: Tests should work with multiple API versions:

```javascript
// Support multiple API versions
const apiVersion = Cypress.env("API_VERSION") || "v1";
const baseUrl = `${Cypress.env("apiUrl")}/api/${apiVersion}`;
```

**Database Schema Changes**: Tests should adapt to schema updates:

```javascript
// Handle schema changes gracefully
cy.adaptToSchemaVersion(schemaVersion);

// Validate data structure compatibility
cy.validateDataStructure(entity, expectedSchema);
```

## 🎯 **Future Enhancements**

### **Planned Improvements**

1. **Visual Regression Testing**: Add screenshot comparison tests
2. **Performance Testing**: Integrate performance benchmarks
3. **Accessibility Testing**: Add accessibility validation tests
4. **Mobile Testing**: Extend tests to mobile device scenarios
5. **Load Testing**: Add stress and load testing capabilities

### **Integration with CI/CD**

**Automated Testing Pipeline**:

```yaml
# Example GitHub Actions workflow
- name: Run Smoke Tests
  run: |
    cd nom-test
    npm run test:integration
    npm run test:api
    npm run test:auth
```

**Test Result Reporting**: Integrate with test reporting tools:

```bash
# Generate test reports
npm run test:integration -- --reporter mochawesome

# Upload results to test dashboard
npm run test:integration -- --record --key $CYPRESS_RECORD_KEY
```

## 📚 **Additional Resources**

### **Related Documentation**

- **[API Reference](API_REFERENCE.md)** - Backend API documentation
- **[Development Standards](DEVELOPMENT_STANDARDS.md)** - Coding standards and conventions
- **[Functional Requirements](requirements/functional-requirements.md)** - System requirements
- **[Business Rules](requirements/business-rules.md)** - Business logic and rules

### **External Resources**

- **[Cypress Documentation](https://docs.cypress.io/)** - Official Cypress testing framework docs
- **[Testing Best Practices](https://docs.cypress.io/guides/references/best-practices)** - Cypress testing best practices
- **[API Testing Guide](https://docs.cypress.io/guides/end-to-end-testing/testing-strategies)** - API testing strategies

---

## 📝 **Documentation Status**

| Section              | Status      | Last Updated |
| -------------------- | ----------- | ------------ |
| Overview             | ✅ Complete | Current      |
| Types of Smoke Tests | ✅ Complete | Current      |
| Running Tests        | ✅ Complete | Current      |
| Test Data Management | ✅ Complete | Current      |
| Test Coverage        | ✅ Complete | Current      |
| Custom Commands      | ✅ Complete | Current      |
| Troubleshooting      | ✅ Complete | Current      |
| Performance          | ✅ Complete | Current      |
| Security Testing     | ✅ Complete | Current      |
| Maintenance          | ✅ Complete | Current      |
| Future Enhancements  | ✅ Complete | Current      |

---

_Last Updated: January 2025_  
_Version: 1.0_  
_Status: Active Development with Comprehensive Testing Coverage_
