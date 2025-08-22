# UI Data Dynamic Conversion Plan

## Overview

This document outlines the plan to replace all hardcoded data in the UI components with dynamic data fetched from the backend API. The goal is to eliminate hardcoded arrays and objects that populate dropdowns, radio buttons, and other form controls, making the application more maintainable and data-driven.

## Current Status

- ✅ **Measurement System**: Already converted to dynamic backend data
- ❌ **All Other Systems**: Still using hardcoded data

## Critical Issues: String Values & Magic Numbers

The following components are using string values, magic numbers, or hardcoded identifiers instead of proper data-backed numeric IDs. These create maintenance issues and should be converted to use database-driven reference data.

### 1.1 Shopping Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `shopping-item-dialog.component.ts`:
  - Priority values: `'low'`, `'medium'`, `'high'` (should be numeric IDs)
  - Mode checks: `'add'`, `'edit'` (should be enum values)
  - Unit values: `'pieces'`, `'pounds'`, `'ounces'` (should be numeric IDs)

### 1.2 Meal Planning Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `meal-plan-rules.component.ts`:
  - Meal type values: `'breakfast'`, `'lunch'`, `'dinner'`, `'snack'` (should be numeric IDs)
  - Day of week values: `'Monday'`, `'Tuesday'`, etc. (should be numeric IDs)
- `meal-plan-edit.component.ts`:
  - Meal type values: `'breakfast'`, `'lunch'`, `'dinner'`, `'snack'` (should be numeric IDs)
- `meal-plan-create.component.ts`:
  - Meal type values: `'breakfast'`, `'lunch'`, `'dinner'`, `'snack'` (should be numeric IDs)

### 1.3 Recipe Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `recipe-suggestions.component.ts`:
  - Difficulty values: `'Easy'`, `'Medium'`, `'Hard'` (should be numeric IDs)
  - Cuisine values: `'Italian'`, `'Mexican'`, `'Asian'`, etc. (should be numeric IDs)
  - Meal type values: `'Breakfast'`, `'Lunch'`, `'Dinner'`, `'Snack'`, `'Dessert'` (should be numeric IDs)
  - Dietary values: `'Vegetarian'`, `'Vegan'`, `'Gluten-Free'`, etc. (should be numeric IDs)
- `recipe-search.component.ts`:
  - Sort options: `'relevance'`, `'rating'`, `'name'`, `'prepTime'`, `'cookTime'` (should be numeric IDs)
  - Sort directions: `'asc'`, `'desc'` (should be numeric IDs)

### 1.4 Person Health Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `person-health-edit.component.ts`:
  - Activity level values: `'sedentary'`, `'lightly_active'`, `'moderately_active'`, etc. (should be numeric IDs)
  - Dietary restriction values: `'none'`, `'vegetarian'`, `'vegan'`, etc. (should be numeric IDs)
  - Health goal values: `'weight_loss'`, `'weight_gain'`, `'maintenance'`, etc. (should be numeric IDs)

### 1.5 Restriction Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `medical-restriction.component.ts`:
  - Allergy values: `'Peanuts'`, `'Tree Nuts'`, `'Dairy'`, etc. (should be numeric IDs)
  - Medical condition values: `'Celiac Disease'`, `'Lactose Intolerance'`, etc. (should be numeric IDs)
  - Gastrointestinal condition values: `"Crohn's Disease"`, `'IBS'`, etc. (should be numeric IDs)
- `societal-restriction.component.ts`:
  - Religious/ethical values: `'Vegan'`, `'Vegetarian'`, `'Kosher'`, `'Halal'`, etc. (should be numeric IDs)
- `personal-preference.component.ts`:
  - Spice level values: `'Mild'`, `'Medium'`, `'Spicy'`, `'Very Spicy'` (should be numeric IDs)
  - Texture values: `'Creamy'`, `'Crunchy'`, `'Smooth'`, etc. (should be numeric IDs)
  - Cooking method values: `'Baked'`, `'Grilled'`, `'Fried'`, etc. (should be numeric IDs)

### 1.6 Onboarding Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `onboarding-restriction-scope.component.ts`:
  - Restriction scope values: `'plan'`, `'specific'` (should be numeric IDs)
  - Sub-step values: `'selectScope'`, `'selectPeople'` (should be numeric IDs)
