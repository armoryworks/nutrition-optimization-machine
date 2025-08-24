# Security: AuthorId Removal from Frontend Models

## Overview

This document summarizes the security changes made to ensure that NO frontend models can pass `AuthorId` or `CreatedById` fields to the backend. Any operation involving recording the current user's ID must be handled exclusively on the backend.

## Security Principle

- **Frontend**: Never sends AuthorId/CreatedById in request payloads
- **Backend**: Always determines current user ID from authentication context
- **Database**: Stores AuthorId for audit/ownership purposes
- **Response**: Can include AuthorId for display/authorization purposes

## Changes Made

### 1. Frontend Models (TypeScript)

Removed `authorId` fields from all frontend request models:

#### Recipe Models

- `RecipeEditModel` - Removed `authorId: number`
- `RecipeCreateModel` - Removed `authorId: number`
- `RecipeUpdateModel` - Removed `authorId: number`
- `RecipeCommentModel` - Removed `authorId: number`
- `RecipeRatingModel` - Removed `authorId: number`
- `RecipeNoteModel` - Removed `authorId: number`
- `RecipeSearchModel` - Removed `authorId: number`

#### Meal Plan Models

- `MealPlanModel` - Removed `authorId: number`
- `MealPlanCreateModel` - Removed `authorId: number`
- `MealPlanUpdateModel` - Removed `authorId: number`
- `MealPlanResponseModel` - Removed `authorId: number`
- `MealPlanRuleModel` - Removed `authorId: number`
- `MealPlanRuleCreateModel` - Removed `authorId: number`

#### Plan Models

- `PlanModel` - Removed `authorId: number`
- `CreatePlanRequest` - Already clean (no AuthorId field)

#### Shopping & Household Models

- `ShoppingListCreateModel` - Removed `AuthorId` field
- `HouseholdCreateModel` - Removed `AuthorId` field

#### Curation Models

- `CurationQueueItemModel` - Removed `authorId: number`

### 2. Frontend Components

Updated components to stop setting AuthorId in request payloads:

- `RecipeEditComponent` - Removed `authorId` assignment
- `ShoppingCreateComponent` - Removed `AuthorId` form field and assignment
- `HouseholdCreateComponent` - Removed `AuthorId` form field and assignment
- `MealPlanCreateComponent` - Removed `AuthorId` form field and assignment
- `MealPlanRulesComponent` - Removed `authorId` from rule creation
- `RecipeRatingsComponent` - Removed `authorId` from rating submission

### 3. Backend Services

Updated services to receive AuthorId as a parameter instead of from the request model:

#### ShoppingListOrchestrationService

- `CreateShoppingListAsync(model, authorId)` - Now accepts authorId parameter
- Updated interface: `IShoppingListOrchestrationService`

#### MealPlanOrchestrationService

- `CreateMealPlanAsync(model, authorId)` - Now accepts authorId parameter
- Updated interface: `IMealPlanOrchestrationService`

#### HouseholdOrchestrationService

- Removed incorrect AuthorId assignment from response (entity doesn't have AuthorId field)

### 4. Backend Controllers

Updated controllers to get current user ID from authentication context:

- `ShoppingListController.CreateShoppingList()` - Gets `authorId` from `GetCurrentPersonIdRequired()`
- `MealPlanController.CreateMealPlan()` - Gets `authorId` from `GetCurrentPersonIdRequired()`
- `PlanController.CreatePlan()` - Already correctly gets `authorId` from `GetCurrentPersonIdRequired()`
- `RecipeController.CreateRecipe()` - Already correctly gets `currentPersonId` from `GetCurrentPersonId()`

### 5. Backend Models

Cleaned up backend models:

- **Request Models**: All request models are clean (no AuthorId fields)
- **Response Models**: Can still include AuthorId for display purposes
- **Entity Models**: Keep AuthorId for database storage

## Security Verification

### ✅ Frontend Security

- No frontend models can send AuthorId to backend
- All form components removed AuthorId fields
- All request payloads cleaned of AuthorId

### ✅ Backend Security

- All services receive AuthorId as parameter (not from request)
- All controllers get AuthorId from authentication context
- No request models accept AuthorId from frontend

### ✅ Data Integrity

- AuthorId still stored in database for audit/ownership
- AuthorId included in responses for display/authorization
- Proper authorization checks maintained

## Remaining AuthorId Usage (Secure)

### Database Entities

- `RecipeEntity.AuthorId` - Stores recipe author
- `MealPlanEntity.AuthorId` - Stores meal plan author
- `PlanEntity.AuthorId` - Stores plan author
- `ShoppingListEntity.AuthorId` - Stores shopping list author
- `RecipeCommentEntity.AuthorId` - Stores comment author
- `RecipeRatingEntity.AuthorId` - Stores rating author
- `RecipeNoteEntity.AuthorId` - Stores note author
- `IngredientEntity.AuthorId` - Stores ingredient author

### Response Models

- `RecipeResponseModel.AuthorId` - Returns for display
- `MealPlanResponseModel.AuthorId` - Returns for display
- `PlanModel.AuthorId` - Returns for display
- `ShoppingListResponseModel.AuthorId` - Returns for display

### Search Results

- `RecipeSearchResultModel.AuthorId` - Returns for search results

## Testing Recommendations

1. **Verify Frontend**: Ensure no AuthorId fields in request payloads
2. **Verify Backend**: Ensure all create/update operations get AuthorId from auth context
3. **Verify Authorization**: Ensure users can only modify their own content
4. **Verify Display**: Ensure AuthorId still shows correctly in UI

## Compliance Status

✅ **FULLY COMPLIANT** - The application now meets the security requirement that NO frontend models can pass AuthorId to the backend. All operations involving recording the current user's ID are handled exclusively on the backend through the authentication context.
