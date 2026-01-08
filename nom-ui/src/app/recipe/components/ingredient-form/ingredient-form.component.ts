import { Component, OnInit, OnDestroy, OnChanges, SimpleChanges, ChangeDetectorRef, inject, input, output, signal } from '@angular/core';

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
import { UpdateIngredientRequestModel } from '../../models/update-ingredient-request.model';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

import { IngredientFormData } from './ingredient-form-data.interface';
import { IngredientFormConfig } from './ingredient-form-config.interface';

// Re-export the interfaces for components that need them
export type { IngredientFormData } from './ingredient-form-data.interface';
export type { IngredientFormConfig } from './ingredient-form-config.interface';

@Component({
  selector: 'nom-ingredient-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    BaseFormComponent,
    BasePageComponent
],
  templateUrl: './ingredient-form.component.html',
  styleUrls: ['./ingredient-form.component.scss']
})
export class IngredientFormComponent implements OnInit, OnDestroy, OnChanges {
  mode = input<'create' | 'edit'>('create');
  ingredient = input<IngredientModel | null>(null);
  isModal = input(false);
  modalData = input<IngredientFormData>();
  showDuplicateChecking = input(true);
  showNavigation = input(true);

  formSubmitted = output<IngredientModel>();
  formCancelled = output<void>();
  duplicateFound = output<IngredientModel>();

  private nonNullableFb = inject(NonNullableFormBuilder);
  private recipeService = inject(RecipeService);
  private referenceDataService = inject(ReferenceDataService);
  private snackBar = inject(MatSnackBar);
  private cdr = inject(ChangeDetectorRef);

  ingredientForm: FormGroup;
  isLoading = signal(false);
  isSubmitting = signal(false);
  isCheckingDuplicate = signal(false);
  existingIngredient = signal<IngredientModel | null>(null);
  measurements = signal<ReferenceItemModel[]>([]);
  nutrientTypes = signal<ReferenceItemModel[]>([]);
  error = signal<string | null>(null);
  private destroy$ = new Subject<void>();

  // Form configuration based on mode and context
  formConfig = signal<IngredientFormConfig>({
    title: 'Create Ingredient',
    subtitle: 'Add a new ingredient to the database',
    submitText: 'Create Ingredient',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px'
  });

  // Page configuration for non-modal usage
  pageConfig = signal<BasePageConfig>({
    title: 'Create New Ingredient',
    subtitle: 'Add ingredient information and nutritional data',
    showBackButton: true,
    showRefreshButton: true,
    refreshButtonText: 'Refresh'
  });

