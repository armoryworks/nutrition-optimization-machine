describe('Smoke Tests', () => {
  it('should load basic pages', () => {
    cy.visit('/');
    cy.get('body').should('be.visible');
  });
});