- `onboarding-workflow.component.ts`:
  - Step ID values: `'healthAttributes'`, `'societalRestrictions'`, `'personalPreferences'`, etc. (should be numeric IDs)
  - Restriction scope values: `'plan'`, `'specific'` (should be numeric IDs)
  - Invitation code: `'PLAN123'` (should be configurable)

### 1.7 Curation Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `curation-queue.component.ts`:
  - Entity type values: `'Recipe'`, `'Ingredient'` (should be numeric IDs)
  - Action type values: `'approve'`, `'revision'` (should be numeric IDs)

### 1.8 Common Components

**Status**: ❌ Not Started
**Components with String Value Issues**:

- `format-mass.pipe.ts`:
  - Mass unit values: `'kg'`, `'g'`, `'mg'`, `'µg'`, `'mcg'` (should use measurement system IDs)

## Phase 1: Backend API Endpoints (Priority: HIGH)

### 1.1 Reference System API (Primary Data Source)

**Status**: ❌ Not Started
**Files to Create/Modify**:

- `nom-api/Nom.Api/Controllers/ReferenceController.cs` - Extend existing controller
- `nom-api/Nom.Data/Reference/_ReferenceDiscriminatorEnum.cs` - Add new reference groups
- `nom-api/Nom.Data/Reference/` - Create new view entities for each reference group

**Endpoints Needed**:

- `GET /api/Reference/{discriminatorId}/all` - Get all references for a specific group
- `GET /api/Reference/bulk` - Get multiple reference groups in one call for performance

**Reference Groups to Add**:

- `ShoppingPriorityType` (ID: 6000) - For shopping priority levels
- `ShoppingCategoryType` (ID: 6001) - For shopping categories
- `ShoppingUnitType` (ID: 6002) - For shopping units (or use existing measurement system)
- `RecipeDifficultyType` (ID: 6003) - For recipe difficulty levels
- `PersonActivityLevelType` (ID: 6004) - For person activity levels
- `PersonDietaryRestrictionType` (ID: 6005) - For dietary restrictions
- `PersonHealthGoalType` (ID: 6006) - For health goals
- `AllergyType` (ID: 6007) - For allergy types
- `MedicalConditionType` (ID: 6008) - For medical conditions
- `SocietalRestrictionType` (ID: 6009) - For religious/ethical restrictions
- `PersonalPreferenceType` (ID: 6010) - For personal preferences
- `OnboardingStepType` (ID: 6011) - For onboarding workflow steps
- `CurationActionType` (ID: 6012) - For curation actions
- `SortOptionType` (ID: 6013) - For search/sort options
- `SortDirectionType` (ID: 6014) - For sort directions

**Existing Reference Groups to Leverage**:

- `MealType` (ID: 1) - Already exists for meal types
- `CuisineType` (ID: 3001) - Already exists for cuisine types
- `RestrictionType` (ID: 2000) - Already exists for restriction types
- `GoalType` (ID: 2001) - Already exists for goal types

### 1.2 Meal Planning API

**Status**: ❌ Not Started
**Files to Create/Modify**:

- `nom-api/Nom.Data/Reference/_ReferenceDiscriminatorEnum.cs` - Add DayOfWeekType
- `nom-api/Nom.Data/Reference/DayOfWeekTypeViewEntity.cs` - Create view entity

**Endpoints Needed**:

- `GET /api/Reference/{(long)ReferenceDiscriminatorEnum.MealType}/all` - Get all meal types (existing)
- `GET /api/Reference/{(long)ReferenceDiscriminatorEnum.DayOfWeekType}/all` - Get all days of week

**Reference Groups to Add**:

- `DayOfWeekType` (ID: 6015) - For days of week

**Existing Reference Groups to Leverage**:

- `MealType` (ID: 1) - Already exists for meal types

### 1.3 Recipe & Cuisine API

**Status**: ❌ Not Started
**Files to Create/Modify**:

- `nom-api/Nom.Data/Reference/_ReferenceDiscriminatorEnum.cs` - Add new reference groups
- `nom-api/Nom.Data/Reference/RecipeDifficultyTypeViewEntity.cs` - Create view entity
- `nom-api/Nom.Data/Reference/RecipeDietaryOptionTypeViewEntity.cs` - Create view entity

**Endpoints Needed**:

