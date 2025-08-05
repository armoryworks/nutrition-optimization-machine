describe('Recipe Management', () => {
  beforeEach(() => {
    cy.clearTestData()
    cy.register('recipe@example.com', 'TestPassword123!', 'Recipe User')
    cy.login('recipe@example.com', 'TestPassword123!')
  })

  describe('Recipe Creation', () => {
    it('should create a new recipe successfully', () => {
      cy.visit('/recipes/new')
      
      // Fill recipe form
      cy.get('[data-cy=recipe-name-input]').type('Test Recipe')
      cy.get('[data-cy=recipe-description-input]').type('A delicious test recipe')
      cy.get('[data-cy=recipe-servings-input]').type('4')
      cy.get('[data-cy=recipe-prep-time-input]').type('30')
      cy.get('[data-cy=recipe-cook-time-input]').type('45')
      
      // Add ingredients
      cy.get('[data-cy=add-ingredient-button]').click()
      cy.get('[data-cy=ingredient-name-input]').type('Chicken Breast')
      cy.get('[data-cy=ingredient-amount-input]').type('500')
      cy.get('[data-cy=ingredient-unit-select]').select('grams')
      
      cy.get('[data-cy=add-ingredient-button]').click()
      cy.get('[data-cy=ingredient-name-input]').eq(1).type('Olive Oil')
      cy.get('[data-cy=ingredient-amount-input]').eq(1).type('2')
      cy.get('[data-cy=ingredient-unit-select]').eq(1).select('tablespoons')
      
      // Add instructions
      cy.get('[data-cy=add-instruction-button]').click()
      cy.get('[data-cy=instruction-text-input]').type('Preheat oven to 400°F')
      
      cy.get('[data-cy=add-instruction-button]').click()
      cy.get('[data-cy=instruction-text-input]').eq(1).type('Season chicken with salt and pepper')
      
      // Submit recipe
      cy.get('[data-cy=save-recipe-button]').click()
      
      // Should be redirected to recipe detail page
      cy.url().should('include', '/recipes/')
      cy.checkSuccessMessage('Recipe created successfully')
    })

    it('should validate required fields', () => {
      cy.visit('/recipes/new')
      
      // Try to save without filling required fields
      cy.get('[data-cy=save-recipe-button]').click()
      
      // Should show validation errors
      cy.get('[data-cy=recipe-name-error]').should('be.visible')
      cy.get('[data-cy=recipe-name-error]').should('contain', 'Recipe name is required')
    })

    it('should reuse existing ingredients', () => {
      cy.visit('/recipes/new')
      
      // Search for existing ingredient
      cy.get('[data-cy=ingredient-search-input]').type('Chicken')
      cy.get('[data-cy=ingredient-suggestions]').should('be.visible')
      cy.get('[data-cy=ingredient-suggestion]').first().click()
      
      // Should populate ingredient fields
      cy.get('[data-cy=ingredient-name-input]').should('have.value', 'Chicken Breast')
    })
  })

  describe('Recipe Listing', () => {
    it('should display user recipes', () => {
      // Create a recipe first
      cy.visit('/recipes/new')
      cy.get('[data-cy=recipe-name-input]').type('My Test Recipe')
      cy.get('[data-cy=recipe-description-input]').type('Test description')
      cy.get('[data-cy=save-recipe-button]').click()
      
      // Go to recipes list
      cy.visit('/recipes')
      
      // Should show the created recipe
      cy.get('[data-cy=recipe-card]').should('contain', 'My Test Recipe')
      cy.get('[data-cy=recipe-card]').should('contain', 'Test description')
    })

    it('should filter recipes by status', () => {
      cy.visit('/recipes')
      
      // Filter by NonCurated status
      cy.get('[data-cy=status-filter]').select('NonCurated')
      cy.get('[data-cy=apply-filter-button]').click()
      
      // Should only show NonCurated recipes
      cy.get('[data-cy=recipe-card]').each(($card) => {
        cy.wrap($card).find('[data-cy=recipe-status]').should('contain', 'NonCurated')
      })
    })

    it('should search recipes', () => {
      cy.visit('/recipes')
      
      // Search for specific recipe
      cy.get('[data-cy=search-input]').type('Test Recipe')
      cy.get('[data-cy=search-button]').click()
      
      // Should show matching recipes
      cy.get('[data-cy=recipe-card]').should('contain', 'Test Recipe')
    })
  })

  describe('Recipe Editing', () => {
    it('should edit an existing recipe', () => {
      // Create a recipe first
      cy.visit('/recipes/new')
      cy.get('[data-cy=recipe-name-input]').type('Original Recipe')
      cy.get('[data-cy=recipe-description-input]').type('Original description')
      cy.get('[data-cy=save-recipe-button]').click()
      
      // Get the recipe ID from URL
      cy.url().then((url) => {
        const recipeId = url.split('/').pop()
        
        // Edit the recipe
        cy.visit(`/recipes/${recipeId}/edit`)
        cy.get('[data-cy=recipe-name-input]').clear().type('Updated Recipe')
        cy.get('[data-cy=recipe-description-input]').clear().type('Updated description')
        cy.get('[data-cy=save-recipe-button]').click()
        
        // Should show updated content
        cy.get('[data-cy=recipe-name]').should('contain', 'Updated Recipe')
        cy.get('[data-cy=recipe-description]').should('contain', 'Updated description')
      })
    })

    it('should create new version of curated recipe', () => {
      // Create a recipe and submit for curation
      cy.visit('/recipes/new')
      cy.get('[data-cy=recipe-name-input]').type('Curated Recipe')
      cy.get('[data-cy=recipe-description-input]').type('Curated description')
      cy.get('[data-cy=save-recipe-button]').click()
      
      // Submit for curation
      cy.get('[data-cy=submit-for-curation-button]').click()
      
      // Should show version creation option
      cy.get('[data-cy=create-version-button]').should('be.visible')
      cy.get('[data-cy=create-version-button]').click()
      
      // Should pre-populate with original data
      cy.get('[data-cy=recipe-name-input]').should('have.value', 'Curated Recipe')
      cy.get('[data-cy=recipe-description-input]').should('have.value', 'Curated description')
    })
  })

  describe('Recipe Curation', () => {
    it('should submit recipe for curation', () => {
      // Create a recipe
      cy.visit('/recipes/new')
      cy.get('[data-cy=recipe-name-input]').type('Recipe for Curation')
      cy.get('[data-cy=recipe-description-input]').type('Ready for curation')
      cy.get('[data-cy=save-recipe-button]').click()
      
      // Submit for curation
      cy.get('[data-cy=submit-for-curation-button]').click()
      
      // Should show pending status
      cy.get('[data-cy=recipe-status]').should('contain', 'PendingCuration')
      cy.checkSuccessMessage('Recipe submitted for curation')
    })
  })
}) 