# NOM API Reference

## Authentication

All API endpoints require JWT authentication unless specified otherwise.

### Login

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "password123"
}
```

### JWT Token

Include the JWT token in the Authorization header:

```http
Authorization: Bearer <your_jwt_token>
```

## Core Endpoints

### Recipes

#### Get Recipes

```http
GET /api/recipes?page=1&perPage=20&search=chicken
```

#### Create Recipe

```http
POST /api/recipes
Content-Type: application/json

{
  "name": "Chicken Parmesan",
  "description": "Classic Italian dish",
  "ingredients": [...],
  "instructions": [...]
}
```

### AI Features

#### Get Recipe Suggestions

```http
GET /api/recipe-suggestions/suggestions?ingredientIds=1,2,3
```

#### Generate AI Suggestions

```http
POST /api/recipe-suggestions/ai-suggestions
Content-Type: application/json

{
  "description": "Quick dinner with chicken and vegetables",
  "dietaryRestrictions": ["vegetarian"],
  "cookingTime": 30
}
```

## Health Endpoints

### Application Health

```http
GET /health
```

### Database Health

```http
GET /health
# Includes database connectivity check
```

## Rate Limiting

- **General API**: 100 requests per minute
- **Recipe endpoints**: 50 requests per minute
- **AI endpoints**: 20 requests per minute

## Error Handling

All errors follow this format:

```json
{
  "error": "Error message",
  "details": "Additional details",
  "timestamp": "2024-01-01T00:00:00Z",
  "requestId": "unique-request-id"
}
```

## Status Codes

- `200` - Success
- `201` - Created
- `400` - Bad Request
- `401` - Unauthorized
- `403` - Forbidden
- `404` - Not Found
- `429` - Too Many Requests
- `500` - Internal Server Error
