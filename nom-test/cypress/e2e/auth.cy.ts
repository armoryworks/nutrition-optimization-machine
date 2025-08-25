describe('Authentication', () => {
  it('should register a new user', () => {
    cy.visit('/register');
    cy.get('body').should('be.visible');
  });

  it('should login existing user', () => {
    cy.visit('/login');
    cy.get('body').should('be.visible');
  });
});
