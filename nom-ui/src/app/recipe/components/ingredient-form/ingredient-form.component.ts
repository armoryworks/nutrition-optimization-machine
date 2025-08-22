import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, takeUntil } from 'rxjs/operators';

import { RecipeService } from '../../services/recipe.service';
import { IngredientModel } from '../../models/ingredient.model';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

export interface IngredientFormData {
  recipeId?: number;
  ingredientName?: string;
}

export interface IngredientFormConfig {
  title: string;
  subtitle?: string;
  submitText: string;
  showCancelButton: boolean;
  cancelText: string;
  maxWidth?: string;
}

@Component({
  selector: 'nom-ingredient-form',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    BaseFormComponent,
    BasePageComponent,
  ],
  templateUrl: './ingredient-form.component.html',
  styleUrls: ['./ingredient-form.component.scss']
})
export class IngredientFormComponent implements OnInit, OnDestroy {
  @Input() mode: 'create' | 'edit' = 'create';
  @Input() ingredient?: IngredientModel | null = null;
  @Input() isModal = false;
  @Input() modalData?: IngredientFormData;
  @Input() showDuplicateChecking = true;
  @Input() showNavigation = true;

  @Output() formSubmitted = new EventEmitter<IngredientModel>();
  @Output() formCancelled = new EventEmitter<void>();
  @Output() duplicateFound = new EventEmitter<IngredientModel>();

  private nonNullableFb = inject(NonNullableFormBuilder);
  private recipeService = inject(RecipeService);
  private snackBar = inject(MatSnackBar);
  private cdr = inject(ChangeDetectorRef);

  ingredientForm: FormGroup;
  isLoading = false;
  isSubmitting = false;
  isCheckingDuplicate = false;
  existingIngredient: IngredientModel | null = null;
  measurements: any[] = [];
  error: string | null = null;
  private destroy$ = new Subject<void>();

  // Form configuration based on mode and context
  formConfig: IngredientFormConfig = {
    title: 'Create Ingredient',
    subtitle: 'Add a new ingredient to the database',
    submitText: 'Create Ingredient',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px'
  };

  // Page configuration for non-modal usage
  pageConfig: BasePageConfig = {
    title: 'Create New Ingredient',
    subtitle: 'Add ingredient information and nutritional data',
    showBackButton: true,
    showRefreshButton: true,
    refreshButtonText: 'Refresh'
  };

