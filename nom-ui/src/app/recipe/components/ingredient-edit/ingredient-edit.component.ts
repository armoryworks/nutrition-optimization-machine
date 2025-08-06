// File: nom-ui/src/app/recipe/components/ingredient-edit/ingredient-edit.component.ts

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, of } from 'rxjs';
import { switchMap, takeUntil } from 'rxjs/operators';

import { RecipeService } from '../../services/recipe.service';
import { IngredientModel } from '../../models/ingredient.model';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

@Component({
  selector: 'nom-ingredient-edit',
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
  templateUrl: './ingredient-edit.component.html',
  styleUrls: ['./ingredient-edit.component.scss']
})
export class IngredientEditComponent implements OnInit, OnDestroy {
  ingredientForm: FormGroup;
  isLoading = false;
  isSubmitting = false;
  isEditMode = false;
  ingredientId: number = 0;
  ingredient: IngredientModel | null = null;
  measurementTypes: ReferenceItemModel[] = [];
  error: string | null = null;
  private destroy$ = new Subject<void>();

  formConfig: BaseFormConfig = {
    title: 'Edit Ingredient',
    subtitle: 'Update ingredient information',
    submitText: 'Update Ingredient',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px',
  };

  pageConfig: BasePageConfig = {
    title: 'Edit Ingredient',
    subtitle: 'Update ingredient information and nutritional data',
    showBackButton: true,
    showRefreshButton: true,
    backButtonText: 'Back to Ingredients',
    refreshButtonText: 'Refresh'
  };

  constructor(
    private nonNullableFb: NonNullableFormBuilder,
    private ingredientService: RecipeService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.ingredientForm = this.nonNullableFb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]],
      description: ['', [Validators.maxLength(2047)]],
      nutrients: this.nonNullableFb.array([])
    });
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(
      switchMap(params => {
        const id = params.get('id');
        if (id) {
          this.isEditMode = true;
          this.ingredientId = +id;
          this.formConfig.title = 'Edit Ingredient';
          this.formConfig.subtitle = 'Update the core properties and nutritional information for this ingredient.';
          this.formConfig.submitText = 'Save Changes';
          return this.ingredientService.getIngredientDetails(+id);
        }
        this.isLoading = false;
        return of(null);
      }),
      takeUntil(this.destroy$)
    ).subscribe({
      next: (ingredientData: IngredientModel | null) => {
        if (this.isEditMode && ingredientData) {
          this.ingredientForm.patchValue(ingredientData);
        }
        this.isLoading = false;
      },
      error: (error: any) => {
        console.error('Error loading ingredient:', error);
        this.error = 'Failed to load ingredient. Please try again.';
        this.isLoading = false;
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get nutrients(): FormArray {
    return this.ingredientForm.get('nutrients') as FormArray;
  }

  newNutrient(): FormGroup {
    return this.nonNullableFb.group({
      nutrientId: ['', [Validators.required]],
      amount: [0, [Validators.required, Validators.min(0)]],
      measurementTypeId: ['4003', [Validators.required]]
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
      return;
    }

    this.isSubmitting = true;
    this.error = null;

    const formValue = this.ingredientForm.value;
    const request$ = this.isEditMode && this.ingredientId
      ? this.ingredientService.updateIngredient(this.ingredientId, formValue)
      : this.ingredientService.createIngredient(formValue);

    request$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (ingredient: IngredientModel) => {
        const action = this.isEditMode ? 'updated' : 'created';
        this.snackBar.open(`Ingredient ${action} successfully!`, 'Close', { duration: 3000 });
        this.router.navigate(['/ingredient', ingredient.id]);
      },
      error: (error: any) => {
        console.error('Error saving ingredient:', error);
        this.error = `Failed to ${this.isEditMode ? 'update' : 'create'} ingredient. Please try again.`;
        this.isSubmitting = false;
      }
    });
  }

  onCancel(): void {
    this.router.navigate(['/ingredients']);
  }

  onBack(): void {
    this.router.navigate(['/ingredients']);
  }

  onRefresh(): void {
    this.error = null;
    this.loadIngredient();
  }

  onRetry(): void {
    this.error = null;
    this.loadIngredient();
  }

  private loadIngredient(): void {
    if (this.ingredientId) {
      this.isLoading = true;
      this.ingredientService.getIngredientDetails(this.ingredientId).pipe(
        takeUntil(this.destroy$)
      ).subscribe({
        next: (ingredientData: IngredientModel) => {
          this.ingredient = ingredientData;
          this.ingredientForm.patchValue(ingredientData);
          this.isLoading = false;
        },
        error: (error: any) => {
          console.error('Error loading ingredient:', error);
          this.error = 'Failed to load ingredient. Please try again.';
          this.isLoading = false;
        }
      });
    }
  }
}