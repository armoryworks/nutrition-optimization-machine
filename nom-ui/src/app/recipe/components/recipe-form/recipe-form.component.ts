import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { AmwInputComponent, AmwTextareaComponent, AmwSelectComponent, AmwButtonComponent, AmwIconComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

import { RecipeReferenceService } from '../../services/recipe-reference.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

@Component({
  selector: 'app-recipe-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwSelectComponent,
    AmwButtonComponent,
    AmwIconComponent,
    AmwProgressSpinnerComponent,
  ],
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
            <amw-input
              formControlName="name"
              label="Recipe Name"
              placeholder="Enter recipe name"
              [required]="true"
              [errorMessage]="recipeForm.get('name')?.hasError('required') ? 'Recipe name is required' : ''"
              class="form-field">
            </amw-input>
          </div>

          <div class="form-row">
            <amw-textarea
              formControlName="description"
              label="Description"
              placeholder="Describe your recipe"
              [rows]="3"
              [required]="true"
              [errorMessage]="recipeForm.get('description')?.hasError('required') ? 'Description is required' : ''"
              class="form-field">
            </amw-textarea>
          </div>

          <div class="form-row">
            <amw-input
              formControlName="prepTime"
              label="Preparation Time (minutes)"
              type="number"
              [required]="true"
              [errorMessage]="getPrepTimeError()"
              class="form-field">
            </amw-input>

            <amw-input
              formControlName="cookTime"
              label="Cook Time (minutes)"
              type="number"
              [errorMessage]="recipeForm.get('cookTime')?.hasError('min') ? 'Cook time cannot be negative' : ''"
              class="form-field">
            </amw-input>
          </div>
        </div>

        <!-- Recipe Classification -->
        <div class="form-section">
          <h3>Recipe Classification</h3>

          <div class="form-row">
            <amw-select
              formControlName="difficultyId"
              label="Difficulty Level"
              [required]="true"
              [options]="getDifficultyOptions()"
              [errorMessage]="recipeForm.get('difficultyId')?.hasError('required') ? 'Difficulty level is required' : ''"
              class="form-field">
            </amw-select>

            <amw-select
              formControlName="cuisineTypeId"
              label="Cuisine Type"
              [required]="true"
              [options]="getCuisineOptions()"
              [errorMessage]="recipeForm.get('cuisineTypeId')?.hasError('required') ? 'Cuisine type is required' : ''"
              class="form-field">
            </amw-select>
          </div>

          <div class="form-row">
            <amw-select
              formControlName="mealTypeId"
              label="Meal Type"
              [required]="true"
              [options]="getMealTypeOptions()"
              [errorMessage]="recipeForm.get('mealTypeId')?.hasError('required') ? 'Meal type is required' : ''"
              class="form-field">
            </amw-select>

            <amw-input
              formControlName="servings"
              label="Servings"
              type="number"
              [required]="true"
              [errorMessage]="getServingsError()"
              class="form-field">
            </amw-input>
          </div>
        </div>

        <!-- Dietary Information -->
        <div class="form-section">
          <h3>Dietary Information</h3>

          <div class="form-row">
            <amw-select
              formControlName="dietaryOptionIds"
              label="Dietary Options"
              [multiple]="true"
              [options]="getDietaryOptions()"
              hint="Select all that apply"
              class="form-field">
            </amw-select>
          </div>

          <div class="form-row">
            <amw-select
              formControlName="allergenIds"
              label="Allergen Information"
              [multiple]="true"
              [options]="getAllergenOptions()"
              hint="Select allergens this recipe contains"
              class="form-field">
            </amw-select>
          </div>
        </div>

        <!-- Instructions -->
        <div class="form-section">
          <h3>Cooking Instructions</h3>

          <div class="form-row">
            <amw-textarea
              formControlName="instructions"
              label="Instructions"
              placeholder="Enter step-by-step cooking instructions"
              [rows]="6"
              [required]="true"
              [errorMessage]="recipeForm.get('instructions')?.hasError('required') ? 'Cooking instructions are required' : ''"
              class="form-field">
            </amw-textarea>
          </div>
        </div>

        <!-- Form Actions -->
        <div class="form-actions">
          <amw-button variant="text" type="button" (click)="onCancel()">Cancel</amw-button>
          <amw-button
            variant="filled"
            color="primary"
            type="submit"
            [disabled]="recipeForm.invalid || isSubmitting">
            @if (isSubmitting) {
              <amw-icon name="refresh" class="spinning"></amw-icon>
            }
            {{ isSubmitting ? 'Saving...' : (isEditMode ? 'Update Recipe' : 'Create Recipe') }}
          </amw-button>
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

  // Helper methods for AMW select options
  getDifficultyOptions(): Array<{ value: any; label: string }> {
    return this.difficulties.map(d => ({ value: d.referenceId, label: d.referenceName }));
  }

  getCuisineOptions(): Array<{ value: any; label: string }> {
    return this.cuisines.map(c => ({ value: c.referenceId, label: c.referenceName }));
  }

  getMealTypeOptions(): Array<{ value: any; label: string }> {
    return this.mealTypes.map(m => ({ value: m.referenceId, label: m.referenceName }));
  }

  getDietaryOptions(): Array<{ value: any; label: string }> {
    return this.dietaryOptions.map(d => ({ value: d.referenceId, label: d.referenceName }));
  }

  getAllergenOptions(): Array<{ value: any; label: string }> {
    return this.allergens.map(a => ({ value: a.referenceId, label: a.referenceName }));
  }

  // Helper methods for error messages
  getPrepTimeError(): string {
    const control = this.recipeForm.get('prepTime');
    if (control?.hasError('required')) return 'Preparation time is required';
    if (control?.hasError('min')) return 'Preparation time must be at least 1 minute';
    return '';
  }

  getServingsError(): string {
    const control = this.recipeForm.get('servings');
    if (control?.hasError('required')) return 'Number of servings is required';
    if (control?.hasError('min')) return 'Servings must be at least 1';
    return '';
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
