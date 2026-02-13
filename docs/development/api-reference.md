# API Reference Guide

This document provides comprehensive documentation for the NOM backend API endpoints, including request/response formats, authentication, and usage examples.

## **Authentication**

### Bearer Token Authentication

All API endpoints require authentication using Bearer tokens, except for public endpoints.

```http
Authorization: Bearer <your-jwt-token>
```

### Token Expiration

- **Bearer Token**: 24 hours
- **Refresh Token**: 7 days

### Public Endpoints

- `POST /api/auth/register` - User registration
- `POST /api/auth/login` - User login
- `GET /api/auth/refresh` - Token refresh

## **API Endpoints**

### Authentication

#### Register User

```http
POST /api/auth/register
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePassword123!",
  "firstName": "John",
  "lastName": "Doe"
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "userId": "uuid",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "accessToken": "jwt-token",
    "refreshToken": "refresh-token"
  }
}
```

#### Login User

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePassword123!"
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "userId": "uuid",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "accessToken": "jwt-token",
    "refreshToken": "refresh-token"
  }
}
```

### User Management

#### Get Current User Profile

```http
GET /api/users/profile
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "createdDate": "2025-07-30T10:00:00Z",
    "lastLoginDate": "2025-07-30T10:00:00Z"
  }
}
```

#### Update User Profile

```http
PUT /api/users/profile
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@example.com"
}
```

### Recipe Management

#### Get All Recipes

```http
GET /api/recipes
Authorization: Bearer <token>
```

**Query Parameters:**

- `page` (optional): Page number (default: 1)
- `pageSize` (optional): Items per page (default: 20)
- `search` (optional): Search term
- `status` (optional): Filter by status (Draft, PendingCuration, Curated, Rejected)

**Response:**

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "uuid",
        "name": "Chicken Pasta",
        "description": "Delicious chicken pasta recipe",
        "status": "Curated",
        "authorId": "uuid",
        "authorName": "John Doe",
        "createdDate": "2025-07-30T10:00:00Z",
        "prepTime": 30,
        "cookTime": 45,
        "servings": 4
      }
    ],
    "totalCount": 100,
    "page": 1,
    "pageSize": 20
  }
}
```

#### Get Recipe by ID

```http
GET /api/recipes/{id}
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "name": "Chicken Pasta",
    "description": "Delicious chicken pasta recipe",
    "instructions": "1. Boil pasta...",
    "prepTime": 30,
    "cookTime": 45,
    "servings": 4,
    "status": "Curated",
    "authorId": "uuid",
    "authorName": "John Doe",
    "createdDate": "2025-07-30T10:00:00Z",
    "ingredients": [
      {
        "id": "uuid",
        "name": "Chicken Breast",
        "quantity": 500,
        "unit": "g",
        "nutritionalInfo": {
          "calories": 165,
          "protein": 31,
          "fat": 3.6
        }
      }
    ],
    "nutritionalInfo": {
      "calories": 450,
      "protein": 25,
      "carbohydrates": 45,
      "fat": 12
    }
  }
}
```

#### Create Recipe

```http
POST /api/recipes
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "New Recipe",
  "description": "Recipe description",
  "instructions": "Step 1...",
  "prepTime": 30,
  "cookTime": 45,
  "servings": 4,
  "ingredients": [
    {
      "ingredientId": "uuid",
      "quantity": 500,
      "unit": "g"
    }
  ]
}
```

#### Update Recipe

```http
PUT /api/recipes/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Updated Recipe Name",
  "description": "Updated description",
  "instructions": "Updated instructions...",
  "prepTime": 35,
  "cookTime": 50,
  "servings": 6
}
```

#### Delete Recipe

```http
DELETE /api/recipes/{id}
Authorization: Bearer <token>
```

### Ingredient Management

#### Get All Ingredients

```http
GET /api/ingredients
Authorization: Bearer <token>
```

**Query Parameters:**

- `page` (optional): Page number
- `pageSize` (optional): Items per page
- `search` (optional): Search term
- `status` (optional): Filter by status

