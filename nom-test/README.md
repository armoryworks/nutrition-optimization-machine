# NOM Testing Suite

This directory contains the comprehensive testing suite for the NOM (Nutritional Optimization Machine) application, built with Cypress for end-to-end testing and integration validation.

## 🧪 **Test Categories**

### **Integration Smoke Tests** 🆕

- **File**: `cypress/e2e/integration-smoke.cy.js`
- **Purpose**: Complete user journey validation from registration to shopping list generation
- **Coverage**: Cross-domain functionality, API integration, end-to-end workflows
- **Run Command**: `npm run test:integration`

### **API Tests**

- **File**: `cypress/e2e/api.cy.js`
- **Purpose**: Backend API endpoint validation and error handling
- **Coverage**: Authentication, user management, recipes, privacy features
- **Run Command**: `npm run test:api`

### **Authentication Tests**

- **File**: `cypress/e2e/auth.cy.js`
- **Purpose**: User authentication and authorization workflows
- **Coverage**: Registration, login, logout, session management
- **Run Command**: `npm run test:auth`

### **Onboarding Tests**

- **File**: `cypress/e2e/onboarding.cy.js`
- **Purpose**: User onboarding and profile completion workflows
- **Coverage**: Multi-step onboarding, dietary restrictions, multi-participant setup
- **Run Command**: `npm run test:onboarding`

### **Recipe Management Tests**

- **File**: `cypress/e2e/recipes.cy.js`
- **Purpose**: Recipe creation, editing, and management functionality
- **Coverage**: Recipe CRUD, ingredient management, curation workflow
- **Run Command**: `npm run test:recipes`

### **Basic Smoke Tests**

- **File**: `cypress/e2e/smoke.cy.js`
- **Purpose**: Basic application health and navigation checks
- **Coverage**: Page loading, navigation, API connectivity
- **Run Command**: `npm run test`

## 🚀 **Quick Start**

### **Prerequisites**

1. **Backend API**: .NET API running on `http://localhost:5000`
2. **Frontend**: Angular app running on `http://localhost:4200`
3. **Database**: PostgreSQL with test data available
4. **Dependencies**: Install with `npm install`

### **Running Tests**

```bash
# Install dependencies
npm install

# Run all tests
npm run test

# Run specific test categories
npm run test:integration    # Integration smoke tests
npm run test:api           # API validation tests
npm run test:auth          # Authentication tests
npm run test:onboarding    # Onboarding tests
npm run test:recipes       # Recipe management tests

# Run with UI for debugging
npm run test:headed

# Open Cypress test runner
npm run test:open
```

## 🔧 **Configuration**

### **Environment Variables**

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

### **Test Data Management**

**No Hardcoded Data**: All tests generate unique data for each run:

- Random email addresses and user names
- Unique ingredient and recipe combinations
- Diverse nutrient profiles for realistic testing
- Automatic cleanup after each test

**Data Generation Strategy**:

```javascript
beforeEach(() => {
  testUser = {
    email: `test-${Date.now()}-${Math.random()
      .toString(36)
      .substring(2, 8)}@example.com`,
    password: Cypress.env("TEST_PASSWORD") || "TestPassword123!",
    fullName: `Test User ${Math.random().toString(36).substring(2, 8)}`,
  };
});
```

## 📊 **Test Coverage**

### **Integration Test Coverage**

| Component                | Test Coverage | Status                                   |
| ------------------------ | ------------- | ---------------------------------------- |
| User Authentication      | ✅ Complete   | Registration, login, session management  |
| Ingredient Management    | ✅ Complete   | Creation, nutrients, categorization      |
| Recipe Management        | ✅ Complete   | Creation, editing, versioning            |
| Meal Planning            | ✅ Complete   | Creation, randomization, scheduling      |
| Shopping Lists           | ✅ Complete   | Generation, categorization, optimization |
| Cross-Domain Integration | ✅ Complete   | End-to-end workflows                     |

