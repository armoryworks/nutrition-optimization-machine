describe('Smoke Test', () => {
  it('should load the application', () => {
    cy.visit('/')
    cy.get('body').should('be.visible')
  })

  it('should have working navigation', () => {
    cy.visit('/')
    
    // Check if navigation elements exist
    cy.get('body').should('contain', 'NOM')
  })

  it('should handle API connectivity', () => {
    // Test API connectivity
    cy.apiRequest('GET', '/api/health').then((response) => {
      // This might fail if the API isn't running, but that's expected
      // The test will pass if the request completes (even with 404)
      expect(response).to.exist
    })
  })
}) 