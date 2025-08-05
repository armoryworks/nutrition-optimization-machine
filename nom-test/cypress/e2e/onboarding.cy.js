describe('Onboarding', () => {
  beforeEach(() => {
    cy.clearTestData()
    // Register and login before each test
    cy.register('onboarding@example.com', 'TestPassword123!', 'Onboarding User')
  })

  describe('Onboarding Flow', () => {
    it('should complete the onboarding process successfully', () => {
      cy.visit('/onboarding')
      
      // Step 1: Basic Profile Information
      cy.get('[data-cy=profile-step]').should('be.visible')
      cy.get('[data-cy=age-input]').type('30')
      cy.get('[data-cy=gender-select]').select('Other')
      cy.get('[data-cy=next-button]').click()
      
      // Step 2: Health Information
      cy.get('[data-cy=health-step]').should('be.visible')
      cy.get('[data-cy=height-input]').type('175')
      cy.get('[data-cy=weight-input]').type('70')
      cy.get('[data-cy=activity-level-select]').select('Moderate')
      cy.get('[data-cy=next-button]').click()
      
      // Step 3: Dietary Restrictions
      cy.get('[data-cy=dietary-step]').should('be.visible')
      cy.get('[data-cy=vegetarian-checkbox]').check()
      cy.get('[data-cy=gluten-free-checkbox]').check()
      cy.get('[data-cy=next-button]').click()
      
      // Step 4: Goals
      cy.get('[data-cy=goals-step]').should('be.visible')
      cy.get('[data-cy=weight-loss-radio]').check()
      cy.get('[data-cy=calorie-goal-input]').type('2000')
      cy.get('[data-cy=submit-button]').click()
      
      // Should be redirected to dashboard after completion
      cy.url().should('include', '/dashboard')
      cy.checkSuccessMessage('Onboarding completed successfully')
    })

    it('should allow navigation between steps', () => {
      cy.visit('/onboarding')
      
      // Go to step 2
      cy.get('[data-cy=age-input]').type('25')
      cy.get('[data-cy=next-button]').click()
      
      // Verify we're on step 2
      cy.get('[data-cy=health-step]').should('be.visible')
      
      // Go back to step 1
      cy.get('[data-cy=back-button]').click()
      
      // Verify we're back on step 1
      cy.get('[data-cy=profile-step]').should('be.visible')
      cy.get('[data-cy=age-input]').should('have.value', '25')
    })

    it('should validate required fields', () => {
      cy.visit('/onboarding')
      
      // Try to proceed without filling required fields
      cy.get('[data-cy=next-button]').click()
      
      // Should show validation errors
      cy.get('[data-cy=age-error]').should('be.visible')
      cy.get('[data-cy=age-error]').should('contain', 'Age is required')
    })

    it('should save progress between steps', () => {
      cy.visit('/onboarding')
      
      // Fill step 1
      cy.get('[data-cy=age-input]').type('35')
      cy.get('[data-cy=gender-select]').select('Male')
      cy.get('[data-cy=next-button]').click()
      
      // Fill step 2
      cy.get('[data-cy=height-input]').type('180')
      cy.get('[data-cy=weight-input]').type('75')
      cy.get('[data-cy=back-button]').click()
      
      // Navigate back and forth
      cy.get('[data-cy=next-button]').click()
      
      // Data should be preserved
      cy.get('[data-cy=height-input]').should('have.value', '180')
      cy.get('[data-cy=weight-input]').should('have.value', '75')
    })
  })

  describe('Multi-participant Onboarding', () => {
    it('should handle multiple participants', () => {
      cy.visit('/onboarding')
      
      // Indicate multiple participants
      cy.get('[data-cy=multiple-participants-radio]').check()
      cy.get('[data-cy=participant-count-input]').type('2')
      cy.get('[data-cy=next-button]').click()
      
      // Should show participant slots
      cy.get('[data-cy=participant-1-name]').type('John Doe')
      cy.get('[data-cy=participant-2-name]').type('Jane Doe')
      cy.get('[data-cy=next-button]').click()
      
      // Continue with onboarding for each participant
      // ... additional steps for each participant
    })
  })

  describe('Onboarding Completion', () => {
    it('should redirect to dashboard after completion', () => {
      cy.visit('/onboarding')
      
      // Complete all steps (simplified)
      cy.get('[data-cy=age-input]').type('30')
      cy.get('[data-cy=next-button]').click()
      cy.get('[data-cy=height-input]').type('175')
      cy.get('[data-cy=weight-input]').type('70')
      cy.get('[data-cy=next-button]').click()
      cy.get('[data-cy=next-button]').click() // Skip dietary restrictions
      cy.get('[data-cy=next-button]').click() // Skip goals
      cy.get('[data-cy=submit-button]').click()
      
      // Should be redirected to dashboard
      cy.url().should('include', '/dashboard')
      cy.get('[data-cy=dashboard-welcome]').should('be.visible')
    })

    it('should show completion summary', () => {
      // Complete onboarding first
      cy.visit('/onboarding')
      // ... complete onboarding steps
      
      // Should show completion summary
      cy.get('[data-cy=completion-summary]').should('be.visible')
      cy.get('[data-cy=profile-complete]').should('be.visible')
      cy.get('[data-cy=health-complete]').should('be.visible')
      cy.get('[data-cy=dietary-complete]').should('be.visible')
      cy.get('[data-cy=goals-complete]').should('be.visible')
    })
  })
}) 