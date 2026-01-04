import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { RecipeReferenceService } from '../../services/recipe-reference.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

@Component({
  selector: 'app-recipe-form',
  template: `
    <div class="recipe-form">
      <div class="recipe-form__header">
        <h2>{{ isEditMode ? 'Edit Recipe' : 'Create New Recipe' }}</h2>
        <p class="subtitle">Fill in the details below to {{ isEditMode ? 'update' : 'create' }} your recipe</p>
      </div>
    
      <form [formGroup]="recipeForm" (ngSubmit)="onSubmit()" class="recipe-form__content">
        <!-- Basic Information -->
        <div class="form-section">
          <h3>Basic Information</h3>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Recipe Name</mat-label>
              <input matInput formControlName="name" placeholder="Enter recipe name">
              @if (recipeForm.get('name')?.hasError('required')) {
                <mat-error>
                  Recipe name is required
                </mat-error>
              }
            </mat-form-field>
          </div>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Description</mat-label>
              <textarea matInput formControlName="description" rows="3" placeholder="Describe your recipe"></textarea>
              @if (recipeForm.get('description')?.hasError('required')) {
                <mat-error>
                  Description is required
                </mat-error>
              }
            </mat-form-field>
          </div>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Preparation Time (minutes)</mat-label>
              <input matInput type="number" formControlName="prepTime" min="1">
              @if (recipeForm.get('prepTime')?.hasError('required')) {
                <mat-error>
                  Preparation time is required
                </mat-error>
              }
              @if (recipeForm.get('prepTime')?.hasError('min')) {
                <mat-error>
                  Preparation time must be at least 1 minute
                </mat-error>
              }
            </mat-form-field>
    
            <mat-form-field class="form-field">
              <mat-label>Cook Time (minutes)</mat-label>
              <input matInput type="number" formControlName="cookTime" min="0">
              @if (recipeForm.get('cookTime')?.hasError('min')) {
                <mat-error>
                  Cook time cannot be negative
                </mat-error>
              }
            </mat-form-field>
          </div>
        </div>
    
        <!-- Recipe Classification -->
        <div class="form-section">
          <h3>Recipe Classification</h3>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Difficulty Level</mat-label>
              <mat-select formControlName="difficultyId" [showDescription]="true">
                @for (difficulty of difficulties; track difficulty) {
                  <mat-option [value]="difficulty.referenceId">
                    {{ difficulty.referenceName }}
                  </mat-option>
                }
              </mat-select>
              @if (recipeForm.get('difficultyId')?.hasError('required')) {
                <mat-error>
                  Difficulty level is required
                </mat-error>
              }
            </mat-form-field>
    
            <mat-form-field class="form-field">
              <mat-label>Cuisine Type</mat-label>
              <mat-select formControlName="cuisineTypeId" [showDescription]="true">
                @for (cuisine in cuisines; track cuisine in cuisines) {
                  <mat-option [value]="cuisine.referenceId">
                    {{ cuisine.referenceName }}
                  </mat-option>
                }
              </mat-select>
              @if (recipeForm.get('cuisineTypeId')?.hasError('required')) {
                <mat-error>
                  Cuisine type is required
                </mat-error>
              }
            </mat-form-field>
          </div>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Meal Type</mat-label>
              <mat-select formControlName="mealTypeId" [showDescription]="true">
                @for (mealType in mealTypes; track mealType in mealTypes) {
                  <mat-option [value]="mealType.referenceId">
                    {{ mealType.referenceName }}
                  </mat-option>
                }
              </mat-select>
              @if (recipeForm.get('mealTypeId')?.hasError('required')) {
                <mat-error>
                  Meal type is required
                </mat-error>
              }
            </mat-form-field>
    
            <mat-form-field class="form-field">
              <mat-label>Servings</mat-label>
              <input matInput type="number" formControlName="servings" min="1">
              @if (recipeForm.get('servings')?.hasError('required')) {
                <mat-error>
                  Number of servings is required
                </mat-error>
              }
              @if (recipeForm.get('servings')?.hasError('min')) {
                <mat-error>
                  Servings must be at least 1
                </mat-error>
              }
            </mat-form-field>
          </div>
        </div>
    
        <!-- Dietary Information -->
        <div class="form-section">
          <h3>Dietary Information</h3>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Dietary Options</mat-label>
              <mat-select formControlName="dietaryOptionIds" multiple [showDescription]="true">
                @for (option in dietaryOptions; track option in dietaryOptions) {
                  <mat-option [value]="option.referenceId">
                    {{ option.referenceName }}
                  </mat-option>
                }
              </mat-select>
              <mat-hint>Select all that apply</mat-hint>
            </mat-form-field>
          </div>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Allergen Information</mat-label>
              <mat-select formControlName="allergenIds" multiple [showDescription]="true">
                @for (allergen in allergens; track allergen in allergens) {
                  <mat-option [value]="allergen.referenceId">
                    {{ allergen.referenceName }}
                  </mat-option>
                }
              </mat-select>
              <mat-hint>Select allergens this recipe contains</mat-hint>
            </mat-form-field>
          </div>
        </div>
    
        <!-- Instructions -->
        <div class="form-section">
          <h3>Cooking Instructions</h3>
    
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Instructions</mat-label>
              <textarea matInput formControlName="instructions" rows="6" placeholder="Enter step-by-step cooking instructions"></textarea>
              @if (recipeForm.get('instructions')?.hasError('required')) {
                <mat-error>
                  Cooking instructions are required
                </mat-error>
              }
            </mat-form-field>
          </div>
        </div>
    
        <!-- Form Actions -->
        <div class="form-actions">
          <button mat-button type="button" (click)="onCancel()">Cancel</button>
          <button mat-raised-button color="primary" type="submit" [disabled]="recipeForm.invalid || isSubmitting">
            @if (isSubmitting) {
              <mat-icon class="spinning">refresh</mat-icon>
            }
            {{ isSubmitting ? 'Saving...' : (isEditMode ? 'Update Recipe' : 'Create Recipe') }}
          </button>
        </div>
      </form>
    
      <!-- Selected Options Display -->
      @if (hasSelectedOptions()) {
        <div class="selected-options">
          <h4>Selected Options:</h4>
          <div class="options-grid">
            @if (getSelectedDifficulty()) {
              <div class="option-item">
                <strong>Difficulty:</strong> {{ getSelectedDifficulty()?.referenceName }}
              </div>
            }
            @if (getSelectedCuisine()) {
              <div class="option-item">
                <strong>Cuisine:</strong> {{ getSelectedCuisine()?.referenceName }}
              </div>
            }
            @if (getSelectedMealType()) {
              <div class="option-item">
                <strong>Meal Type:</strong> {{ getSelectedMealType()?.referenceName }}
              </div>
            }
            @if (getSelectedDietaryOptions().length > 0) {
              <div class="option-item">
                <strong>Dietary:</strong> {{ getSelectedDietaryOptions().map(o => o.referenceName).join(', ') }}
              </div>
            }
            @if (getSelectedAllergens().length > 0) {
              <div class="option-item">
                <strong>Allergens:</strong> {{ getSelectedAllergens().map(o => o.referenceName).join(', ') }}
              </div>
            }
          </div>
        </div>
      }
    </div>
    `,
  styleUrls: ['./recipe-form.component.scss']
})
export class RecipeFormComponent implements OnInit, OnDestroy {
  recipeForm!: FormGroup;
  isEditMode = false;
  isSubmitting = false;