**Response:**

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "uuid",
        "name": "Chicken Breast",
        "description": "Boneless, skinless chicken breast",
        "status": "Curated",
        "nutritionalInfo": {
          "calories": 165,
          "protein": 31,
          "fat": 3.6,
          "carbohydrates": 0
        },
        "createdDate": "2025-07-30T10:00:00Z"
      }
    ],
    "totalCount": 50,
    "page": 1,
    "pageSize": 20
  }
}
```

#### Search Ingredients

```http
GET /api/ingredients/search?q=chicken
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "Chicken Breast",
      "description": "Boneless, skinless chicken breast",
      "nutritionalInfo": {
        "calories": 165,
        "protein": 31,
        "fat": 3.6
      }
    }
  ]
}
```

#### Create Ingredient

```http
POST /api/ingredients
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "New Ingredient",
  "description": "Ingredient description",
  "nutritionalInfo": {
    "calories": 100,
    "protein": 10,
    "fat": 5,
    "carbohydrates": 15
  }
}
```

### Curation Management

#### Get Curation Queue

```http
GET /api/curation/queue
Authorization: Bearer <token>
```

**Required Claims:** `CanManageCuration`

**Response:**

```json
{
  "success": true,
  "data": {
    "recipes": [
      {
        "id": "uuid",
        "name": "Recipe Name",
        "description": "Recipe description",
        "authorName": "John Doe",
        "submittedDate": "2025-07-30T10:00:00Z",
        "status": "PendingCuration"
      }
    ],
    "ingredients": [
      {
        "id": "uuid",
        "name": "Ingredient Name",
        "description": "Ingredient description",
        "authorName": "John Doe",
        "submittedDate": "2025-07-30T10:00:00Z",
        "status": "PendingCuration"
      }
    ]
  }
}
```

#### Approve Content

```http
POST /api/curation/approve
Authorization: Bearer <token>
Content-Type: application/json

{
  "contentId": "uuid",
  "contentType": "Recipe", // or "Ingredient"
  "publicNotes": "Great recipe!",
  "privateNotes": "Internal notes"
}
```

#### Reject Content

```http
POST /api/curation/reject
Authorization: Bearer <token>
Content-Type: application/json

{
  "contentId": "uuid",
  "contentType": "Recipe",
  "reason": "Inappropriate content",
  "notes": "Detailed rejection notes"
}
```

#### Request Revision

```http
POST /api/curation/request-revision
Authorization: Bearer <token>
Content-Type: application/json

{
  "contentId": "uuid",
  "contentType": "Recipe",
  "feedback": "Please add more detailed instructions",
  "notes": "Additional guidance for the author"
}
```

### Household Management

#### Get User Households

```http
GET /api/households
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "Smith Family",
      "description": "Our family household",
      "createdDate": "2025-07-30T10:00:00Z",
      "members": [
        {
          "id": "uuid",
          "name": "John Smith",
          "role": "Owner"
        }
      ]
    }
  ]
}
```

#### Create Household

```http
POST /api/households
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "New Household",
  "description": "Household description"
}
```

#### Invite Member

```http
POST /api/households/{id}/invite
Authorization: Bearer <token>
Content-Type: application/json

{
  "email": "member@example.com",
  "role": "Member"
}
```

### Shopping Lists

#### Get Shopping Lists

```http
GET /api/shopping-lists
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "Weekly Groceries",
      "description": "Weekly shopping list",
      "createdDate": "2025-07-30T10:00:00Z",
      "items": [
        {
          "id": "uuid",
          "name": "Milk",
          "quantity": 2,
          "unit": "L",
          "isCompleted": false
        }
      ]
    }
  ]
}
```

#### Create Shopping List

```http
POST /api/shopping-lists
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "New Shopping List",
  "description": "Shopping list description"
}
```

#### Add Item to Shopping List

```http
POST /api/shopping-lists/{id}/items
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "New Item",
  "quantity": 1,
  "unit": "kg"
}
```

### Meal Planning

#### Get Meal Plans

```http
GET /api/meal-plans
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "Weekly Meal Plan",
      "description": "This week's meals",
      "startDate": "2025-07-30",
      "endDate": "2025-08-05",
      "entries": [
        {
          "id": "uuid",
          "date": "2025-07-30",
          "mealType": "Dinner",
          "recipeId": "uuid",
          "recipeName": "Chicken Pasta"
        }
      ]
    }
  ]
}
```

#### Create Meal Plan

```http
POST /api/meal-plans
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "New Meal Plan",
  "description": "Meal plan description",
  "startDate": "2025-07-30",
  "endDate": "2025-08-05"
}
```

### Privacy Management

#### Get Privacy Settings

```http
GET /api/privacy/settings
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": {
    "consents": [
      {
        "type": "Marketing",
        "granted": true,
        "grantedDate": "2025-07-30T10:00:00Z"
      }
    ],
    "dataProcessingLogs": [
      {
        "id": "uuid",
        "operation": "DataAccess",
        "timestamp": "2025-07-30T10:00:00Z",
        "description": "User accessed profile data"
      }
    ]
  }
}
```

#### Update Privacy Settings

```http
PUT /api/privacy/settings
Authorization: Bearer <token>
Content-Type: application/json

