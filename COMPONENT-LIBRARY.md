# Component Library Documentation

## Overview

This document provides comprehensive documentation for the Dynamic Data Components library, which replaces hardcoded UI values with dynamic backend data. All components are designed to work seamlessly with the Reference system architecture.

## Core Components

### 1. ReferenceSelectorComponent

A reusable, generic component for selecting any type of reference data from the backend.

#### Features

- **Generic Design**: Works with any reference group via discriminator ID
- **Single/Multi Select**: Supports both single and multiple selection modes
- **Form Integration**: Seamlessly integrates with Angular Reactive Forms
- **Dynamic Descriptions**: Shows reference descriptions when enabled
- **Caching**: Leverages the ReferenceDataService caching system

#### Usage

```typescript
import { ReferenceSelectorComponent } from "./common/components/reference-selector/reference-selector.component";
import { REFERENCE_IDS } from "./common/constants/reference-ids";

@Component({
  template: `
    <app-reference-selector
      [discriminatorId]="REFERENCE_IDS.SHOPPING_PRIORITY_TYPE"
      [control]="priorityControl"
      label="Select Priority"
      placeholder="Choose a priority level"
      [showDescription]="true"
      (selectionChange)="onPriorityChange($event)"
    >
    </app-reference-selector>
  `,
})
export class MyComponent {
  priorityControl = new FormControl();

  onPriorityChange(priority: ReferenceItem): void {
    console.log("Selected priority:", priority);
  }
}
```

#### API Reference

| Input                  | Type          | Required | Description                                  |
| ---------------------- | ------------- | -------- | -------------------------------------------- |
| `discriminatorId`      | `number`      | ✅       | Reference group discriminator ID             |
| `control`              | `FormControl` | ✅       | Angular form control                         |
| `label`                | `string`      | ❌       | Label text above the selector                |
| `placeholder`          | `string`      | ❌       | Placeholder text in the select               |
| `requiredErrorMessage` | `string`      | ❌       | Custom error message for required validation |
| `isMultiSelect`        | `boolean`     | ❌       | Enable multiple selection (default: false)   |
| `showDescription`      | `boolean`     | ❌       | Show reference descriptions (default: false) |
| `controlId`            | `string`      | ❌       | Unique ID for the form control               |

| Output            | Type                                             | Description                    |
| ----------------- | ------------------------------------------------ | ------------------------------ |
| `selectionChange` | `EventEmitter<ReferenceItem \| ReferenceItem[]>` | Emitted when selection changes |

#### Styling

The component uses Material Design and can be customized via CSS:

```scss
.reference-selector {
  &__label {
    font-weight: 500;
    margin-bottom: 8px;
  }

  &__field {
    width: 100%;
  }

  &__description {
    margin-top: 4px;
    font-size: 12px;
    color: rgba(0, 0, 0, 0.6);
  }
}
```

### 2. ShoppingListComponent

A comprehensive shopping list component with dynamic filtering and categorization.

#### Features

- **Dynamic Data**: All priorities and categories loaded from backend
- **Advanced Filtering**: Filter by priority, category, or both
- **Real-time Updates**: Form changes trigger immediate filtering
- **Visual Indicators**: Color-coded priorities and categories
- **Summary Statistics**: Dynamic counts and statistics

#### Usage

```typescript
import { ShoppingListComponent } from "./shopping/components/shopping-list/shopping-list.component";

@Component({
  template: ` <app-shopping-list></app-shopping-list> `,
})
export class ShoppingPageComponent {}
```

#### API Reference

The component is self-contained and automatically:

- Loads shopping priorities and categories from the backend
- Sets up filtering forms
- Manages shopping item display and filtering
- Provides action buttons for CRUD operations

#### Styling Classes

```scss
.shopping-list {
  &__header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 24px;
  }

  &__filters {
    display: flex;
    gap: 16px;
    margin-bottom: 24px;
  }

  &__items {
    .shopping-item {
      &.priority-high {
        border-left: 4px solid #f44336;
      }
      &.priority-medium {
        border-left: 4px solid #ff9800;
      }
      &.priority-low {
        border-left: 4px solid #4caf50;
      }
    }
  }
}
```

### 3. ShoppingItemFormComponent

A form component for adding/editing shopping items using dynamic reference data.

#### Features

- **Dynamic Categories**: Shopping categories loaded from backend
- **Dynamic Priorities**: Shopping priorities loaded from backend
- **Form Validation**: Comprehensive validation with error messages
- **Real-time Feedback**: Shows selected options with descriptions

