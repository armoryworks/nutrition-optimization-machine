/// <reference types="cypress" />

// This file ensures Cypress types are properly recognized
declare global {
  namespace Cypress {
    interface Chainable {
      // Custom commands will be added here
    }
  }
}

export { };