  constructor() {
    this.ingredientForm = this.nonNullableFb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]],
      description: ['', [Validators.maxLength(2047)]],
      nutrients: this.nonNullableFb.array([])
    });
  }

  ngOnInit(): void {
    this.initializeForm();
    this.setupDuplicateChecking();
    this.loadMeasurementTypes();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private initializeForm(): void {
    // Set form configuration based on mode and context
    if (this.mode === 'edit' && this.ingredient) {
      this.formConfig = {
        title: 'Edit Ingredient',
        subtitle: 'Update the core properties and nutritional information for this ingredient.',
        submitText: 'Save Changes',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '600px'
      };

      this.pageConfig = {
        title: 'Edit Ingredient',
        subtitle: 'Update ingredient information and nutritional data',
        showBackButton: true,
        showRefreshButton: true,
        refreshButtonText: 'Refresh'
      };

      // Populate form with existing data
      this.ingredientForm.patchValue(this.ingredient);
      this.populateNutrients(this.ingredient.nutrients || []);
    } else {
      // Create mode
      if (this.isModal && this.modalData?.ingredientName) {
        this.ingredientForm.patchValue({
          name: this.modalData.ingredientName
        });
      }
    }
  }

  private setupDuplicateChecking(): void {
    if (!this.showDuplicateChecking || this.mode === 'edit') {
      return;
    }

    this.ingredientForm.get('name')?.valueChanges.pipe(
      debounceTime(500),
      distinctUntilChanged(),
      switchMap(name => {
        if (name && name.trim().length > 2) {
          this.isCheckingDuplicate = true;
          return this.recipeService.searchIngredients(name.trim());
        } else {
          this.existingIngredient = null;
          this.isCheckingDuplicate = false;
          return of([]);
        }
      }),
      takeUntil(this.destroy$)
    ).subscribe({
      next: (ingredients) => {
        this.isCheckingDuplicate = false;
        // Check if any ingredient has the exact same name (case-insensitive)
        const exactMatch = ingredients.find(ing =>
          ing.name.toLowerCase() === this.ingredientForm.get('name')?.value?.toLowerCase()
        );
        this.existingIngredient = exactMatch || null;

        if (this.existingIngredient) {
          this.duplicateFound.emit(this.existingIngredient);
        }
      },
      error: (error) => {
        console.error('Error checking for duplicate ingredients:', error);
        this.isCheckingDuplicate = false;
      }
    });
  }

  private loadMeasurementTypes(): void {
    // Load measurements from the new measurement system
    // This should be enhanced to load from the measurement service
    this.measurements = [
      { id: 1, name: 'Gram', symbol: 'g' },
      { id: 2, name: 'Milligram', symbol: 'mg' },
      { id: 3, name: 'Microgram', symbol: 'µg' }
    ];
  }

  private populateNutrients(nutrients: any[]): void {
    // Clear existing nutrients
    while (this.nutrients.length !== 0) {
      this.nutrients.removeAt(0);
    }

    // Add nutrients from existing ingredient
    nutrients.forEach(nutrient => {
      this.nutrients.push(this.nonNullableFb.group({
        nutrientId: [nutrient.nutrientId || '', Validators.required],
        amount: [nutrient.amount || 0, [Validators.required, Validators.min(0)]],
        measurementId: [nutrient.measurementId || 1, Validators.required]
      }));
    });
  }

  get nutrients(): FormArray {
    return this.ingredientForm.get('nutrients') as FormArray;
  }

  newNutrient(): FormGroup {
    return this.nonNullableFb.group({
      nutrientId: ['', Validators.required],
      amount: [0, [Validators.required, Validators.min(0)]],
      measurementId: [1, Validators.required]
    });
  }

  addNutrient(): void {
    this.nutrients.push(this.newNutrient());
  }

  removeNutrient(index: number): void {
    this.nutrients.removeAt(index);
  }

  onSubmit(): void {
    if (this.ingredientForm.invalid || this.isSubmitting) {
      this.ingredientForm.markAllAsTouched();
      return;
    }

    // Check if ingredient already exists (for create mode with duplicate checking)
    if (this.showDuplicateChecking && this.existingIngredient && this.mode === 'create') {
      // Emit the existing ingredient instead of creating a new one
      const result = {
        id: this.existingIngredient.id,
        name: this.existingIngredient.name,
        description: this.ingredientForm.get('description')?.value || '',
        fdcId: this.existingIngredient.fdcId
      } as IngredientModel;

      this.formSubmitted.emit(result);
      return;
    }

    this.isSubmitting = true;
    this.error = null;
    const formValue = this.ingredientForm.value;

    // Create the ingredient request
    const request = {
      name: formValue.name,
      description: formValue.description,
      nutrients: formValue.nutrients
    };

    const request$ = this.mode === 'edit' && this.ingredient?.id
      ? this.recipeService.updateIngredient(this.ingredient.id, request)
      : this.recipeService.createIngredient(request);

    request$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        const action = this.mode === 'edit' ? 'updated' : 'created';
        this.snackBar.open(`Ingredient ${action} successfully!`, 'Close', { duration: 3000 });

        // Emit the result
        this.formSubmitted.emit(result);
      },
      error: (error) => {
        console.error(`Error ${this.mode === 'edit' ? 'updating' : 'creating'} ingredient:`, error);
        this.error = `Failed to ${this.mode === 'edit' ? 'update' : 'create'} ingredient. Please try again.`;
        this.isSubmitting = false;
      }
    });
  }

  onCancel(): void {
    this.formCancelled.emit();
  }

  onBack(): void {
    this.formCancelled.emit();
  }

  onRefresh(): void {
    this.error = null;
    this.loadIngredient();
  }

  onRetry(): void {
    this.error = null;
    this.loadIngredient();
  }

  useExistingIngredient(): void {
    if (this.existingIngredient) {
      const result = {
        id: this.existingIngredient.id,
        name: this.existingIngredient.name,
        description: this.ingredientForm.get('description')?.value || '',
        fdcId: this.existingIngredient.fdcId
      } as IngredientModel;

      this.formSubmitted.emit(result);
    }
  }

  private loadIngredient(): void {
    if (this.ingredient?.id) {
      this.isLoading = true;
      this.recipeService.getIngredientDetails(this.ingredient.id).pipe(
        takeUntil(this.destroy$)
      ).subscribe({
        next: (ingredientData: IngredientModel) => {
          this.ingredient = ingredientData;
          this.ingredientForm.patchValue(ingredientData);
          this.populateNutrients(ingredientData.nutrients || []);
          this.isLoading = false;
        },
        error: () => {
          console.error('Error loading ingredient');
          this.error = 'Failed to load ingredient. Please try again.';
          this.isLoading = false;
        }
      });
    }
  }
}
