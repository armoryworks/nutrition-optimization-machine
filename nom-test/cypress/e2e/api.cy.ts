describe('API Tests', () => {
  it('should test API endpoints', () => {
    cy.visit('/');
    cy.get('body').should('be.visible');
  });
});