#### Usage

```typescript
import { ShoppingItemFormComponent } from "./shopping/components/shopping-item-form/shopping-item-form.component";

@Component({
  template: ` <app-shopping-item-form></app-shopping-item-form> `,
})
export class ShoppingPageComponent {}
```

#### Form Fields

| Field        | Type     | Required | Description                |
| ------------ | -------- | -------- | -------------------------- |
| `name`       | `string` | ✅       | Item name                  |
| `categoryId` | `number` | ✅       | Category from dynamic data |
| `priorityId` | `number` | ✅       | Priority from dynamic data |
| `quantity`   | `number` | ✅       | Item quantity (min: 1)     |

#### Events

| Event              | Type            | Description               |
| ------------------ | --------------- | ------------------------- |
| `onSubmit`         | `void`          | Form submission handler   |
| `onCancel`         | `void`          | Form cancellation handler |
| `onCategoryChange` | `ReferenceItem` | Category selection change |
| `onPriorityChange` | `ReferenceItem` | Priority selection change |

### 4. RecipeFormComponent

A comprehensive recipe creation/editing form with dynamic classifications.

#### Features

- **Dynamic Classifications**: Difficulty, cuisine, meal type from backend
- **Dietary Options**: Multi-select dietary restrictions and allergens
- **Form Validation**: Comprehensive validation for all required fields
- **Real-time Preview**: Shows selected options as they're chosen

#### Usage

```typescript
import { RecipeFormComponent } from "./recipe/components/recipe-form/recipe-form.component";

@Component({
  template: ` <app-recipe-form></app-recipe-form> `,
})
export class RecipePageComponent {}
```

#### Form Fields

| Field              | Type       | Required | Description                   |
| ------------------ | ---------- | -------- | ----------------------------- |
| `name`             | `string`   | ✅       | Recipe name                   |
| `description`      | `string`   | ✅       | Recipe description            |
| `prepTime`         | `number`   | ✅       | Preparation time in minutes   |
| `cookTime`         | `number`   | ❌       | Cooking time in minutes       |
| `difficultyId`     | `number`   | ✅       | Difficulty level from backend |
| `cuisineTypeId`    | `number`   | ✅       | Cuisine type from backend     |
| `mealTypeId`       | `number`   | ✅       | Meal type from backend        |
| `servings`         | `number`   | ✅       | Number of servings            |
| `dietaryOptionIds` | `number[]` | ❌       | Array of dietary option IDs   |
| `allergenIds`      | `number[]` | ❌       | Array of allergen IDs         |
| `instructions`     | `string`   | ✅       | Cooking instructions          |

#### Dynamic Data Sources

- **Difficulties**: `REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE` (6003)
- **Cuisines**: `REFERENCE_IDS.CUISINE_TYPE` (3001)
- **Meal Types**: `REFERENCE_IDS.MEAL_TYPE` (1)
- **Dietary Options**: `REFERENCE_IDS.RECIPE_DIETARY_OPTION_TYPE` (6016)
- **Allergens**: `REFERENCE_IDS.ALLERGY_TYPE` (6007)

### 5. MealPlanFormComponent

A meal planning component with dynamic scheduling and meal assignment.

#### Features

- **Dynamic Scheduling**: Days of week loaded from backend
- **Dynamic Meal Types**: Meal types loaded from backend
- **Flexible Assignments**: Add/remove meal assignments dynamically
- **Shopping Integration**: Option to generate shopping lists

#### Usage

```typescript
import { MealPlanFormComponent } from "./meal-plan/components/meal-plan-form/meal-plan-form.component";

@Component({
  template: ` <app-meal-plan-form></app-meal-plan-form> `,
})
export class MealPlanPageComponent {}
```

#### Form Structure

```typescript
interface MealPlanForm {
  name: string;
  description: string;
  startDate: Date;
  endDate: Date;
  day0: boolean; // Monday
  day1: boolean; // Tuesday
  // ... other days
  mealAssignments: FormArray<MealAssignment>;
  generateShoppingList: boolean;
  shoppingListName: string;
}

interface MealAssignment {
  dayOfWeekId: number;
  mealTypeId: number;
  recipeName: string;
  notes: string;
}
```

#### Dynamic Data Sources

- **Days of Week**: `REFERENCE_IDS.DAY_OF_WEEK_TYPE` (6015)
- **Meal Types**: `REFERENCE_IDS.MEAL_TYPE` (1)

