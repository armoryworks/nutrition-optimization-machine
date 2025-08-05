describe('Simple Test', () => {
  it('should load the registration page', () => {
    cy.visit('/register')
    
    // Check if page loads
    cy.get('body').should('be.visible')
    
    // Check if we can find any elements
    cy.get('input').should('exist')
    
    // Log what we find
    cy.get('input').then(($inputs) => {
      cy.log(`Found ${$inputs.length} input elements`)
      $inputs.each((index, element) => {
        cy.log(`Input ${index}: ${element.getAttribute('data-cy') || 'no data-cy'}`)
      })
    })
  })

  it('should find registration form elements', () => {
    cy.visit('/register')
    
    // Check for any elements with data-cy attributes
    cy.get('[data-cy]').then(($elements) => {
      cy.log(`Found ${$elements.length} elements with data-cy attributes`)
      $elements.each((index, element) => {
        cy.log(`Element ${index}: ${element.getAttribute('data-cy')}`)
      })
    })
  })
}) 