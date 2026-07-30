const { defineConfig } = require('cypress')

module.exports = defineConfig({
  e2e: {
    // Support both local dev and containerized test environments
    baseUrl: process.env.CYPRESS_BASE_URL || 'http://localhost:4210', // Angular dev server
    supportFile: 'cypress/support/e2e.ts',
    specPattern: 'cypress/e2e/**/*.cy.{js,jsx,ts,tsx}',
    viewportWidth: 1280,
    viewportHeight: 720,
    video: false,
    screenshotOnRunFailure: true,
    defaultCommandTimeout: 10000,
    requestTimeout: 10000,
    responseTimeout: 10000,
    env: {
      // Support environment variable override for CI/CD
      apiUrl: process.env.CYPRESS_API_URL || 'http://localhost:8080', // .NET API server
    },
    setupNodeEvents(on, config) {
      // implement node event listeners here
    },
  },
}) 