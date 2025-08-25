describe('Recipe Management', () => {
  it('should test recipe functionality', () => {
    cy.visit('/recipe');
    cy.get('body').should('be.visible');
  });
});
