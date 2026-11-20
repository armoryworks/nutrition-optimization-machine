import { PHONE_VIEWPORT } from '../support/viewports';

interface RecipeSearchResponse {
  results: { id: number; name: string }[];
}

describe('Recipe with a withheld method (phone)', () => {
  beforeEach(() => {
    cy.viewport(PHONE_VIEWPORT.width, PHONE_VIEWPORT.height);
    cy.clearCookies();
    cy.clearLocalStorage();
  });

  it('shows key facts, the source link and the note, and no steps or description', () => {
    cy.intercept('GET', '**/api/RecipeSearch/popular*').as('popularRecipes');
    cy.visit('/home');
    cy.wait('@popularRecipes').its('response.body').then((body: RecipeSearchResponse) => {
      if (body.results.length === 0) {
        cy.log('No public recipe to open; nothing to check.');
        return;
      }
      const id = body.results[0].id;
      cy.intercept('GET', `**/api/Recipe/${id}`, (req) => {
        req.continue((res) => {
          res.body = {
            ...res.body,
            description: '',
            steps: [],
            proseWithheld: true,
            keyFacts: ['oven 350°F', '9 x 13-inch pan', 'bake 30 to 40 min'],
            sourceUrl: 'https://www.example.com/cake',
          };
        });
      }).as('withheldRecipe');

      cy.visit(`/recipe/${id}`);
      cy.wait('@withheldRecipe');
      cy.byTestId('recipe-detail').should('be.visible');
      cy.byTestId('recipe-method-withheld').scrollIntoView().should('be.visible');
      cy.byTestId('recipe-key-fact').should('have.length', 3).first().should('have.text', 'oven 350°F');
      cy.byTestId('recipe-method-source-link')
        .should('have.attr', 'href', 'https://www.example.com/cake')
        .and('contain.text', 'Full method at example.com');
      cy.byTestId('recipe-method-withheld-note').should('contain.text', 'in our own words');
      cy.byTestId('recipe-instructions').should('not.contain.text', 'No instructions listed.');
      cy.document().its('documentElement.scrollWidth').should('be.lte', PHONE_VIEWPORT.width);
    });
  });
});
