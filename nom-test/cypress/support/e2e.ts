// ***********************************************************
// This example support/e2e.ts is processed and
// loaded automatically before your test files.
//
// This is a great place to put global configuration and
// behavior that modifies Cypress.
//
// You can change the location of this file or turn off
// automatically serving support files with the 'supportFile' configuration option.
//
// You can read more here:
// https://on.cypress.io/configuration
// ***********************************************************

// Import commands.js using ES2015 syntax:
import './commands';

// Alternatively you can use CommonJS syntax:
// require('./commands')

// Type definitions for custom commands
declare global {
    namespace Cypress {
        interface Chainable {
            login(email?: string, password?: string): Chainable<void>;
            register(email?: string, password?: string, fullName?: string): Chainable<void>;
            apiRequest(method: string, endpoint: string, body?: any, headers?: any): Chainable<any>;
            clearTestData(): Chainable<void>;
            waitForPageLoad(): Chainable<void>;
            validatePageElements(expectedElements: string[]): Chainable<void>;
            testResponsiveDesign(): Chainable<void>;
            testPageAccessibility(): Chainable<void>;
            waitForAngular(): Chainable<void>;
            isLoggedIn(): Chainable<boolean>;
            logout(): Chainable<void>;
            navigateTo(page: string): Chainable<void>;
            fillForm(formData: Record<string, string>): Chainable<void>;
            submitForm(): Chainable<void>;
            checkSuccessMessage(message: string): Chainable<void>;
            checkErrorMessage(message: string): Chainable<void>;
            waitForLoading(): Chainable<void>;
            shouldBeVisible(selector: string): Chainable<void>;
            shouldNotBeVisible(selector: string): Chainable<void>;
            generateRandomEmail(): Chainable<string>;
            generateRandomPassword(): Chainable<string>;
            generateRandomName(): Chainable<string>;
            registerWithRandomCredentials(): Chainable<{ email: string; password: string; fullName: string }>;
        }
    }
}

// Custom commands are defined in commands.ts