### **Test Scenarios**

**Full User Journey Test**:

1. User registration and authentication
2. Ingredient creation with diverse nutrients
3. Recipe creation using ingredients
4. Meal plan creation
5. Randomized meal plan generation
6. Shopping list generation from meal plan
7. Verification of meal plan schedule

**Meal Type Constraint Testing**:

- Breakfast-specific recipe randomization
- Lunch and dinner appropriate selections
- Snack time constraints
- Meal type validation

**Shopping List Validation**:

- Proper item categorization
- Duplicate item merging
- Quantity calculations
- Recipe source tracking

## 🛠 **Custom Commands**

### **Authentication Commands**

```javascript
cy.registerAndAuthenticateUser(userData);
cy.login(email, password);
cy.logout();
```

### **Data Creation Commands**

```javascript
cy.createDiverseIngredients();
cy.createRecipesWithIngredients(ingredients);
cy.createMealPlan(recipes);
cy.generateRandomizedMealPlan(mealPlanId, recipes, mealType);
cy.generateShoppingListFromMealPlan(mealPlanId);
```

### **Validation Commands**

```javascript
cy.verifyMealPlanSchedule(mealPlanId);
cy.validateApiResponse(response, expectedStructure);
cy.verifyBusinessRules(entity, ruleSet);
```

## 🔍 **Troubleshooting**

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

**Test Data Inspection**:

```javascript
// Log test data for debugging
cy.log("Created Ingredients:", testIngredients);
cy.log("Created Recipes:", testRecipes);
cy.log("Created Meal Plan:", testMealPlan);
```

## 📚 **Documentation**

### **Smoke Testing Guide**

- **File**: `../docs/development/smoke-testing.md`
- **Coverage**: Comprehensive testing strategy, custom commands, troubleshooting
- **Status**: ✅ Complete with full documentation

### **API Reference**

- **File**: `../docs/API_REFERENCE.md`
- **Coverage**: Backend API endpoints, authentication, error handling
- **Status**: ✅ Complete

### **Development Standards**

- **File**: `../docs/development/conventions.md`
- **Coverage**: Coding standards, naming conventions, architectural patterns
- **Status**: ✅ Complete

## 🎯 **Future Enhancements**

### **Planned Improvements**

1. **Visual Regression Testing**: Add screenshot comparison tests
2. **Performance Testing**: Integrate performance benchmarks
3. **Accessibility Testing**: Add accessibility validation tests
4. **Mobile Testing**: Extend tests to mobile device scenarios
5. **Load Testing**: Add stress and load testing capabilities

### **CI/CD Integration**

```yaml
# Example GitHub Actions workflow
- name: Run Smoke Tests
  run: |
    cd nom-test
    npm run test:integration
    npm run test:api
    npm run test:auth
```

## 📋 **Test Maintenance**

### **Regular Tasks**

1. **Update Test Data**: Refresh test data to match current business rules
2. **API Endpoint Updates**: Update tests when API endpoints change
3. **Business Rule Validation**: Ensure tests reflect current business logic
4. **Performance Monitoring**: Track test execution times and optimize

### **Version Compatibility**

- Support multiple API versions
- Handle database schema changes gracefully
- Validate data structure compatibility
- Adapt to business rule updates

---

## 📝 **Project Status**

| Component            | Status      | Coverage                       |
| -------------------- | ----------- | ------------------------------ |
| Integration Tests    | ✅ Complete | Full user journey validation   |
| API Tests            | ✅ Complete | All major endpoints covered    |
| Authentication Tests | ✅ Complete | Full auth workflow coverage    |
| Onboarding Tests     | ✅ Complete | Multi-step process validation  |
| Recipe Tests         | ✅ Complete | CRUD and workflow coverage     |
| Documentation        | ✅ Complete | Comprehensive guides available |

---

_Last Updated: January 2025_  
_Version: 2.0_  
_Status: Active Development with Comprehensive Testing Coverage_