  constructor() {
    this.ingredientForm = this.nonNullableFb.group({
      id: [0], // Add ID field to the form
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]],
      description: ['', [Validators.maxLength(2047)]],
      nutrients: this.nonNullableFb.array([this.newNutrient()])
    });
  }

  ngOnInit(): void {
    this.initializeForm();
    this.setupDuplicateChecking();
    this.loadMeasurementTypes();
    this.loadNutrientTypes();

    // Add a default nutrient if none exist
    if (this.nutrients.length === 0) {
      this.addNutrient();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    // React to changes in ingredient input
    if (changes['ingredient'] && !changes['ingredient'].firstChange) {
      console.log('Ingredient input changed:', changes['ingredient'].currentValue);
      this.initializeForm();
    }

    // React to changes in mode input
    if (changes['mode'] && !changes['mode'].firstChange) {
      console.log('Mode input changed:', changes['mode'].currentValue);
      this.initializeForm();
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private initializeForm(): void {
    // Set form configuration based on mode and context
    if (this.mode() === 'edit' && this.ingredient()) {
      this.formConfig.set({
        title: 'Edit Ingredient',
        subtitle: 'Update the core properties and nutritional information for this ingredient.',
        submitText: 'Save Changes',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '600px'
      });

      this.pageConfig.set({
        title: 'Edit Ingredient',
        subtitle: 'Update ingredient information and nutritional data',
        showBackButton: true,
        showRefreshButton: true,
        refreshButtonText: 'Refresh'
      });

      // Populate form with existing data
      this.ingredientForm.patchValue(this.ingredient()!);
      this.populateNutrients(this.ingredient()?.nutrients || []);
    } else {
      // Create mode
      if (this.isModal() && this.modalData()?.ingredientName) {
        this.ingredientForm.patchValue({
          name: this.modalData()!.ingredientName
        });
      }
    }
  }

  private setupDuplicateChecking(): void {
    if (!this.showDuplicateChecking() || this.mode() === 'edit') {
      return;
    }

    this.ingredientForm.get('name')?.valueChanges.pipe(
      debounceTime(500),
      distinctUntilChanged(),
      switchMap(name => {
        if (name && name.trim().length > 2) {
          this.isCheckingDuplicate.set(true);
          return this.recipeService.searchIngredients(name.trim());
        } else {
          this.existingIngredient.set(null);
          this.isCheckingDuplicate.set(false);
          return of([]);
        }
      }),
      takeUntil(this.destroy$)
    ).subscribe({
      next: (ingredients) => {
        this.isCheckingDuplicate.set(false);
        // Check if any ingredient has the exact same name (case-insensitive)
        const exactMatch = ingredients.find(ing =>
          ing.name.toLowerCase() === this.ingredientForm.get('name')?.value?.toLowerCase()
        );
        this.existingIngredient.set(exactMatch || null);

        if (this.existingIngredient()) {
          this.duplicateFound.emit(this.existingIngredient()!);
        }
      },
      error: (error) => {
        console.error('Error checking for duplicate ingredients:', error);
        this.isCheckingDuplicate.set(false);
      }
    });
  }

  private loadMeasurementTypes(): void {
    // Load measurements from the backend API
    this.referenceDataService.getMeasurementTypes().pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: (measurements) => {
        this.measurements.set(measurements);
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error loading measurement types:', error);
        // Fallback to empty array if API fails
        this.measurements.set([]);
      }
    });
  }

  private loadNutrientTypes(): void {
    // Load nutrient types from the backend API
    this.referenceDataService.getNutrientTypes().pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: (nutrients) => {
        this.nutrientTypes.set(nutrients);
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error loading nutrient types:', error);
        // Fallback to empty array if API fails
        this.nutrientTypes.set([]);
      }
    });
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

    // Only add a default nutrient in create mode, not edit mode
    if (this.nutrients.length === 0 && this.mode() === 'create') {
      this.addNutrient();
    }
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
    if (this.ingredientForm.invalid || this.isSubmitting()) {
      this.ingredientForm.markAllAsTouched();
      return;
    }

    // Ensure nutrients array exists and has at least one item
    if (this.nutrients.length === 0) {
      this.addNutrient();
      return;
    }

    // Check if ingredient already exists (for create mode with duplicate checking)
    const existingIng = this.existingIngredient();
    if (this.showDuplicateChecking() && existingIng && this.mode() === 'create') {
      // Emit the existing ingredient instead of creating a new one
      const result = {
        id: existingIng.id,
        name: existingIng.name,
        description: this.ingredientForm.get('description')?.value || '',
        fdcId: existingIng.fdcId
      } as IngredientModel;

      this.formSubmitted.emit(result);
      return;
    }

    this.isSubmitting.set(true);
    this.error.set(null);
    const formValue = this.ingredientForm.value;

    // Convert string values to numbers for nutrients
    const processedNutrients = formValue.nutrients.map((nutrient: any) => ({
      nutrientId: parseInt(nutrient.nutrientId.toString(), 10),
      amount: nutrient.amount,
      measurementId: parseInt(nutrient.measurementId.toString(), 10)
    }));

    const request$ = this.mode() === 'edit' && this.ingredient()?.id
      ? this.recipeService.updateIngredient(this.ingredient()!.id, {
        id: this.ingredient()!.id, // Use ID from form (now includes the ingredient ID)
        name: formValue.name,
        description: formValue.description,
        nutrients: processedNutrients
      })
      : this.recipeService.createIngredient({
        name: formValue.name,
        description: formValue.description,
        nutrients: processedNutrients
      });

    request$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        const action = this.mode() === 'edit' ? 'updated' : 'created';
        this.snackBar.open(`Ingredient ${action} successfully!`, 'Close', { duration: 3000 });

        // Emit the result
        this.formSubmitted.emit(result);
      },
      error: (error) => {
        console.error(`Error ${this.mode() === 'edit' ? 'updating' : 'creating'} ingredient:`, error);
        this.error.set(`Failed to ${this.mode() === 'edit' ? 'update' : 'create'} ingredient. Please try again.`);
        this.isSubmitting.set(false);
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
    this.error.set(null);
    this.loadIngredient();
  }

  onRetry(): void {
    this.error.set(null);
    this.loadIngredient();
  }

  useExistingIngredient(): void {
    const existingIng = this.existingIngredient();
    if (existingIng) {
      const result = {
        id: existingIng.id,
        name: existingIng.name,
        description: this.ingredientForm.get('description')?.value || '',
        fdcId: existingIng.fdcId
      } as IngredientModel;

      this.formSubmitted.emit(result);
    }
  }

  private loadIngredient(): void {
    const currentIngredient = this.ingredient();
    if (currentIngredient?.id) {
      this.isLoading.set(true);
      this.recipeService.getIngredientDetails(currentIngredient.id).pipe(
        takeUntil(this.destroy$)
      ).subscribe({
        next: (ingredientData: IngredientModel) => {
          // Note: Cannot reassign input signal, but can use the data
          this.ingredientForm.patchValue(ingredientData);
          this.populateNutrients(ingredientData.nutrients || []);
          this.isLoading.set(false);
        },
        error: () => {
          console.error('Error loading ingredient');
          this.error.set('Failed to load ingredient. Please try again.');
          this.isLoading.set(false);
        }
      });
    }
  }
}