  // Reference data
  difficulties: any[] = [];
  cuisines: any[] = [];
  mealTypes: any[] = [];
  dietaryOptions: any[] = [];
  allergens: any[] = [];

  // Make constants available in template
  readonly REFERENCE_IDS = REFERENCE_IDS;

  private destroy$ = new Subject<void>();

  constructor(
    private fb: FormBuilder,
    private recipeReferenceService: RecipeReferenceService
  ) {
    this.initializeForm();
  }

  ngOnInit(): void {
    this.loadReferenceData();
    this.setupFormListeners();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private initializeForm(): void {
    this.recipeForm = this.fb.group({
      name: ['', Validators.required],
      description: ['', Validators.required],
      prepTime: [30, [Validators.required, Validators.min(1)]],
      cookTime: [45, [Validators.min(0)]],
      difficultyId: [null, Validators.required],
      cuisineTypeId: [null, Validators.required],
      mealTypeId: [null, Validators.required],
      servings: [4, [Validators.required, Validators.min(1)]],
      dietaryOptionIds: [[]],
      allergenIds: [[]],
      instructions: ['', Validators.required]
    });
  }

  private loadReferenceData(): void {
    // Load all reference data in bulk for performance
    this.recipeReferenceService.getRecipeReferencesBulk()
      .pipe(takeUntil(this.destroy$))
      .subscribe(({ difficulties, cuisines, mealTypes, dietaryOptions, allergens }) => {
        this.difficulties = difficulties;
        this.cuisines = cuisines;
        this.mealTypes = mealTypes;
        this.dietaryOptions = dietaryOptions;
        this.allergens = allergens;
      });
  }

  private setupFormListeners(): void {
    // Listen for form changes to update selected options display
    this.recipeForm.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        // Form validation and UI updates can go here
      });
  }

  hasSelectedOptions(): boolean {
    const formValue = this.recipeForm.value;
    return formValue.difficultyId ||
      formValue.cuisineTypeId ||
      formValue.mealTypeId ||
      (formValue.dietaryOptionIds && formValue.dietaryOptionIds.length > 0) ||
      (formValue.allergenIds && formValue.allergenIds.length > 0);
  }

  getSelectedDifficulty(): any {
    const difficultyId = this.recipeForm.get('difficultyId')?.value;
    return this.difficulties.find(d => d.referenceId === difficultyId);
  }

  getSelectedCuisine(): any {
    const cuisineId = this.recipeForm.get('cuisineTypeId')?.value;
    return this.cuisines.find(c => c.referenceId === cuisineId);
  }

  getSelectedMealType(): any {
    const mealTypeId = this.recipeForm.get('mealTypeId')?.value;
    return this.mealTypes.find(m => m.referenceId === mealTypeId);
  }

  getSelectedDietaryOptions(): any[] {
    const dietaryIds = this.recipeForm.get('dietaryOptionIds')?.value || [];
    return this.dietaryOptions.filter(d => dietaryIds.includes(d.referenceId));
  }

  getSelectedAllergens(): any[] {
    const allergenIds = this.recipeForm.get('allergenIds')?.value || [];
    return this.allergens.filter(a => allergenIds.includes(a.referenceId));
  }

  onSubmit(): void {
    if (this.recipeForm.valid) {
      this.isSubmitting = true;

      const formValue = this.recipeForm.value;
      console.log('Recipe form submitted:', formValue);

      // Simulate API call
      setTimeout(() => {
        this.isSubmitting = false;
        alert(`Recipe ${this.isEditMode ? 'updated' : 'created'} successfully!`);
        this.resetForm();
      }, 2000);
    }
  }

  onCancel(): void {
    if (this.recipeForm.dirty) {
      if (confirm('Are you sure you want to cancel? All changes will be lost.')) {
        this.resetForm();
      }
    } else {
      this.resetForm();
    }
  }

  private resetForm(): void {
    this.recipeForm.reset({
      prepTime: 30,
      cookTime: 45,
      servings: 4,
      dietaryOptionIds: [],
      allergenIds: []
    });
  }
}