- `GET /api/Reference/{(long)ReferenceDiscriminatorEnum.RecipeDifficultyType}/all` - Get all difficulty levels
- `GET /api/Reference/{(long)ReferenceDiscriminatorEnum.CuisineType}/all` - Get all cuisine types (existing)
- `GET /api/Reference/{(long)ReferenceDiscriminatorEnum.MealType}/all` - Get all meal types (existing)
- `GET /api/Reference/{(long)ReferenceDiscriminatorEnum.RecipeDietaryOptionType}/all` - Get all dietary options

**Reference Groups to Add**:

- `RecipeDifficultyType` (ID: 6003) - For recipe difficulty levels
- `RecipeDietaryOptionType` (ID: 6016) - For recipe dietary options

**Existing Reference Groups to Leverage**:

- `CuisineType` (ID: 3001) - Already exists for cuisine types
- `MealType` (ID: 1) - Already exists for meal types

### 1.4 Person Health & Restrictions API

**Status**: ❌ Not Started
**Files to Create/Modify**:

- `nom-api/Nom.Api/Controllers/Person/PersonActivityLevelController.cs`
- `nom-api/Nom.Api/Controllers/Person/PersonDietaryRestrictionController.cs`
- `nom-api/Nom.Api/Controllers/Person/PersonHealthGoalController.cs`
- `nom-api/Nom.Api/Controllers/Restriction/AllergyController.cs`
- `nom-api/Nom.Api/Controllers/Restriction/MedicalConditionController.cs`
- `nom-api/Nom.Api/Controllers/Restriction/SocietalRestrictionController.cs`
- `nom-api/Nom.Api/Controllers/Restriction/PersonalPreferenceController.cs`

**Endpoints Needed**:

- `GET /api/PersonActivityLevel/all` - Get all activity levels
- `GET /api/PersonDietaryRestriction/all` - Get all dietary restrictions
- `GET /api/PersonHealthGoal/all` - Get all health goals
- `GET /api/Allergy/all` - Get all allergy types
- `GET /api/MedicalCondition/all` - Get all medical conditions
- `GET /api/SocietalRestriction/all` - Get all societal restrictions
- `GET /api/PersonalPreference/all` - Get all personal preference options

**Database Tables**: Need to create person health and restriction reference tables

### 1.5 Domain-Specific Data APIs (Exceptions to Reference System)
**Status**: ❌ Not Started
**Files to Create/Modify**:

#### 1.5.1 Shopping Domain
- `nom-api/Nom.Api/Controllers/Shopping/ShoppingUnitController.cs` - For shopping-specific units
- **Reason**: Shopping units have different business logic than measurement units (e.g., "cans", "boxes", "bottles")

#### 1.5.2 Onboarding Domain  
- `nom-api/Nom.Api/Controllers/Onboarding/OnboardingWorkflowController.cs` - For workflow step configuration
- **Reason**: Onboarding steps have complex business logic, dependencies, and workflow state management

#### 1.5.3 Curation Domain
- `nom-api/Nom.Api/Controllers/Curation/CurationWorkflowController.cs` - For curation workflow configuration
- **Reason**: Curation actions have complex approval workflows and business rules

#### 1.5.4 System Configuration Domain
- `nom-api/Nom.Api/Controllers/System/SystemConfigurationController.cs` - For system-wide settings
- **Reason**: System configuration includes non-reference data like feature flags, limits, and environment-specific values

**Endpoints Needed**:
- `GET /api/ShoppingUnit/all` - Get shopping-specific units (separate from measurement system)
- `GET /api/OnboardingWorkflow/steps` - Get onboarding workflow configuration
- `GET /api/CurationWorkflow/actions` - Get curation workflow configuration  
- `GET /api/SystemConfiguration/{category}` - Get system configuration values

**Data to Include**:
- **Shopping Units**: cans, boxes, bottles, packages, containers (business-specific, not measurement units)
- **Onboarding Workflow**: step dependencies, validation rules, conditional logic
- **Curation Workflow**: approval chains, role requirements, business rules
- **System Configuration**: feature flags, limits, thresholds, environment settings

## Phase 2: Database Schema & Seeding (Priority: HIGH)

### 2.0 Convert String Values to Numeric IDs

**Status**: ❌ Not Started
**Priority**: CRITICAL

**Database Tables to Create**:

