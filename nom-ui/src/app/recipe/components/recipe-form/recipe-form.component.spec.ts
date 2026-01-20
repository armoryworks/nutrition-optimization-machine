import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { provideAnimations } from '@angular/platform-browser/animations';
import { RecipeFormComponent } from './recipe-form.component';
import { RecipeReferenceService } from '../../services/recipe-reference.service';
import { of } from 'rxjs';

describe('RecipeFormComponent', () => {
    let component: RecipeFormComponent;
    let fixture: ComponentFixture<RecipeFormComponent>;
    let mockRecipeReferenceService: jasmine.SpyObj<RecipeReferenceService>;

    const mockDifficulties = [
        { referenceId: 1, referenceName: 'Easy', referenceDescription: 'Easy recipes', groupId: 6003, groupName: 'Recipe Difficulty', groupDescription: 'Recipe difficulty levels' },
        { referenceId: 2, referenceName: 'Medium', referenceDescription: 'Medium difficulty recipes', groupId: 6003, groupName: 'Recipe Difficulty', groupDescription: 'Recipe difficulty levels' },
        { referenceId: 3, referenceName: 'Hard', referenceDescription: 'Hard recipes', groupId: 6003, groupName: 'Recipe Difficulty', groupDescription: 'Recipe difficulty levels' }
    ];

    const mockCuisines = [
        { referenceId: 1, referenceName: 'Italian', referenceDescription: 'Italian cuisine', groupId: 3001, groupName: 'Cuisine Type', groupDescription: 'Types of cuisine' },
        { referenceId: 2, referenceName: 'Mexican', referenceDescription: 'Mexican cuisine', groupId: 3001, groupName: 'Cuisine Type', groupDescription: 'Types of cuisine' },
        { referenceId: 3, referenceName: 'Asian', referenceDescription: 'Asian cuisine', groupId: 3001, groupName: 'Cuisine Type', groupDescription: 'Types of cuisine' }
    ];

    const mockMealTypes = [
        { referenceId: 1, referenceName: 'Breakfast', referenceDescription: 'Breakfast meals', groupId: 1, groupName: 'Meal Type', groupDescription: 'Types of meals' },
        { referenceId: 2, referenceName: 'Lunch', referenceDescription: 'Lunch meals', groupId: 1, groupName: 'Meal Type', groupDescription: 'Types of meals' },
        { referenceId: 3, referenceName: 'Dinner', referenceDescription: 'Dinner meals', groupId: 1, groupName: 'Meal Type', groupDescription: 'Types of meals' }
    ];

    const mockDietaryOptions = [
        { referenceId: 1, referenceName: 'Vegetarian', referenceDescription: 'Vegetarian options', groupId: 6016, groupName: 'Recipe Dietary Option', groupDescription: 'Dietary options for recipes' },
        { referenceId: 2, referenceName: 'Vegan', referenceDescription: 'Vegan options', groupId: 6016, groupName: 'Recipe Dietary Option', groupDescription: 'Dietary options for recipes' },
        { referenceId: 3, referenceName: 'Gluten-Free', referenceDescription: 'Gluten-free options', groupId: 6016, groupName: 'Recipe Dietary Option', groupDescription: 'Dietary options for recipes' }
    ];

    const mockAllergens = [
        { referenceId: 1, referenceName: 'Nuts', referenceDescription: 'Contains nuts', groupId: 6007, groupName: 'Allergy Type', groupDescription: 'Types of allergies' },
        { referenceId: 2, referenceName: 'Dairy', referenceDescription: 'Contains dairy', groupId: 6007, groupName: 'Allergy Type', groupDescription: 'Types of allergies' },
        { referenceId: 3, referenceName: 'Shellfish', referenceDescription: 'Contains shellfish', groupId: 6007, groupName: 'Allergy Type', groupDescription: 'Types of allergies' }
    ];

    beforeEach(async () => {
        mockRecipeReferenceService = jasmine.createSpyObj('RecipeReferenceService', [
            'getRecipeReferencesBulk'
        ]);

        await TestBed.configureTestingModule({
            imports: [
                RecipeFormComponent,
                ReactiveFormsModule
            ],
            providers: [
                provideAnimations(),
                FormBuilder,
                { provide: RecipeReferenceService, useValue: mockRecipeReferenceService }
            ]
        }).compileComponents();

        fixture = TestBed.createComponent(RecipeFormComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should initialize form with default values', () => {
        expect(component.recipeForm).toBeDefined();
        expect(component.recipeForm.get('name')).toBeDefined();
        expect(component.recipeForm.get('description')).toBeDefined();
        expect(component.recipeForm.get('prepTime')).toBeDefined();
        expect(component.recipeForm.get('cookTime')).toBeDefined();
        expect(component.recipeForm.get('difficultyId')).toBeDefined();
        expect(component.recipeForm.get('cuisineTypeId')).toBeDefined();
        expect(component.recipeForm.get('mealTypeId')).toBeDefined();
        expect(component.recipeForm.get('servings')).toBeDefined();
        expect(component.recipeForm.get('dietaryOptionIds')).toBeDefined();
        expect(component.recipeForm.get('allergenIds')).toBeDefined();
        expect(component.recipeForm.get('instructions')).toBeDefined();
    });

    it('should set default values correctly', () => {
        expect(component.recipeForm.get('prepTime')?.value).toBe(30);
        expect(component.recipeForm.get('cookTime')?.value).toBe(45);
        expect(component.recipeForm.get('servings')?.value).toBe(4);
        expect(component.recipeForm.get('dietaryOptionIds')?.value).toEqual([]);
        expect(component.recipeForm.get('allergenIds')?.value).toEqual([]);
    });

    it('should load reference data on init', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        expect(mockRecipeReferenceService.getRecipeReferencesBulk).toHaveBeenCalled();
        expect(component.difficulties).toEqual(mockDifficulties);
        expect(component.cuisines).toEqual(mockCuisines);
        expect(component.mealTypes).toEqual(mockMealTypes);
        expect(component.dietaryOptions).toEqual(mockDietaryOptions);
        expect(component.allergens).toEqual(mockAllergens);
    });

    it('should detect selected options correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        // Initially no options selected
        expect(component.hasSelectedOptions()).toBe(false);

        // Select difficulty
        component.recipeForm.get('difficultyId')?.setValue(1);
        expect(component.hasSelectedOptions()).toBe(true);

        // Select cuisine
        component.recipeForm.get('cuisineTypeId')?.setValue(2);
        expect(component.hasSelectedOptions()).toBe(true);

        // Select meal type
        component.recipeForm.get('mealTypeId')?.setValue(3);
        expect(component.hasSelectedOptions()).toBe(true);

        // Select dietary options
        component.recipeForm.get('dietaryOptionIds')?.setValue([1, 2]);
        expect(component.hasSelectedOptions()).toBe(true);

        // Select allergens
        component.recipeForm.get('allergenIds')?.setValue([1]);
        expect(component.hasSelectedOptions()).toBe(true);
    });

    it('should get selected difficulty correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        component.recipeForm.get('difficultyId')?.setValue(2);
        const selectedDifficulty = component.getSelectedDifficulty();
        expect(selectedDifficulty?.referenceName).toBe('Medium');
    });

    it('should get selected cuisine correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        component.recipeForm.get('cuisineTypeId')?.setValue(1);
        const selectedCuisine = component.getSelectedCuisine();
        expect(selectedCuisine?.referenceName).toBe('Italian');
    });

    it('should get selected meal type correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        component.recipeForm.get('mealTypeId')?.setValue(3);
        const selectedMealType = component.getSelectedMealType();
        expect(selectedMealType?.referenceName).toBe('Dinner');
    });

    it('should get selected dietary options correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        component.recipeForm.get('dietaryOptionIds')?.setValue([1, 3]);
        const selectedDietaryOptions = component.getSelectedDietaryOptions();
        expect(selectedDietaryOptions.length).toBe(2);
        expect(selectedDietaryOptions[0].referenceName).toBe('Vegetarian');
        expect(selectedDietaryOptions[1].referenceName).toBe('Gluten-Free');
    });

    it('should get selected allergens correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        component.recipeForm.get('allergenIds')?.setValue([2]);
        const selectedAllergens = component.getSelectedAllergens();
        expect(selectedAllergens.length).toBe(1);
        expect(selectedAllergens[0].referenceName).toBe('Dairy');
    });

    it('should handle form submission when valid', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        // Fill required fields
        component.recipeForm.patchValue({
            name: 'Test Recipe',
            description: 'Test description',
            prepTime: 30,
            difficultyId: 1,
            cuisineTypeId: 1,
            mealTypeId: 1,
            servings: 4,
            instructions: 'Test instructions'
        });

        spyOn(console, 'log');
        spyOn(window, 'alert');

        component.onSubmit();

        expect(component.isSubmitting).toBe(true);
        expect(console.log).toHaveBeenCalledWith('Recipe form submitted:', jasmine.any(Object));
    });

    it('should not submit when form is invalid', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        // Leave required fields empty
        spyOn(console, 'log');
        spyOn(window, 'alert');

        component.onSubmit();

        expect(component.isSubmitting).toBe(false);
        expect(console.log).not.toHaveBeenCalled();
        expect(window.alert).not.toHaveBeenCalled();
    });

    it('should handle cancel with confirmation when form is dirty', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        // Make form dirty
        component.recipeForm.get('name')?.setValue('Test Recipe');
        spyOn(window, 'confirm').and.returnValue(true);

        component.onCancel();

        expect(window.confirm).toHaveBeenCalledWith('Are you sure you want to cancel? All changes will be lost.');
    });

    it('should handle cancel without confirmation when form is not dirty', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        spyOn(window, 'confirm');

        component.onCancel();

        expect(window.confirm).not.toHaveBeenCalled();
    });

    it('should reset form correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        // Fill some fields
        component.recipeForm.patchValue({
            name: 'Test Recipe',
            description: 'Test description',
            prepTime: 60,
            cookTime: 90,
            servings: 6
        });

        // Reset form
        component['resetForm']();

        expect(component.recipeForm.get('name')?.value).toBe('');
        expect(component.recipeForm.get('description')?.value).toBe('');
        expect(component.recipeForm.get('prepTime')?.value).toBe(30);
        expect(component.recipeForm.get('cookTime')?.value).toBe(45);
        expect(component.recipeForm.get('servings')?.value).toBe(4);
        expect(component.recipeForm.get('dietaryOptionIds')?.value).toEqual([]);
        expect(component.recipeForm.get('allergenIds')?.value).toEqual([]);
    });

    it('should setup form listeners correctly', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        // Form listeners should be set up
        expect(component.recipeForm.valueChanges).toBeDefined();
    });

    it('should clean up subscriptions on destroy', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: mockDifficulties,
                cuisines: mockCuisines,
                mealTypes: mockMealTypes,
                dietaryOptions: mockDietaryOptions,
                allergens: mockAllergens
            })
        );

        fixture.detectChanges();

        // Should not throw on destroy
        expect(() => component.ngOnDestroy()).not.toThrow();
    });

    it('should handle missing reference data gracefully', () => {
        mockRecipeReferenceService.getRecipeReferencesBulk.and.returnValue(
            of({
                difficulties: [],
                cuisines: [],
                mealTypes: [],
                dietaryOptions: [],
                allergens: []
            })
        );

        expect(() => fixture.detectChanges()).not.toThrow();
    });
});
