# Migration Guide: From Hardcoded to Dynamic Data

## Overview

This guide provides step-by-step instructions for migrating existing components from hardcoded values to the new dynamic data system. The migration process is designed to be incremental and non-breaking, allowing teams to migrate components one at a time.

## Pre-Migration Checklist

Before starting migration, ensure you have:

- **Backend Ready**: New reference data is seeded and API endpoints are working
- **Frontend Services**: ReferenceDataService and specialized services are available
- **Component Library**: New dynamic components are built and tested
- **Constants**: REFERENCE_IDS constants are defined and accessible

## Migration Process Overview

### Phase 1: Analysis

1. Identify hardcoded values in the component
2. Map them to appropriate reference groups
3. Plan the migration approach

### Phase 2: Implementation

1. Replace hardcoded arrays with service calls
2. Update templates to use new components
3. Modify component logic to work with dynamic data

### Phase 3: Testing

1. Verify data loads correctly
2. Test form validation and submission
3. Ensure error handling works

### Phase 4: Cleanup

1. Remove unused hardcoded constants
2. Update component documentation
3. Verify no regressions

## Common Migration Patterns

### Pattern 1: Simple Dropdown Replacement

**Using AMW with Dynamic Data:**

```typescript
// Component
export class ShoppingComponent implements OnInit {
  priorities: ReferenceItem[] = [];

  constructor(private shoppingReferenceService: ShoppingReferenceService) {}

  ngOnInit(): void {
    this.shoppingReferenceService.getShoppingPriorities()
      .subscribe(priorities => this.priorities = priorities);
  }

  getPriorityName(id: number): string {
    const priority = this.priorities.find(p => p.referenceId === id);
    return priority ? priority.referenceName : 'Unknown';
  }
}

// Template (AMW + Reference Selector)
<app-reference-selector
  [discriminatorId]="REFERENCE_IDS.SHOPPING_PRIORITY_TYPE"
  [control]="priorityControl"
  label="Priority"
  placeholder="Select priority">
</app-reference-selector>
```

### Pattern 2: Form Control Integration

**Using AMW with Dynamic Data:**

```typescript
// Component
export class RecipeFormComponent implements OnInit {
  difficulties: ReferenceItem[] = [];

  recipeForm = this.fb.group({
    name: ['', Validators.required],
    difficultyId: [null, Validators.required]
  });

  constructor(
    private fb: FormBuilder,
    private recipeReferenceService: RecipeReferenceService
  ) {}

  ngOnInit(): void {
    this.recipeReferenceService.getRecipeDifficulties()
      .subscribe(difficulties => this.difficulties = difficulties);
  }
}

// Template (AMW + Reference Selector)
<app-reference-selector
  [discriminatorId]="REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE"
  [control]="recipeForm.get('difficultyId')!"
  label="Difficulty"
  placeholder="Select difficulty level"
  [showDescription]="true">
</app-reference-selector>
```

## Migration Checklist

### Pre-Migration

- [ ] Identify all hardcoded values in the component
- [ ] Map values to appropriate reference groups
- [ ] Verify reference data exists in the backend
- [ ] Plan migration approach (incremental vs. complete)

### Implementation

- [ ] Import required services and constants
- [ ] Replace hardcoded arrays with service calls
- [ ] Update component initialization (ngOnInit)
- [ ] Replace template hardcoded options with ReferenceSelectorComponent
- [ ] Update form controls and validation
- [ ] Modify component logic to work with ReferenceItem interface

### Testing

- [ ] Verify data loads correctly from backend
- [ ] Test form validation and submission
- [ ] Ensure error handling works
- [ ] Test edge cases (empty data, network errors)
- [ ] Verify no regressions in existing functionality

### Cleanup

- [ ] Remove unused hardcoded constants
- [ ] Remove unused imports
- [ ] Update component documentation
- [ ] Verify TypeScript compilation
- [ ] Run existing tests to ensure no regressions

## Common Pitfalls and Solutions

### Pitfall 1: Async Data Loading

**Problem**: Component tries to use data before it's loaded
**Solution**: Use proper initialization and loading states

```typescript
export class SafeComponent implements OnInit {
  priorities: ReferenceItem[] = [];
  isLoading = true;

  ngOnInit(): void {
    this.referenceDataService.getReferencesByGroup(discriminatorId).subscribe({
      next: (priorities) => {
        this.priorities = priorities;
        this.isLoading = false;
      },
      error: (error) => {
        console.error("Error loading priorities:", error);
        this.isLoading = false;
      },
    });
  }
}
```

### Pitfall 2: Form Control Binding

**Problem**: Form control not properly bound to ReferenceSelectorComponent
**Solution**: Ensure proper form control initialization

```typescript
export class FormComponent {
  priorityControl = new FormControl(null, Validators.required);

  ngOnInit(): void {
    // Set initial value if needed
    this.priorityControl.setValue(1);
  }
}
```

## Performance Considerations

### Bulk Loading

Always use bulk loading when multiple reference groups are needed:

```typescript
// Good: Bulk loading
this.recipeReferenceService
  .getRecipeReferencesBulk()
  .subscribe(({ difficulties, cuisines, mealTypes }) => {
    this.difficulties = difficulties;
    this.cuisines = cuisines;
    this.mealTypes = mealTypes;
  });

// Avoid: Multiple individual calls
this.recipeReferenceService
  .getRecipeDifficulties()
  .subscribe((d) => (this.difficulties = d));
this.recipeReferenceService
  .getCuisineTypes()
  .subscribe((c) => (this.cuisines = c));
this.recipeReferenceService
  .getMealTypes()
  .subscribe((m) => (this.mealTypes = m));
```

### Caching

The ReferenceDataService automatically caches results. Clear cache when needed:

```typescript
// Clear specific group cache
this.referenceDataService.clearCache(REFERENCE_IDS.SHOPPING_PRIORITY_TYPE);

// Clear all caches
this.referenceDataService.clearCache();
```

## Conclusion

Migrating from hardcoded values to dynamic data is a significant improvement that provides:

- **Maintainability**: Centralized data management
- **Consistency**: Uniform data across all components
- **Flexibility**: Easy to add new options without code changes
- **Performance**: Intelligent caching and bulk loading
- **Type Safety**: Strong typing with TypeScript interfaces

By following this migration guide and using the provided patterns, teams can successfully migrate their components while maintaining functionality and improving the overall system architecture.

For additional support, refer to the Component Library Documentation and Testing Summary for comprehensive coverage of the dynamic data system.