- Use existing `reference.Reference` table for all reference data
- Use existing `reference.Group` table for reference group definitions
- Use existing `reference.ReferenceIndex` table for reference-group relationships
- Use existing `reference.ReferenceGroupView` view for TPH inheritance

**String Value Conversions Needed**:

1. **Shopping Priorities**: `'low'` → ID 1, `'medium'` → ID 2, `'high'` → ID 3
2. **Meal Types**: `'breakfast'` → ID 1, `'lunch'` → ID 2, `'dinner'` → ID 3, `'snack'` → ID 4
3. **Days of Week**: `'Monday'` → ID 1, `'Tuesday'` → ID 2, etc.
4. **Recipe Difficulties**: `'Easy'` → ID 1, `'Medium'` → ID 2, `'Hard'` → ID 3
5. **Activity Levels**: `'sedentary'` → ID 1, `'lightly_active'` → ID 2, etc.
6. **Dietary Restrictions**: `'none'` → ID 1, `'vegetarian'` → ID 2, etc.
7. **Allergy Types**: `'Peanuts'` → ID 200, `'Tree Nuts'` → ID 201, etc.
8. **Medical Conditions**: `'Celiac Disease'` → ID 200, `'Lactose Intolerance'` → ID 201, etc.

**Files to Modify**:

- `_CustomMigration.cs` - Add seeding for all enum values
- All component files - Replace string values with numeric IDs
- All service files - Update to use numeric IDs in API calls

### 2.1 Create Reference Tables

**Status**: ❌ Not Started
**Tables to Create**:

- **No new tables needed** - Use existing reference system
- **New Reference Groups to add to `reference.Group`**:
  - `ShoppingPriorityType` (ID: 6000)
  - `ShoppingCategoryType` (ID: 6001)
  - `ShoppingUnitType` (ID: 6002) - Or leverage existing measurement system
  - `RecipeDifficultyType` (ID: 6003)
  - `PersonActivityLevelType` (ID: 6004)
  - `PersonDietaryRestrictionType` (ID: 6005)
  - `PersonHealthGoalType` (ID: 6006)
  - `AllergyType` (ID: 6007)
  - `MedicalConditionType` (ID: 6008)
  - `SocietalRestrictionType` (ID: 6009)
  - `PersonalPreferenceType` (ID: 6010)
  - `OnboardingStepType` (ID: 6011)
  - `CurationActionType` (ID: 6012)
  - `SortOptionType` (ID: 6013)
  - `SortDirectionType` (ID: 6014)
  - `DayOfWeekType` (ID: 6015)
  - `RecipeDietaryOptionType` (ID: 6016)

**New View Entities to Create**:

- `ShoppingPriorityTypeViewEntity.cs`
- `ShoppingCategoryTypeViewEntity.cs`
- `ShoppingUnitTypeViewEntity.cs` (or use measurement system)
- `RecipeDifficultyTypeViewEntity.cs`
- `PersonActivityLevelTypeViewEntity.cs`
- `PersonDietaryRestrictionTypeViewEntity.cs`
- `PersonHealthGoalTypeViewEntity.cs`
- `AllergyTypeViewEntity.cs`
- `MedicalConditionTypeViewEntity.cs`
- `SocietalRestrictionTypeViewEntity.cs`
- `PersonalPreferenceTypeViewEntity.cs`
- `OnboardingStepTypeViewEntity.cs`
- `CurationActionTypeViewEntity.cs`
- `SortOptionTypeViewEntity.cs`
- `SortDirectionTypeViewEntity.cs`
- `DayOfWeekTypeViewEntity.cs`
- `RecipeDietaryOptionTypeViewEntity.cs`

### 2.2 Seed Reference Data

**Status**: ❌ Not Started
**Files to Modify**:

- `nom-api/Nom.Data/_CustomMigration.cs` - Add seeding for all new reference groups and references
- `nom-api/Nom.Data/Reference/_ReferenceDiscriminatorEnum.cs` - Add new enum values
- `nom-api/Nom.Data/ApplicationDbContext.cs` - Add new view entity discriminators

**Data to Seed**:

- **Reference Groups** (in `reference.Group` table):

  - All new reference group types with proper IDs and descriptions

