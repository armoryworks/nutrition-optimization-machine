# NOM Test Suite

This directory contains the Cypress test suite for the Nutrition Optimization Machine (NOM) application. The tests cover both frontend (Angular) and backend (.NET API) functionality.

## Structure

```
nom-test/
├── cypress/
│   ├── e2e/                    # End-to-end tests
│   │   ├── auth.cy.js         # Authentication tests
│   │   ├── onboarding.cy.js   # Onboarding workflow tests
│   │   ├── recipes.cy.js      # Recipe management tests
│   │   └── api.cy.js          # API endpoint tests
│   ├── fixtures/               # Test data
│   │   └── users.json         # User test data
│   └── support/                # Support files
│       ├── e2e.js             # E2E support and custom commands
│       └── commands.js        # Additional utility commands
├── cypress.config.js           # Cypress configuration
└── package.json               # Dependencies and scripts
```

## Prerequisites

Before running the tests, ensure both the frontend and backend servers are running:

### Frontend (Angular)

```bash
cd ../nom-ui
npm start
```

The Angular app should be running on `http://localhost:4200`

### Backend (.NET API)

```bash
cd ../nom-api
dotnet run
```

The API should be running on `http://localhost:5000`

## Installation

```bash
npm install
```

## Running Tests

### All Tests

```bash
npm test
```

### Open Cypress Test Runner

```bash
npm run test:open
```

### Specific Test Suites

```bash
# Authentication tests
npm run test:auth

# Onboarding tests
npm run test:onboarding

# Recipe management tests
npm run test:recipes

# API tests
npm run test:api
```

### Other Options

```bash
# Run tests with browser visible
npm run test:headed

# Run only E2E tests
npm run test:e2e

# Run only component tests
npm run test:component
```

## Test Categories

### 1. Authentication Tests (`auth.cy.js`)

- User registration
- User login/logout
- Form validation
- Auto-login after registration
- Error handling

### 2. Onboarding Tests (`onboarding.cy.js`)

- Multi-step onboarding workflow
- Data persistence between steps
- Form validation
- Multi-participant onboarding
- Completion flow

### 3. Recipe Management Tests (`recipes.cy.js`)

- Recipe creation
- Recipe editing
- Recipe listing and filtering
- Ingredient management
- Curation workflow

### 4. API Tests (`api.cy.js`)

- Authentication API endpoints
- User management API
- Recipe API endpoints
- Privacy API endpoints
- Error handling

## Custom Commands

The test suite includes several custom Cypress commands:

### Authentication

- `cy.login(email, password)` - Login with credentials
- `cy.register(email, password, fullName)` - Register new user
- `cy.logout()` - Logout current user

### API Testing

- `cy.apiRequest(method, endpoint, body, headers)` - Make API requests
- `cy.clearTestData()` - Clear test data

### Form Handling

- `cy.fillForm(formData)` - Fill form fields
- `cy.submitForm()` - Submit form
- `cy.checkSuccessMessage(message)` - Check for success message
- `cy.checkErrorMessage(message)` - Check for error message

### Navigation

- `cy.navigateTo(page)` - Navigate to specific page
- `cy.waitForPageLoad()` - Wait for page to load
- `cy.waitForLoading()` - Wait for loading to complete

## Configuration

The Cypress configuration (`cypress.config.js`) includes:

- **Base URL**: `http://localhost:4200` (Angular dev server)
- **API URL**: `http://localhost:5000` (via environment variable)
- **Viewport**: 1280x720
- **Timeouts**: 10 seconds for commands and requests
- **Video**: Disabled for faster runs
- **Screenshots**: Enabled on failure

## Test Data

Test data is stored in `cypress/fixtures/users.json` and includes:

- Valid test users
- Admin users
- Invalid test data

## Data Attributes

The tests use `data-cy` attributes for element selection. Ensure your Angular components include these attributes:

```html
<!-- Example data-cy attributes -->
<input data-cy="email-input" type="email" />
<button data-cy="login-button">Login</button>
<div data-cy="success-message">Success!</div>
```

## Environment Variables

Set these environment variables for different environments:

```bash
# Development
CYPRESS_BASE_URL=http://localhost:4200
CYPRESS_API_URL=http://localhost:5000

# Staging
CYPRESS_BASE_URL=https://staging.nom-app.com
CYPRESS_API_URL=https://staging-api.nom-app.com

# Production
CYPRESS_BASE_URL=https://nom-app.com
CYPRESS_API_URL=https://api.nom-app.com
```

## Continuous Integration

The test suite is designed to work with CI/CD pipelines. Key considerations:

1. **Database Setup**: Tests use `cy.clearTestData()` to ensure clean state
2. **Parallel Execution**: Tests are designed to run independently
3. **Headless Mode**: Tests run in headless mode by default
4. **Screenshots**: Failed tests automatically capture screenshots

## Troubleshooting

### Common Issues

1. **Tests failing due to timing**: Increase timeouts in `cypress.config.js`
2. **API connection errors**: Ensure backend server is running
3. **Frontend not loading**: Ensure Angular dev server is running
4. **Database issues**: Check that test data cleanup is working

### Debug Mode

Run tests with browser visible for debugging:

```bash
npm run test:headed
```

### Debugging API Calls

Add logging to custom commands in `cypress/support/e2e.js`:

```javascript
cy.apiRequest("GET", "/api/test").then((response) => {
  console.log("API Response:", response);
});
```

## Contributing

When adding new tests:

1. Follow the existing naming conventions
2. Use appropriate `data-cy` attributes
3. Include both positive and negative test cases
4. Add custom commands for reusable functionality
5. Update this README with new test categories

## Best Practices

1. **Isolation**: Each test should be independent
2. **Cleanup**: Always clean up test data
3. **Selectors**: Use `data-cy` attributes for reliable element selection
4. **Assertions**: Include meaningful assertions
5. **Error Handling**: Test both success and failure scenarios
