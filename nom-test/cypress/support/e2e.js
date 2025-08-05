// ***********************************************************
// This example support/e2e.js is processed and
// loaded automatically before your test files.
//
// This is a great place to put global configuration and
// behavior that modifies Cypress.
//
// You can change the location of this file or turn off
// automatically serving support files with the
// 'supportFile' configuration option.
//
// You can read more here:
// https://on.cypress.io/configuration
// ***********************************************************

// Import commands.js using ES2015 syntax:
import './commands'

// Alternatively you can use CommonJS syntax:
// require('./commands')

// Custom commands for NOM application
Cypress.Commands.add('login', (email = 'test@example.com', password = 'TestPassword123!') => {
  cy.visit('/login')
  cy.get('[data-cy=email-input]').type(email)
  cy.get('[data-cy=password-input]').type(password)
  cy.get('[data-cy=login-button]').click()
  cy.url().should('not.include', '/login')
})

Cypress.Commands.add('register', (email = 'test@example.com', password = 'TestPassword123!', fullName = 'Test User') => {
  cy.visit('/register')
  cy.get('[data-cy=email-input]').type(email)
  cy.get('[data-cy=full-name-input]').type(fullName)
  cy.get('[data-cy=password-input]').type(password)
  cy.get('[data-cy=confirm-password-input]').type(password)
  cy.get('[data-cy=register-button]').click()
})

Cypress.Commands.add('apiRequest', (method, endpoint, body = null) => {
  const apiUrl = Cypress.env('apiUrl')
  const options = {
    method,
    url: `${apiUrl}${endpoint}`,
    headers: {
      'Content-Type': 'application/json',
    },
    failOnStatusCode: false,
  }
  
  if (body) {
    options.body = body
  }
  
  return cy.request(options).catch(() => {
    // Return a rejected promise if the request fails
    return Promise.reject(new Error('API request failed'))
  })
})

Cypress.Commands.add('clearTestData', () => {
  // Clear any test data that might interfere with tests
  cy.apiRequest('POST', '/api/test/clear-data')
})

Cypress.Commands.add('waitForPageLoad', () => {
  cy.get('body').should('not.have.class', 'loading')
})

// Custom commands are defined above 