- **Reference Data** (in `reference.Reference` table):

  - Shopping priorities: Low, Medium, High
  - Shopping categories: Produce, Dairy, Meat, Pantry, Frozen, Beverages, Snacks, Household, Other
  - Shopping units: pieces, pounds, ounces, grams, kilograms, cups, tablespoons, teaspoons, liters, milliliters, bottles, cans, boxes, bags
  - Recipe difficulties: Easy, Medium, Hard
  - Person activity levels: Sedentary, Lightly Active, Moderately Active, Very Active, Extremely Active
  - Dietary restrictions: None, Vegetarian, Vegan, Gluten-Free, Dairy-Free, Keto, Paleo
  - Health goals: Weight Loss, Weight Gain, Maintenance, Muscle Gain, General Health
  - Allergy types: Peanuts, Tree Nuts, Dairy, Eggs, Wheat, Soy, Fish, Shellfish
  - Medical conditions: Celiac Disease, Lactose Intolerance, Diabetes Type 1, Diabetes Type 2, High Blood Pressure, High Cholesterol, Gout, Anemia, Pregnancy
  - Societal restrictions: Vegan, Vegetarian, Kosher, Halal, Pescatarian
  - Personal preferences: Mild, Medium, Spicy, Very Spicy (spice levels)
  - Onboarding steps: healthAttributes, societalRestrictions, personalPreferences, applyIndividualPreferences, summary
  - Curation actions: approve, revision
  - Sort options: relevance, rating, name, prepTime, cookTime
  - Sort directions: asc, desc
  - Days of week: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday

- **Reference Indexes** (in `reference.ReferenceIndex` table):
  - Link all references to their appropriate groups

## Phase 3: Frontend Service Layer (Priority: MEDIUM)

### 3.1 Create Data Services

**Status**: ❌ Not Started
**Files to Create**:

- `nom-ui/src/app/common/services/reference-data.service.ts`
- `nom-ui/src/app/shopping/services/shopping-reference.service.ts`
- `nom-ui/src/app/meal-plan/services/meal-plan-reference.service.ts`
- `nom-ui/src/app/recipe/services/recipe-reference.service.ts`
- `nom-ui/src/app/person/services/person-reference.service.ts`
- `nom-ui/src/app/restriction/services/restriction-reference.service.ts`

### 3.2 Implement Caching Strategy

**Status**: ❌ Not Started
**Approach**:

- Cache reference data in services
- Refresh on application startup
- Provide fallback to hardcoded data during development

## Phase 4: Frontend Component Updates (Priority: MEDIUM)

### 4.0 String Value to Numeric ID Conversion

**Status**: ❌ Not Started
**Priority**: CRITICAL

**Components Requiring String Value Conversion**:

#### 4.0.1 Shopping Components

- `shopping-item-dialog.component.ts`:
  - Replace `'low'`, `'medium'`, `'high'` with numeric IDs from API
  - Replace `'add'`, `'edit'` mode checks with enum values
  - Replace unit strings with numeric IDs from measurement system

#### 4.0.2 Meal Planning Components

- `meal-plan-rules.component.ts`:
  - Replace meal type strings with numeric IDs from API
  - Replace day of week strings with numeric IDs from API
- `meal-plan-edit.component.ts` & `meal-plan-create.component.ts`:
  - Replace meal type strings with numeric IDs from API

#### 4.0.3 Recipe Components

- `recipe-suggestions.component.ts`:
  - Replace difficulty strings with numeric IDs from API
  - Replace cuisine strings with numeric IDs from API
  - Replace meal type strings with numeric IDs from API
  - Replace dietary strings with numeric IDs from API
- `recipe-search.component.ts`:
  - Replace sort option strings with numeric IDs from API
  - Replace sort direction strings with numeric IDs from API

#### 4.0.4 Person Health Components

- `person-health-edit.component.ts`:
  - Replace activity level strings with numeric IDs from API
  - Replace dietary restriction strings with numeric IDs from API
  - Replace health goal strings with numeric IDs from API

#### 4.0.5 Restriction Components

- `medical-restriction.component.ts`:
  - Replace allergy strings with numeric IDs from API
  - Replace medical condition strings with numeric IDs from API
  - Replace gastrointestinal condition strings with numeric IDs from API
- `societal-restriction.component.ts`:
  - Replace religious/ethical strings with numeric IDs from API