### 6. DynamicDataDemoComponent

A showcase component demonstrating all dynamic data components in action.

#### Features

- **Component Showcase**: Displays all new components
- **Implementation Details**: Shows backend and frontend architecture
- **Benefits Overview**: Highlights advantages of dynamic data
- **Interactive Examples**: Working examples of each component

#### Usage

```typescript
import { DynamicDataDemoComponent } from "./common/components/dynamic-data-demo/dynamic-data-demo.component";

@Component({
  template: ` <app-dynamic-data-demo></app-dynamic-data-demo> `,
})
export class DemoPageComponent {}
```

## Service Layer

### ReferenceDataService

The core service for fetching and caching reference data.

#### Methods

```typescript
class ReferenceDataService {
  // Get references for a specific group
  getReferencesByGroup(discriminatorId: number): Observable<ReferenceItem[]>;

  // Get multiple reference groups in one call
  getReferencesBulk(discriminatorIds: number[]): Observable<ReferenceGroup>;

  // Clear cache for specific group or all groups
  clearCache(discriminatorId?: number): void;

  // Get specific reference by ID
  getReferenceById(
    groupId: number,
    referenceId: number
  ): Observable<ReferenceItem | null>;

  // Search references by name pattern
  getReferencesByNamePattern(
    groupId: number,
    pattern: string
  ): Observable<ReferenceItem[]>;
}
```

#### Caching Strategy

- **Individual Cache**: Each reference group cached separately
- **Bulk Cache**: Combined results cached with sorted key
- **Automatic Updates**: Individual caches updated when bulk loading
- **Manual Clearing**: Cache can be cleared programmatically

### Specialized Services

#### ShoppingReferenceService

```typescript
class ShoppingReferenceService {
  getShoppingPriorities(): Observable<ReferenceItem[]>;
  getShoppingCategories(): Observable<ReferenceItem[]>;
  getShoppingReferences(): Observable<{
    priorities: ReferenceItem[];
    categories: ReferenceItem[];
  }>;
  getShoppingReferencesBulk(): Observable<{
    priorities: ReferenceItem[];
    categories: ReferenceItem[];
  }>;
}
```

#### RecipeReferenceService

```typescript
class RecipeReferenceService {
  getRecipeDifficulties(): Observable<ReferenceItem[]>;
  getCuisineTypes(): Observable<ReferenceItem[]>;
  getMealTypes(): Observable<ReferenceItem[]>;
  getRecipeReferencesBulk(): Observable<{
    difficulties: ReferenceItem[];
    cuisines: ReferenceItem[];
    mealTypes: ReferenceItem[];
    dietaryOptions: ReferenceItem[];
    allergens: ReferenceItem[];
  }>;
}
```

#### MealPlanReferenceService

```typescript
class MealPlanReferenceService {
  getMealTypes(): Observable<ReferenceItem[]>;
  getDaysOfWeek(): Observable<ReferenceItem[]>;
  getMealPlanReferencesBulk(): Observable<{
    mealTypes: ReferenceItem[];
    daysOfWeek: ReferenceItem[];
  }>;
}
```

## Constants and Types

### REFERENCE_IDS

```typescript
export const REFERENCE_IDS = {
  // Core System (1-999)
  MEAL_TYPE: 1,
  RECIPE_TYPE: 3,

  // Core Application (1000-1999)
  QUESTION_CATEGORY: 1000,

  // Dietary & Health (2000-2999)
  RESTRICTION_TYPE: 2000,

  // Nutritional (3000-3999)
  CUISINE_TYPE: 3001,

  // UI Data Conversion (6000-6999)
  SHOPPING_PRIORITY_TYPE: 6000,
  SHOPPING_CATEGORY_TYPE: 6001,
  RECIPE_DIFFICULTY_TYPE: 6003,
  DAY_OF_WEEK_TYPE: 6015,
  RECIPE_DIETARY_OPTION_TYPE: 6016,
} as const;
```

### ReferenceItem Interface

```typescript
export interface ReferenceItem {
  referenceId: number;
  referenceName: string;
  referenceDescription: string;
  groupId: number;
  groupName: string;
  groupDescription: string;
}
```

## Integration Patterns

### 1. Basic Integration

