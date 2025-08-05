describe('Authentication', () => {
  beforeEach(() => {
    cy.clearTestData()
  })

  describe('Registration', () => {
    it('should successfully register a new user', () => {
      const user = {
        email: 'newuser@example.com',
        password: 'TestPassword123!',
        fullName: 'New User'
      }

      cy.visit('/register')
      
      // Fill registration form
      cy.get('[data-cy=email-input]').type(user.email)
      cy.get('[data-cy=full-name-input]').type(user.fullName)
      cy.get('[data-cy=password-input]').type(user.password)
      cy.get('[data-cy=confirm-password-input]').type(user.password)
      
      // Submit form
      cy.get('[data-cy=register-button]').click()
      
      // Should be redirected to onboarding after successful registration
      cy.url().should('include', '/onboarding')
      
      // Should show success message
      cy.checkSuccessMessage('Registration successful')
    })

    it('should show error for invalid email', () => {
      cy.visit('/register')
      
      cy.get('[data-cy=email-input]').type('invalid-email')
      cy.get('[data-cy=full-name-input]').type('Test User')
      cy.get('[data-cy=password-input]').type('TestPassword123!')
      cy.get('[data-cy=confirm-password-input]').type('TestPassword123!')
      
      cy.get('[data-cy=register-button]').click()
      
      cy.checkErrorMessage('Please enter a valid email')
    })

    it('should show error for password mismatch', () => {
      cy.visit('/register')
      
      cy.get('[data-cy=email-input]').type('test@example.com')
      cy.get('[data-cy=full-name-input]').type('Test User')
      cy.get('[data-cy=password-input]').type('TestPassword123!')
      cy.get('[data-cy=confirm-password-input]').type('DifferentPassword123!')
      
      cy.get('[data-cy=register-button]').click()
      
      cy.checkErrorMessage('Passwords do not match')
    })

    it('should show error for weak password', () => {
      cy.visit('/register')
      
      cy.get('[data-cy=email-input]').type('test@example.com')
      cy.get('[data-cy=full-name-input]').type('Test User')
      cy.get('[data-cy=password-input]').type('weak')
      cy.get('[data-cy=confirm-password-input]').type('weak')
      
      cy.get('[data-cy=register-button]').click()
      
      cy.checkErrorMessage('Password must be at least 8 characters')
    })
  })

  describe('Login', () => {
    it('should successfully login with valid credentials', () => {
      // First register a user
      cy.register('test@example.com', 'TestPassword123!', 'Test User')
      
      // Then login
      cy.login('test@example.com', 'TestPassword123!')
      
      // Should be redirected to dashboard or home
      cy.url().should('not.include', '/login')
      cy.url().should('not.include', '/register')
    })

    it('should show error for invalid credentials', () => {
      cy.visit('/login')
      
      cy.get('[data-cy=email-input]').type('nonexistent@example.com')
      cy.get('[data-cy=password-input]').type('wrongpassword')
      cy.get('[data-cy=login-button]').click()
      
      cy.checkErrorMessage('Invalid credentials')
    })

    it('should show error for empty fields', () => {
      cy.visit('/login')
      
      cy.get('[data-cy=login-button]').click()
      
      cy.checkErrorMessage('Please fill all required fields')
    })
  })

  describe('Logout', () => {
    it('should successfully logout', () => {
      // First login
      cy.register('test@example.com', 'TestPassword123!', 'Test User')
      cy.login('test@example.com', 'TestPassword123!')
      
      // Then logout
      cy.logout()
      
      // Should be redirected to login page
      cy.url().should('include', '/login')
    })
  })

  describe('Auto-login after registration', () => {
    it('should automatically login user after successful registration', () => {
      const user = {
        email: 'autologin@example.com',
        password: 'TestPassword123!',
        fullName: 'Auto Login User'
      }

      cy.visit('/register')
      
      // Fill and submit registration form
      cy.fillForm({
        'email-input': user.email,
        'full-name-input': user.fullName,
        'password-input': user.password,
        'confirm-password-input': user.password
      })
      
      cy.submitForm()
      
      // Should be automatically logged in and redirected
      cy.url().should('include', '/onboarding')
      
      // Should not show login button (user is logged in)
      cy.get('[data-cy=login-button]').should('not.exist')
      
      // Should show user menu (indicating logged in state)
      cy.get('[data-cy=user-menu]').should('be.visible')
    })
  })
}) 