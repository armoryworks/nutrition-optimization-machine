describe('User Registration with Random Credentials', () => {
  beforeEach(() => {
    // For now, skip API cleanup since backend might not be running
    cy.log('Starting registration tests with random credentials')
  })

  it('should successfully register a user with random credentials', () => {
    // Generate random test data
    const email = `test-${Date.now()}-${Math.random().toString(36).substring(2, 8)}@example.com`
    const password = 'TestPassword123!'
    const fullName = 'Random Test User'

    cy.visit('/register')
    
    // Wait for form to be ready and visible
    cy.get('[data-cy=email-input]').should('exist').and('be.visible')
    cy.get('[data-cy=full-name-input]').should('exist').and('be.visible')
    cy.get('[data-cy=password-input]').should('exist').and('be.visible')
    cy.get('[data-cy=confirm-password-input]').should('exist').and('be.visible')
    cy.get('[data-cy=register-button]').should('exist').and('be.visible')
    
    // Fill registration form with random data - use force for Angular Material visibility issues
    cy.get('[data-cy=email-input]').first().type(email, { force: true })
    cy.get('[data-cy=full-name-input]').first().type(fullName, { force: true })
    cy.get('[data-cy=password-input]').first().type(password, { force: true })
    cy.get('[data-cy=confirm-password-input]').first().type(password, { force: true })
    
    // Submit form
    cy.get('[data-cy=register-button]').first().click()
    
    // Check if we get redirected or show success message
    // This will depend on whether the backend is running
    cy.url().should('not.include', '/register')
  })

  it('should validate form fields with random data', () => {
    cy.visit('/register')
    
    // Test with invalid email
    cy.get('[data-cy=email-input]').first().type('invalid-email', { force: true })
    cy.get('[data-cy=full-name-input]').first().type('Test User', { force: true })
    cy.get('[data-cy=password-input]').first().type('TestPassword123!', { force: true })
    cy.get('[data-cy=confirm-password-input]').first().type('TestPassword123!', { force: true })
    
    // Should show validation error for invalid email
    cy.get('.mat-error').should('contain', 'valid email')
    
    // Test with short password
    cy.get('[data-cy=email-input]').first().clear().type('valid@example.com', { force: true })
    cy.get('[data-cy=password-input]').first().clear().type('short', { force: true })
    cy.get('[data-cy=confirm-password-input]').first().clear().type('short', { force: true })
    
    // Should show validation error for short password
    cy.get('.mat-error').should('contain', 'at least 8 characters')
  })

  it('should handle password mismatch validation', () => {
    cy.visit('/register')
    
    const email = `test-${Date.now()}-${Math.random().toString(36).substring(2, 8)}@example.com`
    
    cy.get('[data-cy=email-input]').first().type(email, { force: true })
    cy.get('[data-cy=full-name-input]').first().type('Test User', { force: true })
    cy.get('[data-cy=password-input]').first().type('TestPassword123!', { force: true })
    cy.get('[data-cy=confirm-password-input]').first().type('DifferentPassword123!', { force: true })
    
    // Should show password mismatch error
    cy.get('.mat-error').should('contain', 'Passwords do not match')
  })

  it('should use the custom registerWithRandomCredentials command', () => {
    // Use the custom command we created
    cy.registerWithRandomCredentials()
    
    // Verify the form was filled and submitted
    cy.url().should('not.include', '/register')
  })

  it('should handle very long random credentials', () => {
    // Generate very long random credentials
    const longEmail = `very-long-test-${Date.now()}-${'a'.repeat(50)}@example.com`
    const longPassword = 'A'.repeat(50) + 'a'.repeat(50) + '1'.repeat(10) + '!@#$%^&*'
    const longName = 'Very Long Test User Name That Exceeds Normal Length Limits For Testing Purposes'
    
    cy.visit('/register')
    
    cy.get('[data-cy=email-input]').first().type(longEmail, { force: true })
    cy.get('[data-cy=full-name-input]').first().type(longName, { force: true })
    cy.get('[data-cy=password-input]').first().type(longPassword, { force: true })
    cy.get('[data-cy=confirm-password-input]').first().type(longPassword, { force: true })
    
    // Verify form accepts long inputs
    cy.get('[data-cy=email-input]').first().should('have.value', longEmail)
    cy.get('[data-cy=full-name-input]').first().should('have.value', longName)
    cy.get('[data-cy=password-input]').first().should('have.value', longPassword)
  })

  it('should handle special characters in random credentials', () => {
    const email = `special-chars-${Date.now()}-test@example.com`
    const password = 'TestPassword123!@#$%^&*()_+-=[]{}|;:,.<>?'
    const fullName = 'Special Chars Test User !@#$%^&*()'
    
    cy.visit('/register')
    
    cy.get('[data-cy=email-input]').first().type(email, { force: true })
    cy.get('[data-cy=full-name-input]').first().type(fullName, { force: true })
    cy.get('[data-cy=password-input]').first().type(password, { force: true })
    cy.get('[data-cy=confirm-password-input]').first().type(password, { force: true })
    
    // Verify form accepts special characters
    cy.get('[data-cy=email-input]').first().should('have.value', email)
    cy.get('[data-cy=full-name-input]').first().should('have.value', fullName)
    cy.get('[data-cy=password-input]').first().should('have.value', password)
  })
}) 