- `personal-preference.component.ts`:
  - Replace spice level strings with numeric IDs from API
  - Replace texture strings with numeric IDs from API
  - Replace cooking method strings with numeric IDs from API

#### 4.0.6 Onboarding Components

- `onboarding-restriction-scope.component.ts`:
  - Replace restriction scope strings with numeric IDs from API
  - Replace sub-step strings with numeric IDs from API
- `onboarding-workflow.component.ts`:
  - Replace step ID strings with numeric IDs from API
  - Replace restriction scope strings with numeric IDs from API
  - Make invitation code configurable

#### 4.0.7 Curation Components

- `curation-queue.component.ts`:
  - Replace entity type strings with numeric IDs from API
  - Replace action type strings with numeric IDs from API

#### 4.0.8 Common Components

- `format-mass.pipe.ts`:
  - Replace hardcoded mass unit strings with measurement system IDs

### 4.1 Shopping Components

**Status**: ❌ Not Started
**Components to Update**:

- `shopping-item-dialog.component.ts` - Replace hardcoded categories, units, priorities
- `shopping-category-management.component.ts` - Use dynamic categories

### 4.2 Meal Planning Components

**Status**: ❌ Not Started
**Components to Update**:

- `meal-plan-rules.component.ts` - Replace hardcoded meal types, days
- `meal-plan-edit.component.ts` - Replace hardcoded meal types
- `meal-plan-create.component.ts` - Replace hardcoded meal types

### 4.3 Recipe Components

**Status**: ❌ Not Started
**Components to Update**:

- `recipe-suggestions.component.ts` - Replace hardcoded difficulties, cuisines, meal types, dietary options
- `ingredient-form.component.ts` - Already using dynamic measurements ✅

### 4.4 Person & Health Components

**Status**: ❌ Not Started
**Components to Update**:

- `person-health-edit.component.ts` - Replace hardcoded activity levels, dietary restrictions, health goals

### 4.5 Restriction Components

**Status**: ❌ Not Started
**Components to Update**:

- `medical-restriction.component.ts` - Replace hardcoded allergies, medical conditions, micronutrients
- `societal-restriction.component.ts` - Replace hardcoded religious/ethical options
- `personal-preference.component.ts` - Replace hardcoded spice levels, textures, cooking methods

### 4.6 Common Components

**Status**: ❌ Not Started
**Components to Update**:

- `format-mass.pipe.ts` - Replace hardcoded mass units with dynamic measurement data

## Phase 5: Testing & Validation (Priority: MEDIUM)

### 5.1 Backend Testing

**Status**: ❌ Not Started
**Tests to Create**:

- Unit tests for all new controllers
- Integration tests for API endpoints
- Database seeding validation tests

### 5.2 Frontend Testing

**Status**: ❌ Not Started
**Tests to Update**:

- Component tests to use mocked reference data services
- Service tests for new reference data services

### 5.3 End-to-End Testing

**Status**: ❌ Not Started
**Scenarios to Test**:

- All dropdowns populate correctly
- Form submissions work with new data structure
- Caching works properly
- Fallback mechanisms work during errors

## Phase 6: Documentation & Cleanup (Priority: LOW)

### 6.1 Update Documentation

**Status**: ❌ Not Started
**Documents to Update**:

- API documentation
- Component usage guides
- Database schema documentation

### 6.2 Remove Hardcoded Data

**Status**: ❌ Not Started
**Cleanup Tasks**:

- Remove all hardcoded arrays and objects
- Remove unused imports
- Clean up any temporary fallback code

## Execution Tracking

### Week 1: Backend Foundation

- [ ] Create database schema for reference tables
- [ ] Create system.EnumValue and system.EnumType tables for string value conversion
- [ ] Implement basic controllers for shopping, meal planning, and recipe data
- [ ] Create initial seeding in `_CustomMigration.cs`
- [ ] Begin string value to numeric ID conversion planning

### Week 2: Core API Endpoints

- [ ] Implement person health and restriction APIs
- [ ] Complete all reference data seeding
- [ ] Complete string value to numeric ID conversion in database
- [ ] Test all API endpoints
- [ ] Validate string value conversions

### Week 3: Frontend Services

- [ ] Create reference data services
- [ ] Implement caching strategy
- [ ] Begin string value to numeric ID conversion in components
- [ ] Update shopping and meal planning components

### Week 4: Component Updates

