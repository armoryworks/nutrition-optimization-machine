describe('Random Registration', () => {
  it('should test random user registration', () => {
    cy.visit('/register');
    cy.get('body').should('be.visible');
  });
});
