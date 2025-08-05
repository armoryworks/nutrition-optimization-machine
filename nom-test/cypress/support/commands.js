// ***********************************************
// This example commands.js shows you how to
// create various custom commands and overwrite
// existing commands.
//
// For more comprehensive examples of custom
// commands please read more here:
// https://on.cypress.io/custom-commands
// ***********************************************

// Custom command to wait for Angular to be ready
Cypress.Commands.add('waitForAngular', () => {
  cy.window().then((win) => {
    // Wait for Angular to be ready
    cy.wait(1000) // Basic wait, can be enhanced with Angular-specific checks
  })
})

// Custom command to check if user is logged in
Cypress.Commands.add('isLoggedIn', () => {
  cy.window().then((win) => {
    // Check if user is logged in by looking for auth-related elements
    return cy.get('body').then(($body) => {
      return !$body.find('[data-cy=login-button]').length
    })
  })
})

// Custom command to logout
Cypress.Commands.add('logout', () => {
  cy.get('[data-cy=user-menu]').click()
  cy.get('[data-cy=logout-button]').click()
  cy.url().should('include', '/login')
})

// Custom command to navigate to a specific page
Cypress.Commands.add('navigateTo', (page) => {
  cy.visit(`/${page}`)
  cy.waitForAngular()
})

// Custom command to fill a form
Cypress.Commands.add('fillForm', (formData) => {
  Object.keys(formData).forEach((field) => {
    cy.get(`[data-cy=${field}]`).type(formData[field])
  })
})

// Custom command to submit a form
Cypress.Commands.add('submitForm', () => {
  cy.get('[data-cy=submit-button]').click()
})

// Custom command to check for success message
Cypress.Commands.add('checkSuccessMessage', (message) => {
  cy.get('[data-cy=success-message]').should('contain', message)
})

// Custom command to check for error message
Cypress.Commands.add('checkErrorMessage', (message) => {
  cy.get('[data-cy=error-message]').should('contain', message)
})

// Custom command to wait for loading to complete
Cypress.Commands.add('waitForLoading', () => {
  cy.get('[data-cy=loading-spinner]').should('not.exist')
})

// Custom command to check if element is visible
Cypress.Commands.add('shouldBeVisible', (selector) => {
  cy.get(selector).should('be.visible')
})

// Custom command to check if element is not visible
Cypress.Commands.add('shouldNotBeVisible', (selector) => {
  cy.get(selector).should('not.be.visible')
})

// Utility function to generate random email
Cypress.Commands.add('generateRandomEmail', () => {
  const timestamp = Date.now()
  const randomString = Math.random().toString(36).substring(2, 8)
  return `test-${timestamp}-${randomString}@example.com`
})

// Utility function to generate random password
Cypress.Commands.add('generateRandomPassword', () => {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*'
  let password = ''
  for (let i = 0; i < 12; i++) {
    password += chars.charAt(Math.floor(Math.random() * chars.length))
  }
  return password
})

// Utility function to generate random name
Cypress.Commands.add('generateRandomName', () => {
  const firstNames = ['John', 'Jane', 'Mike', 'Sarah', 'David', 'Lisa', 'Tom', 'Emma', 'Chris', 'Anna']
  const lastNames = ['Smith', 'Johnson', 'Williams', 'Brown', 'Jones', 'Garcia', 'Miller', 'Davis', 'Rodriguez', 'Martinez']
  
  const firstName = firstNames[Math.floor(Math.random() * firstNames.length)]
  const lastName = lastNames[Math.floor(Math.random() * lastNames.length)]
  
  return `${firstName} ${lastName}`
})

// Custom command to register with random credentials
Cypress.Commands.add('registerWithRandomCredentials', () => {
  const email = `test-${Date.now()}-${Math.random().toString(36).substring(2, 8)}@example.com`
  const password = 'TestPassword123!'
  const fullName = 'Random Test User'
  
  cy.visit('/register')
  
  // Fill registration form with random data - use force for Angular Material visibility issues
  cy.get('[data-cy=email-input]').first().type(email, { force: true })
  cy.get('[data-cy=full-name-input]').first().type(fullName, { force: true })
  cy.get('[data-cy=password-input]').first().type(password, { force: true })
  cy.get('[data-cy=confirm-password-input]').first().type(password, { force: true })
  
  // Submit form
  cy.get('[data-cy=register-button]').first().click()
  
  // Don't return anything - Cypress commands are async
  cy.wrap({ email, password, fullName })
}) 