```typescript
// 1. Import component and constants
import { ReferenceSelectorComponent } from './common/components/reference-selector/reference-selector.component';
import { REFERENCE_IDS } from './common/constants/reference-ids';

// 2. Add to component declarations
@Component({
  declarations: [ReferenceSelectorComponent]
})

// 3. Use in template
<app-reference-selector
  [discriminatorId]="REFERENCE_IDS.SHOPPING_PRIORITY_TYPE"
  [control]="priorityControl">
</app-reference-selector>
```

### 2. Form Integration

```typescript
// 1. Create form control
priorityControl = new FormControl(null, Validators.required);

// 2. Handle selection changes
onPriorityChange(priority: ReferenceItem): void {
  console.log('Priority selected:', priority.referenceName);
  // Update form or trigger other actions
}

// 3. Use in template
<app-reference-selector
  [discriminatorId]="REFERENCE_IDS.SHOPPING_PRIORITY_TYPE"
  [control]="priorityControl"
  (selectionChange)="onPriorityChange($event)">
</app-reference-selector>
```

### 3. Service Integration

```typescript
// 1. Inject service
constructor(private shoppingReferenceService: ShoppingReferenceService) {}

// 2. Load data
ngOnInit(): void {
  this.shoppingReferenceService.getShoppingReferencesBulk()
    .subscribe(({ priorities, categories }) => {
      this.priorities = priorities;
      this.categories = categories;
    });
}
```

## Best Practices

### 1. Performance Optimization

- **Use Bulk Loading**: Always use bulk loading for multiple reference groups
- **Leverage Caching**: The service automatically caches results
- **Clear Cache When Needed**: Clear cache when data might be stale

### 2. Error Handling

```typescript
this.referenceDataService
  .getReferencesByGroup(discriminatorId)
  .pipe(
    catchError((error) => {
      console.error("Error loading references:", error);
      return of([]); // Return empty array on error
    })
  )
  .subscribe((references) => {
    this.references = references;
  });
```

### 3. Form Validation

```typescript
// Always validate required fields
difficultyControl = new FormControl(null, Validators.required);

// Custom validators for specific business rules
difficultyControl = new FormControl(null, [
  Validators.required,
  this.validateDifficulty.bind(this),
]);
```

### 4. Type Safety

```typescript
// Use typed constants
const priorityId = REFERENCE_IDS.SHOPPING_PRIORITY_TYPE;

// Use proper interfaces
onSelectionChange(priority: ReferenceItem): void {
  // TypeScript will ensure priority has all required properties
}
```

## Migration Guide

### From Hardcoded Values

**Before (Hardcoded):**

```typescript
const priorities = [
  { id: 1, name: "Low" },
  { id: 2, name: "Medium" },
  { id: 3, name: "High" },
];
```

**After (Dynamic):**

```typescript
// In component
this.shoppingReferenceService.getShoppingPriorities()
  .subscribe(priorities => this.priorities = priorities);

// In template
<app-reference-selector
  [discriminatorId]="REFERENCE_IDS.SHOPPING_PRIORITY_TYPE"
  [control]="priorityControl">
</app-reference-selector>
```

### From Magic Numbers

**Before (Magic Numbers):**

```typescript
if (priority === 1) return "Low";
if (priority === 2) return "Medium";
if (priority === 3) return "High";
```

**After (Dynamic):**

```typescript
getPriorityName(priorityId: number): string {
  const priority = this.priorities.find(p => p.referenceId === priorityId);
  return priority?.referenceName || 'Unknown';
}
```

## Troubleshooting

### Common Issues

1. **Component Not Loading Data**

   - Check discriminator ID is correct
   - Verify service is properly injected
   - Check browser console for errors

2. **Form Validation Issues**

   - Ensure FormControl is properly initialized
   - Check required validators are set
   - Verify form control binding

3. **Styling Issues**
   - Check Material Design imports
   - Verify CSS classes are applied
   - Check for conflicting styles

### Debug Tips

```typescript
// Add logging to see what's happening
ngOnInit(): void {
  console.log('Loading references for group:', this.discriminatorId);

  this.referenceDataService.getReferencesByGroup(this.discriminatorId)
    .subscribe({
      next: (references) => {
        console.log('References loaded:', references);
        this.references = references;
      },
      error: (error) => {
        console.error('Error loading references:', error);
      }
    });
}
```

## Conclusion

The Dynamic Data Components library provides a robust, scalable solution for replacing hardcoded UI values with dynamic backend data. By following the patterns and best practices outlined in this documentation, developers can easily integrate these components into their applications and maintain consistency across the UI.

For additional support or questions, refer to the testing documentation and implementation plan for comprehensive coverage of the system architecture and design decisions.