- [ ] Complete string value to numeric ID conversion in all components
- [ ] Update recipe and person components
- [ ] Update restriction components
- [ ] Update common components and pipes
- [ ] Validate all string value conversions work correctly

### Week 5: Testing & Validation

- [ ] Complete backend testing
- [ ] Complete frontend testing
- [ ] End-to-end validation

### Week 6: Documentation & Cleanup

- [ ] Update documentation
- [ ] Remove hardcoded data
- [ ] Final testing and validation

## Risk Assessment

### High Risk

- **Database Schema Changes**: May require migration strategy for existing data
- **API Breaking Changes**: Frontend components may break during transition

### Medium Risk

- **Performance Impact**: Multiple API calls for reference data
- **Caching Complexity**: Need to ensure data consistency

### Low Risk

- **UI Behavior Changes**: Users may notice different data options
- **Testing Coverage**: Need comprehensive testing for all scenarios

## Success Criteria

1. **Zero Hardcoded Data**: All UI elements use dynamic backend data
2. **Performance**: Reference data loads within acceptable time limits
3. **Reliability**: Fallback mechanisms work during API failures
4. **Maintainability**: New reference data can be added without code changes
5. **Testing**: 90%+ test coverage for new functionality

## Notes

- The measurement system conversion serves as a template for other conversions
- **Use existing reference system architecture** - No need for new controllers or enum tables
- **Leverage existing infrastructure**:
  - `reference.Reference` table for all reference data
  - `reference.Group` table for reference group definitions
  - `reference.ReferenceIndex` table for reference-group relationships
  - `reference.ReferenceGroupView` view for TPH inheritance
  - `ReferenceController` for all reference data endpoints
- Consider implementing a bulk reference data endpoint for initial page loads
- Plan for internationalization (i18n) if needed in the future
- Document all new API endpoints for frontend developers

## Existing Reference System Architecture

The system already has a robust reference data architecture:

1. **Reference Tables**:

   - `reference.Reference` - Stores individual reference items (Name, Description, etc.)
   - `reference.Group` - Stores reference group definitions (MealType, CuisineType, etc.)
   - `reference.ReferenceIndex` - Links references to groups (many-to-many relationship)

2. **View System**:

   - `reference.ReferenceGroupView` - SQL view that joins the three tables
   - Provides unified access to reference data with group context

3. **Entity Framework TPH Inheritance**:

   - `GroupedReferenceViewEntity` - Base abstract class
   - Specific view entities (MealTypeViewEntity, CuisineTypeViewEntity, etc.)
   - Discriminated by `GroupId` in ApplicationDbContext

4. **API Layer**:
   - `ReferenceController` - Single controller for all reference data
   - `IReferenceOrchestrationService` - Service layer for reference operations

This architecture eliminates the need for:

- Multiple controllers for different reference types
- Custom enum tables
- Separate seeding mechanisms
- Complex data access patterns

## String Value Conversion Priority Order

### High Priority (Week 1-2)

1. **Shopping Priorities** - Used in shopping lists, affects user experience
2. **Meal Types** - Core meal planning functionality
3. **Days of Week** - Essential for meal planning rules

### Medium Priority (Week 2-3)

1. **Recipe Difficulties** - Recipe search and filtering
2. **Cuisine Types** - Recipe categorization and search
3. **Activity Levels** - Person health attributes

### Lower Priority (Week 3-4)

1. **Dietary Restrictions** - Person preferences
2. **Allergy Types** - Medical restrictions
3. **Medical Conditions** - Health restrictions
4. **Personal Preferences** - User experience enhancements

## Migration Strategy for String Values

### Phase 1: Database Preparation

- Create enum value tables
- Seed with current string values mapped to numeric IDs
- Maintain backward compatibility during transition

### Phase 2: API Updates

- Update all endpoints to accept and return numeric IDs
- Add string value fallbacks for legacy support
- Implement validation for numeric ID ranges

### Phase 3: Frontend Conversion

- Replace string literals with numeric constants
- Update form controls to use numeric values
- Implement proper error handling for invalid IDs

### Phase 4: Cleanup

- Remove string value fallbacks
- Update documentation
- Remove hardcoded string arrays

---

**Last Updated**: $(date)
**Status**: Planning Phase
**Next Milestone**: Backend Foundation (Week 1)
