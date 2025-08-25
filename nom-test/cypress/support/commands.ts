// ***********************************************
// This example commands.ts shows you how to
// create various custom commands and overwrite
// existing commands.
//
// For more comprehensive examples of custom
// commands please read more here:
// https://on.cypress.io/custom-commands
// ***********************************************

// Custom commands for NOM application
Cypress.Commands.add('login', (email = 'test@example.com', password = 'TestPassword123!') => {
    cy.visit('/login');
    cy.get('[data-cy=email-input]').type(email);
    cy.get('[data-cy=password-input]').type(password);
    cy.get('[data-cy=login-button]').click();
    cy.url().should('not.include', '/login');
});

Cypress.Commands.add('register', (email = 'test@example.com', password = 'TestPassword123!', fullName = 'Test User') => {
    cy.visit('/register');
    cy.get('[data-cy=email-input]').type(email);
    cy.get('[data-cy=full-name-input]').type(fullName);
    cy.get('[data-cy=password-input]').type(password);
    cy.get('[data-cy=confirm-password-input]').type(password);
    cy.get('[data-cy=register-button]').click();
});

Cypress.Commands.add('apiRequest', (method: string, endpoint: string, body: any = null, headers: any = null) => {
    const apiUrl = Cypress.env('apiUrl');
    const options: any = {
        method,
        url: `${apiUrl}${endpoint}`,
        headers: {
            'Content-Type': 'application/json',
            ...headers
        },
        failOnStatusCode: false,
    };

    if (body) {
        options.body = body;
    }

    return cy.request(options);
});

Cypress.Commands.add('clearTestData', () => {
    // Clear any test data that might interfere with tests
    // This is optional and can be skipped if API is not available
    try {
        cy.apiRequest('POST', '/api/test/clear-data');
    } catch (error) {
        // Silently continue if API is not available
        cy.log('API not available, skipping test data cleanup');
    }
});

Cypress.Commands.add('waitForPageLoad', () => {
    cy.get('body').should('not.have.class', 'loading');
});

// Custom command to wait for Angular to be ready
Cypress.Commands.add('waitForAngular', () => {
    cy.window().then((win) => {
        // Wait for Angular to be ready
        cy.wait(1000); // Basic wait, can be enhanced with Angular-specific checks
    });
});

// Custom command to check if user is logged in
Cypress.Commands.add('isLoggedIn', () => {
    cy.window().then((win) => {
        // Check if user is logged in by looking for auth-related elements
        return cy.get('body').then(($body) => {
            return !$body.find('[data-cy=login-button]').length;
        });
    });
});

// Custom command to logout
Cypress.Commands.add('logout', () => {
    cy.get('[data-cy=user-menu]').click();
    cy.get('[data-cy=logout-button]').click();
    cy.url().should('include', '/login');
});

// Custom command to navigate to a specific page
Cypress.Commands.add('navigateTo', (page: string) => {
    cy.visit(`/${page}`);
    cy.waitForAngular();
});

// Custom command to fill a form
Cypress.Commands.add('fillForm', (formData: Record<string, string>) => {
    Object.keys(formData).forEach((field) => {
        cy.get(`[data-cy=${field}]`).type(formData[field]);
    });
});

// Custom command to submit a form
Cypress.Commands.add('submitForm', () => {
    cy.get('[data-cy=submit-button]').click();
});

// Custom command to check for success message
Cypress.Commands.add('checkSuccessMessage', (message: string) => {
    cy.get('[data-cy=success-message]').should('contain', message);
});

// Custom command to check for error message
Cypress.Commands.add('checkErrorMessage', (message: string) => {
    cy.get('[data-cy=error-message]').should('contain', message);
});

// Custom command to wait for loading to complete
Cypress.Commands.add('waitForLoading', () => {
    cy.get('[data-cy=loading-spinner]').should('not.exist');
});

// Custom command to check if element is visible
Cypress.Commands.add('shouldBeVisible', (selector: string) => {
    cy.get(selector).should('be.visible');
});

// Custom command to check if element is not visible
Cypress.Commands.add('shouldNotBeVisible', (selector: string) => {
    cy.get(selector).should('not.be.visible');
});

// Utility function to generate random email
Cypress.Commands.add('generateRandomEmail', () => {
    const timestamp = Date.now();
    const randomString = Math.random().toString(36).substring(2, 8);
    return cy.wrap(`test-${timestamp}-${randomString}@example.com`);
});

// Utility function to generate random password
Cypress.Commands.add('generateRandomPassword', () => {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*';
    let password = '';
    for (let i = 0; i < 12; i++) {
        password += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return cy.wrap(password);
});

// Utility function to generate random name
Cypress.Commands.add('generateRandomName', () => {
    const firstNames = ['John', 'Jane', 'Mike', 'Sarah', 'David', 'Lisa', 'Tom', 'Emma', 'Chris', 'Anna'];
    const lastNames = ['Smith', 'Johnson', 'Williams', 'Brown', 'Jones', 'Garcia', 'Miller', 'Davis', 'Rodriguez', 'Martinez'];

    const firstName = firstNames[Math.floor(Math.random() * firstNames.length)];
    const lastName = lastNames[Math.floor(Math.random() * lastNames.length)];

    return cy.wrap(`${firstName} ${lastName}`);
});

// Custom command to register with random credentials
Cypress.Commands.add('registerWithRandomCredentials', () => {
    const email = `test-${Date.now()}-${Math.random().toString(36).substring(2, 8)}@example.com`;
    const password = 'TestPassword123!';
    const fullName = 'Random Test User';

    cy.visit('/register');

    // Fill registration form with random data - use force for Angular Material visibility issues
    cy.get('[data-cy=email-input]').first().type(email, { force: true });
    cy.get('[data-cy=full-name-input]').first().type(fullName, { force: true });
    cy.get('[data-cy=password-input]').first().type(password, { force: true });
    cy.get('[data-cy=confirm-password-input]').first().type(password, { force: true });

    // Submit form
    cy.get('[data-cy=register-button]').first().click();

    // Return the credentials for use in tests
    cy.wrap({ email, password, fullName });
});

// Custom Commands for Website Testing
Cypress.Commands.add('validatePageElements', (expectedElements: string[]) => {
    expectedElements.forEach(element => {
        cy.get('body').should('contain', element);
    });
});

Cypress.Commands.add('testResponsiveDesign', () => {
    const viewports = [
        { width: 1920, height: 1080, name: 'Desktop' },
        { width: 1366, height: 768, name: 'Laptop' },
        { width: 768, height: 1024, name: 'Tablet' },
        { width: 375, height: 667, name: 'Mobile' }
    ];

    viewports.forEach(viewport => {
        cy.viewport(viewport.width, viewport.height);
        cy.get('body').should('be.visible');
        cy.log(`✅ ${viewport.name} viewport (${viewport.width}x${viewport.height}) validated`);
    });

    // Reset to default viewport
    cy.viewport(1280, 720);
});

Cypress.Commands.add('testPageAccessibility', () => {
    // Test basic accessibility features
    cy.get('body').should('have.attr', 'lang');

    // Test for alt text on images
    cy.get('img').each(($img) => {
        cy.wrap($img).should('have.attr', 'alt');
    });

    // Test for proper heading hierarchy
    cy.get('h1, h2, h3, h4, h5, h6').should('exist');

    cy.log('✅ Basic accessibility validation completed');
});

export { };
