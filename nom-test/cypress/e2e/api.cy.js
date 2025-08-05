describe('API Tests', () => {
  beforeEach(() => {
    cy.clearTestData()
  })

  describe('Authentication API', () => {
    it('should register a new user via API', () => {
      const userData = {
        email: 'apitest@example.com',
        username: 'apitest@example.com',
        password: 'TestPassword123!',
        confirmPassword: 'TestPassword123!',
        fullName: 'API Test User',
        groupToken: null,
        householdToken: null
      }

      cy.apiRequest('POST', '/api/auth/register', userData).then((response) => {
        expect(response.status).to.eq(200)
      })
    })

    it('should login user via API', () => {
      // First register a user
      const userData = {
        email: 'apilogin@example.com',
        username: 'apilogin@example.com',
        password: 'TestPassword123!',
        confirmPassword: 'TestPassword123!',
        fullName: 'API Login User',
        groupToken: null,
        householdToken: null
      }

      cy.apiRequest('POST', '/api/auth/register', userData)

      // Then login
      const loginData = {
        email: 'apilogin@example.com',
        password: 'TestPassword123!',
        twoFactorCode: '',
        toFactorRecoveryCode: '',
        rememberMe: true
      }

      cy.apiRequest('POST', '/api/auth/login', loginData).then((response) => {
        expect(response.status).to.eq(200)
        expect(response.body).to.have.property('accessToken')
        expect(response.body).to.have.property('refreshToken')
        expect(response.body).to.have.property('expiresIn')
      })
    })

    it('should reject invalid login credentials', () => {
      const loginData = {
        email: 'nonexistent@example.com',
        password: 'wrongpassword',
        twoFactorCode: '',
        toFactorRecoveryCode: '',
        rememberMe: false
      }

      cy.apiRequest('POST', '/api/auth/login', loginData).then((response) => {
        expect(response.status).to.eq(400)
      })
    })
  })

  describe('User Management API', () => {
    let authToken

    beforeEach(() => {
      // Register and login to get auth token
      const userData = {
        email: 'usermanagement@example.com',
        username: 'usermanagement@example.com',
        password: 'TestPassword123!',
        confirmPassword: 'TestPassword123!',
        fullName: 'User Management Test',
        groupToken: null,
        householdToken: null
      }

      cy.apiRequest('POST', '/api/auth/register', userData)

      const loginData = {
        email: 'usermanagement@example.com',
        password: 'TestPassword123!',
        twoFactorCode: '',
        toFactorRecoveryCode: '',
        rememberMe: true
      }

      cy.apiRequest('POST', '/api/auth/login', loginData).then((response) => {
        authToken = response.body.accessToken
      })
    })

    it('should get current user info', () => {
      cy.apiRequest('GET', '/api/auth/manage/info', null, {
        Authorization: `Bearer ${authToken}`
      }).then((response) => {
        expect(response.status).to.eq(200)
        expect(response.body).to.have.property('email')
        expect(response.body.email).to.eq('usermanagement@example.com')
      })
    })

    it('should update user info', () => {
      const updateData = {
        fullName: 'Updated User Name',
        phoneNumber: '+1234567890'
      }

      cy.apiRequest('POST', '/api/auth/manage/info', updateData, {
        Authorization: `Bearer ${authToken}`
      }).then((response) => {
        expect(response.status).to.eq(200)
      })
    })
  })

  describe('Recipe API', () => {
    let authToken

    beforeEach(() => {
      // Register and login to get auth token
      const userData = {
        email: 'recipeapi@example.com',
        username: 'recipeapi@example.com',
        password: 'TestPassword123!',
        confirmPassword: 'TestPassword123!',
        fullName: 'Recipe API Test',
        groupToken: null,
        householdToken: null
      }

      cy.apiRequest('POST', '/api/auth/register', userData)

      const loginData = {
        email: 'recipeapi@example.com',
        password: 'TestPassword123!',
        twoFactorCode: '',
        toFactorRecoveryCode: '',
        rememberMe: true
      }

      cy.apiRequest('POST', '/api/auth/login', loginData).then((response) => {
        authToken = response.body.accessToken
      })
    })

    it('should create a recipe via API', () => {
      const recipeData = {
        name: 'API Test Recipe',
        description: 'Recipe created via API',
        servings: 4,
        prepTime: 30,
        cookTime: 45,
        ingredients: [
          {
            name: 'Chicken Breast',
            amount: 500,
            unit: 'grams'
          }
        ],
        instructions: [
          {
            stepNumber: 1,
            text: 'Preheat oven to 400°F'
          },
          {
            stepNumber: 2,
            text: 'Season chicken with salt and pepper'
          }
        ]
      }

      cy.apiRequest('POST', '/api/Recipe', recipeData, {
        Authorization: `Bearer ${authToken}`
      }).then((response) => {
        expect(response.status).to.eq(200)
        expect(response.body).to.have.property('id')
        expect(response.body.name).to.eq('API Test Recipe')
      })
    })

    it('should get user recipes', () => {
      cy.apiRequest('GET', '/api/Recipe/user', null, {
        Authorization: `Bearer ${authToken}`
      }).then((response) => {
        expect(response.status).to.eq(200)
        expect(response.body).to.be.an('array')
      })
    })

    it('should update a recipe', () => {
      // First create a recipe
      const recipeData = {
        name: 'Original Recipe',
        description: 'Original description',
        servings: 2,
        prepTime: 15,
        cookTime: 30,
        ingredients: [],
        instructions: []
      }

      cy.apiRequest('POST', '/api/Recipe', recipeData, {
        Authorization: `Bearer ${authToken}`
      }).then((createResponse) => {
        const recipeId = createResponse.body.id

        // Then update it
        const updateData = {
          name: 'Updated Recipe',
          description: 'Updated description',
          servings: 4,
          prepTime: 20,
          cookTime: 40,
          ingredients: [],
          instructions: []
        }

        cy.apiRequest('PUT', `/api/Recipe/${recipeId}`, updateData, {
          Authorization: `Bearer ${authToken}`
        }).then((updateResponse) => {
          expect(updateResponse.status).to.eq(200)
          expect(updateResponse.body.name).to.eq('Updated Recipe')
        })
      })
    })
  })

  describe('Privacy API', () => {
    let authToken

    beforeEach(() => {
      // Register and login to get auth token
      const userData = {
        email: 'privacyapi@example.com',
        username: 'privacyapi@example.com',
        password: 'TestPassword123!',
        confirmPassword: 'TestPassword123!',
        fullName: 'Privacy API Test',
        groupToken: null,
        householdToken: null
      }

      cy.apiRequest('POST', '/api/auth/register', userData)

      const loginData = {
        email: 'privacyapi@example.com',
        password: 'TestPassword123!',
        twoFactorCode: '',
        toFactorRecoveryCode: '',
        rememberMe: true
      }

      cy.apiRequest('POST', '/api/auth/login', loginData).then((response) => {
        authToken = response.body.accessToken
      })
    })

    it('should get user consents', () => {
      cy.apiRequest('GET', '/api/Privacy/consents', null, {
        Authorization: `Bearer ${authToken}`
      }).then((response) => {
        expect(response.status).to.eq(200)
        expect(response.body).to.be.an('array')
      })
    })

    it('should update user consent', () => {
      const consentData = {
        consentType: 'Analytics',
        isConsented: true,
        consentVersion: '1.0',
        legalBasis: 'Legitimate Interest'
      }

      cy.apiRequest('POST', '/api/Privacy/consent', consentData, {
        Authorization: `Bearer ${authToken}`
      }).then((response) => {
        expect(response.status).to.eq(200)
      })
    })

    it('should request data export', () => {
      const exportData = {
        exportType: 'PersonalData',
        format: 'JSON'
      }

      cy.apiRequest('POST', '/api/Privacy/data-export', exportData, {
        Authorization: `Bearer ${authToken}`
      }).then((response) => {
        expect(response.status).to.eq(202) // Accepted for async processing
      })
    })
  })

  describe('Error Handling', () => {
    it('should return 401 for unauthorized requests', () => {
      cy.apiRequest('GET', '/api/Recipe/user').then((response) => {
        expect(response.status).to.eq(401)
      })
    })

    it('should return 404 for non-existent endpoints', () => {
      cy.apiRequest('GET', '/api/nonexistent').then((response) => {
        expect(response.status).to.eq(404)
      })
    })

    it('should return 400 for invalid data', () => {
      const invalidData = {
        email: 'invalid-email',
        password: 'short'
      }

      cy.apiRequest('POST', '/api/auth/register', invalidData).then((response) => {
        expect(response.status).to.eq(400)
      })
    })
  })
}) 