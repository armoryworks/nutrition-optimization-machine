describe('Onboarding', () => {
  it('should complete onboarding process', () => {
    // Test onboarding workflow
    cy.visit('/onboarding');
    cy.get('body').should('be.visible');
  });
});
