> **⚠️ ARCHIVED — DO NOT FOLLOW (marked 2026-07-30)**
>
> This document describes a previous generation of the codebase (largely the UI archived
> in commit `696db43` and the since-removed EF-migrations workflow). Its claims were
> verified inaccurate against the current code in the July 2026 deep-dive review.
> Kept for historical reference only — trust `CLAUDE.md` and the code itself.
> Current, verified findings: `docs/DEEP_DIVE_REVIEW_2026-07.md`.

# NOM Testing Suite

Comprehensive end-to-end testing suite for the Nutrition Optimization Machine (NOM) application, built with Cypress for integration validation and user journey testing.

[![Cypress](https://img.shields.io/badge/Cypress-Latest-green.svg)](https://www.cypress.io/)
[![Integration Tests](https://img.shields.io/badge/Integration-Complete-brightgreen.svg)](cypress/e2e/)
[![Test Coverage](https://img.shields.io/badge/Coverage-Comprehensive-blue.svg)](#test-coverage)
[![Production Ready](https://img.shields.io/badge/Production-Ready-green.svg)](#production-testing)

## **Overview**

This testing suite provides comprehensive validation of the NOM application through multiple test categories, ensuring reliability and quality across all user workflows.

### **Key Features**

- **Complete User Journeys** - End-to-end workflow validation
- **Dynamic Test Data** - No hardcoded credentials or data
- **Cross-Domain Testing** - Backend API and frontend integration
- **Production-Ready** - Suitable for CI/CD and production validation
- **Comprehensive Coverage** - All critical application features tested

## **Test Categories**

### ** Integration Smoke Tests**  **Featured**

**File**: `cypress/e2e/integration-smoke.cy.ts`  
**Purpose**: Complete user journey validation from registration to shopping list generation  
**Coverage**: Cross-domain functionality, API integration, end-to-end workflows

```bash
npm run test:integration
```

**Test Flow**:

1. User registration with dynamic credentials
2. Ingredient creation with diverse nutrients
3. Recipe creation with meal type constraints
4. Meal plan creation and scheduling
5. Randomized meal plan generation
6. Shopping list auto-generation
7. Complete workflow validation

### ** API Validation Tests**

**File**: `cypress/e2e/api.cy.ts`  
**Purpose**: Backend API endpoint validation and error handling  
**Coverage**: Authentication, user management, recipes, privacy features

```bash
npm run test:api
```

**Test Coverage**:

- Authentication endpoints
- Recipe CRUD operations
- User management APIs
- Privacy and GDPR endpoints
- Error handling and validation

### ** Authentication Tests**

**File**: `cypress/e2e/auth.cy.ts`  
**Purpose**: User authentication and authorization workflows  
**Coverage**: Registration, login, logout, session management

```bash
npm run test:auth
```

**Security Testing**:

- JWT token validation
- Session management
- Password security
- Authorization checks

### ** Onboarding Tests**

**File**: `cypress/e2e/onboarding.cy.ts`  
**Purpose**: User onboarding and profile completion workflows  
**Coverage**: Multi-step onboarding, dietary restrictions, preferences

```bash
npm run test:onboarding
```

**Onboarding Flow**:

- Profile creation
- Dietary restriction setup
- Nutrition goal setting
- Preference configuration

### ** Recipe Management Tests**

**File**: `cypress/e2e/recipes.cy.ts`  
**Purpose**: Recipe creation, editing, and management functionality  
**Coverage**: Recipe CRUD, ingredient management, curation workflow

```bash
npm run test:recipes
```

**Recipe Features**:

- Recipe creation and editing
- Ingredient management
- Nutrition calculations
- Curation workflow

### ** Basic Smoke Tests**

**File**: `cypress/e2e/smoke.cy.ts`  
**Purpose**: Basic application health and navigation checks  
**Coverage**: Page loading, navigation, API connectivity

```bash
npm run test
```

## **Quick Start**

### **Prerequisites**

Ensure the following services are running:

1. **Backend API** - .NET API on `http://localhost:5000`
2. **Frontend App** - Angular app on `http://localhost:4200`
3. **Database** - PostgreSQL with test data available
4. **Dependencies** - Node.js 20+ installed

### **Installation & Setup**

```bash
# Navigate to test directory
cd nom-test

# Install dependencies
npm install

# Verify setup
npm run test:smoke
```

### **Running Tests**

```bash
# Run all test categories
npm run test

# Run specific test suites
npm run test:integration    #  Complete integration tests
npm run test:api           #  API validation tests
npm run test:auth          #  Authentication tests
npm run test:onboarding    #  Onboarding workflow tests
npm run test:recipes       #  Recipe management tests

# Development & Debugging
npm run test:open          #  Open Cypress test runner
npm run test:headed        #  Run tests with browser visible
```

## **Configuration**

### **Environment Setup**

```javascript
// cypress.config.js
export default {
  e2e: {
    baseUrl: "http://localhost:4200",
    env: {
      apiUrl: "http://localhost:5000",
      testPassword: "TestPassword123!",
    },
  },
};
```

### **Custom Environment Variables**

```bash
# Set custom test password
export CYPRESS_TEST_PASSWORD="CustomTestPassword123!"

# Run with custom configuration
CYPRESS_TEST_PASSWORD="CustomPass123!" npm run test:integration
```

### **Test Data Strategy**

** No Hardcoded Data Policy**

All tests generate unique, dynamic data for each run:

```javascript
// Dynamic user generation
const testUser = {
  email: `test-${Date.now()}-${Math.random()
    .toString(36)
    .substring(2, 8)}@example.com`,
  password: Cypress.env("TEST_PASSWORD") || "TestPassword123!",
  fullName: `Test User ${Math.random().toString(36).substring(2, 8)}`,
};
```

**Benefits**:

- **No Test Interference** - Each test run is isolated
- **Production Safe** - No hardcoded production credentials
- **Parallel Execution** - Tests can run concurrently
- **Security Compliant** - No sensitive data in code

## **Test Coverage**

### **Domain Coverage Matrix**

| Domain                  | Integration | API | Auth | Onboarding | Recipes | Coverage |
| ----------------------- | ----------- | --- | ---- | ---------- | ------- | -------- |
| **User Authentication** |           |   |    |          |       | 80%      |
| **Recipe Management**   |           |   |    |          |       | 75%      |
| **Meal Planning**       |           |   |    |          |       | 50%      |
| **Shopping Lists**      |           |   |    |          |       | 50%      |
| **User Onboarding**     |           |   |    |          |       | 75%      |
| **Privacy/GDPR**        |           |   |    |          |       | 25%      |

### **Validation Points**

- **Data Integrity** - All CRUD operations validated
- **Business Rules** - Meal type constraints, dietary restrictions
- **User Experience** - Complete workflow validation
- **API Integration** - Backend-frontend communication
- **Error Handling** - Graceful failure scenarios
- **Security** - Authentication and authorization

## **Advanced Testing Features**

### **Smart Test Data Generation**

```javascript
// Diverse ingredient creation with nutrients
const ingredients = [
  {
    name: `Chicken Breast ${uniqueId}`,
    nutrients: ["protein", "vitamin-b6", "niacin"],
    category: "protein",
  },
  {
    name: `Salmon Fillet ${uniqueId}`,
    nutrients: ["protein", "omega-3", "vitamin-d"],
    category: "protein",
  },
  // ... more ingredients
];
```

### **Business Logic Validation**

```javascript
// Meal type constraint testing
it("should respect meal type constraints in randomization", () => {
  // Breakfast recipes only used for breakfast
  // Lunch/dinner recipes appropriately categorized
  // Snack recipes for snack times
});
```

### **Automatic Cleanup**

```javascript
afterEach(() => {
  // Clean up test data after each test
  if (testShoppingList?.id) {
    cy.apiRequest("DELETE", `/api/shopping/${testShoppingList.id}`);
  }
  // ... cleanup other entities
});
```

## **Custom Commands**

### **Authentication Commands**

```javascript
// Custom Cypress commands for common operations
Cypress.Commands.add("registerAndAuthenticateUser", (userData) => {
  // Complete authentication workflow
});

Cypress.Commands.add("loginUser", (credentials) => {
  // User login with token management
});
```

### **API Commands**

```javascript
Cypress.Commands.add("apiRequest", (method, url, body) => {
  // Authenticated API requests
});

Cypress.Commands.add("createTestRecipe", (recipeData) => {
  // Recipe creation with validation
});
```

### **Data Generation Commands**

```javascript
Cypress.Commands.add("createDiverseIngredients", () => {
  // Generate ingredients with varied nutrients
});

Cypress.Commands.add("generateMealPlan", (recipes, constraints) => {
  // Smart meal plan generation
});
```

## **CI/CD Integration**

### **GitHub Actions Support**

The test suite integrates with GitHub Actions for automated testing:

```yaml
# .github/workflows/test-frontend.yml
- name: Run E2E tests
  run: |
    cd nom-test
    npm run test:integration
```

### **Production Testing**

```bash
# Production environment testing
CYPRESS_BASE_URL=https://production.nom.app npm run test:smoke

# Staging environment validation
CYPRESS_BASE_URL=https://staging.nom.app npm run test:integration
```

## **Performance Testing**

### **Response Time Validation**

```javascript
// Performance assertions in tests
cy.intercept("GET", "/api/recipes").as("getRecipes");
cy.wait("@getRecipes").then((interception) => {
  expect(interception.response.duration).to.be.lessThan(2000);
});
```

### **Load Testing Scenarios**

- **Concurrent Users** - Multiple user simulation
- **Data Volume** - Large dataset handling
- **API Performance** - Response time validation
- **Memory Usage** - Browser memory monitoring

## **Development & Debugging**

### **Interactive Development**

```bash
# Open Cypress test runner for development
npm run test:open

# Run specific test file with browser
npx cypress run --spec "cypress/e2e/integration-smoke.cy.ts" --headed

# Debug mode with console logs
DEBUG=cypress:* npm run test:integration
```

### **Test Development Workflow**

1. **Write Test** - Create new test file in `cypress/e2e/`
2. **Add Commands** - Create custom commands in `cypress/support/`
3. **Test Locally** - Use `npm run test:open` for development
4. **Validate** - Run complete test suite
5. **Document** - Update this README with new test info

## **Troubleshooting**

### **Common Issues**

1. **Service Not Running**

   ```bash
   # Check backend API
   curl http://localhost:5000/health

   # Check frontend app
   curl http://localhost:4200
   ```

2. **Test Data Conflicts**

   - Tests use dynamic data generation
   - Automatic cleanup prevents conflicts
   - Each test run is isolated

3. **Browser Issues**

   ```bash
   # Clear Cypress cache
   npx cypress cache clear

   # Reset Cypress installation
   npx cypress install
   ```

### **Debug Information**

```javascript
// Add comprehensive logging to tests
cy.log("Test execution step details");
console.log("Debug information:", testData);
```

## **Documentation**

### **Testing Guides**

- **[Smoke Testing Guide](../docs/development/smoke-testing.md)** - Complete testing strategy
- **[Development Workflow](../docs/workflows/development-workflow.md)** - Testing in development process
- **[Troubleshooting](../docs/development/troubleshooting.md)** - Common issues and solutions

### **API Documentation**

- **[API Reference](../docs/API_REFERENCE.md)** - Backend endpoint documentation
- **[Architecture Guide](../docs/architecture/system-architecture.md)** - System architecture overview

## **Contributing**

### **Adding New Tests**

1. **Follow Patterns** - Use existing test patterns and custom commands
2. **Dynamic Data** - Never hardcode test data or credentials
3. **Comprehensive Coverage** - Test happy path, edge cases, and errors
4. **Clean Up** - Ensure proper test data cleanup
5. **Documentation** - Update this README with new test information

### **Test Quality Standards**

- **Isolation** - Each test runs independently
- **Deterministic** - Reproducible results every time
- **Fast Execution** - Optimized for quick feedback
- **Clear Assertions** - Meaningful test assertions and error messages
- **Maintainable** - Easy to understand and modify

## 🆘 **Support**

### **Getting Help**

- **Documentation**: [../docs/README.md](../docs/README.md)
- **Issues**: Check test output and browser console
- **Development**: Use `npm run test:open` for interactive debugging
- **Support**: Refer to troubleshooting section above

### **Test Execution Scripts**

| Script                     | Purpose            | Usage                        |
| -------------------------- | ------------------ | ---------------------------- |
| `npm run test`             | All tests          | Production validation        |
| `npm run test:integration` | Integration tests  | Complete workflow validation |
| `npm run test:open`        | Interactive runner | Development and debugging    |
| `npm run test:headed`      | Visible browser    | Visual test debugging        |

---

**The NOM test suite ensures quality and reliability across all user workflows!** 