{
  "consents": [
    {
      "type": "Marketing",
      "granted": false
    }
  ]
}
```

#### Request Data Export

```http
POST /api/privacy/export
Authorization: Bearer <token>
```

**Response:**

```json
{
  "success": true,
  "data": {
    "requestId": "uuid",
    "status": "Processing",
    "estimatedCompletion": "2025-07-30T12:00:00Z"
  }
}
```

## **Error Handling**

### Standard Error Response

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid input data",
    "details": {
      "field": "email",
      "message": "Email is required"
    }
  }
}
```

### Common Error Codes

- `VALIDATION_ERROR` - Input validation failed
- `AUTHENTICATION_ERROR` - Invalid or missing authentication
- `AUTHORIZATION_ERROR` - Insufficient permissions
- `NOT_FOUND` - Resource not found
- `CONFLICT` - Resource conflict (e.g., duplicate email)
- `INTERNAL_ERROR` - Server error

### HTTP Status Codes

- `200` - Success
- `201` - Created
- `400` - Bad Request
- `401` - Unauthorized
- `403` - Forbidden
- `404` - Not Found
- `409` - Conflict
- `500` - Internal Server Error

## **Pagination**

### Standard Pagination Response

```json
{
  "success": true,
  "data": {
    "items": [...],
    "totalCount": 100,
    "page": 1,
    "pageSize": 20,
    "totalPages": 5
  }
}
```

### Pagination Parameters

- `page` - Page number (1-based)
- `pageSize` - Items per page (default: 20, max: 100)

## **Search and Filtering**

### Search Parameters

- `search` - Text search across relevant fields
- `status` - Filter by status
- `dateFrom` - Filter by start date
- `dateTo` - Filter by end date
- `authorId` - Filter by author

### Example Search Request

```http
GET /api/recipes?search=chicken&status=Curated&page=1&pageSize=10
Authorization: Bearer <token>
```

## **Usage Examples**

### Angular Service Example

```typescript
@Injectable({
  providedIn: "root",
})
export class RecipeService {
  private apiUrl = `${environment.apiUrl}/recipes`;

  constructor(private http: HttpClient) {}

  getRecipes(params?: any): Observable<any> {
    return this.http.get(this.apiUrl, { params });
  }

  getRecipe(id: string): Observable<any> {
    return this.http.get(`${this.apiUrl}/${id}`);
  }

  createRecipe(recipe: any): Observable<any> {
    return this.http.post(this.apiUrl, recipe);
  }

  updateRecipe(id: string, recipe: any): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}`, recipe);
  }

  deleteRecipe(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
```

### Error Handling Example

```typescript
this.recipeService.getRecipes().subscribe({
  next: (response) => {
    if (response.success) {
      this.recipes = response.data.items;
    }
  },
  error: (error) => {
    if (error.status === 401) {
      // Handle authentication error
      this.authService.logout();
    } else if (error.status === 403) {
      // Handle authorization error
      this.notificationService.showError("Insufficient permissions");
    } else {
      // Handle other errors
      this.notificationService.showError(
        error.error?.message || "An error occurred"
      );
    }
  },
});
```

---

_Last Updated: July 30, 2025_  
_Version: 1.0_  
_Status: Active Development_
