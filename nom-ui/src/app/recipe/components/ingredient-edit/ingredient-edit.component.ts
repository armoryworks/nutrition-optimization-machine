// File: nom-ui/src/app/recipe/components/ingredient-edit/ingredient-edit.component.ts

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { RecipeService } from '../../services/recipe.service';
import { Observable, of, Subject } from 'rxjs';
import { switchMap, takeUntil } from 'rxjs/operators';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

@Component({
    selector: 'app-ingredient-edit',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        RouterModule,
        MatProgressSpinnerModule,
        MatIconModule,
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        BaseFormComponent,
        BasePageComponent
    ],
    templateUrl: './ingredient-edit.component.html',
    styleUrls: ['./ingredient-edit.component.scss']
})
export class IngredientEditComponent implements OnInit, OnDestroy {
    ingredientForm: FormGroup;
    isEditMode = false;
    ingredientId: number | null = null;
    isLoading = true;
    isSubmitting = false;
    error: string | null = null;

    pageConfig: BasePageConfig = {
        title: 'Create New Ingredient',
        subtitle: 'Define the core properties and nutritional information for this ingredient.',
        showBackButton: true,
        maxWidth: '800px'
    };

    formConfig: BaseFormConfig = {
        title: '',
        subtitle: '',
        submitText: 'Create Ingredient',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '100%'
    };

    private destroy$ = new Subject<void>();

    constructor(
        private fb: FormBuilder,
        private route: ActivatedRoute,
        public router: Router,
        private recipeService: RecipeService
    ) {
        this.ingredientForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(2047)]],
            description: ['', [Validators.maxLength(4095)]],
            nutrients: this.fb.array([])
        });
    }

    ngOnInit(): void {
        this.route.paramMap.pipe(
            switchMap(params => {
                const id = params.get('id');
                if (id) {
                    this.isEditMode = true;
                    this.ingredientId = +id;
                    this.pageConfig.title = 'Edit Ingredient';
                    this.pageConfig.subtitle = 'Update the core properties and nutritional information for this ingredient.';
                    this.formConfig.submitText = 'Save Changes';
                    return this.recipeService.getIngredientDetails(+id);
                }
                this.isLoading = false;
                return of(null);
            }),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (ingredientData) => {
                if (this.isEditMode && ingredientData) {
                    this.ingredientForm.patchValue(ingredientData);
                }
                this.isLoading = false;
            },
            error: (error) => {
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
        return this.fb.group({
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
            ? this.recipeService.updateIngredient(this.ingredientId, formValue)
            : this.recipeService.createIngredient(formValue);

        request$.pipe(takeUntil(this.destroy$)).subscribe({
            next: (ingredient) => {
                const action = this.isEditMode ? 'updated' : 'created';
                // You might want to add a notification service here
                this.router.navigate(['/ingredient', ingredient.id]);
            },
            error: (error) => {
                console.error('Error saving ingredient:', error);
                this.error = `Failed to ${this.isEditMode ? 'update' : 'create'} ingredient. Please try again.`;
                this.isSubmitting = false;
            }
        });
    }

    onCancel(): void {
        this.router.navigate(['/ingredient']);
    }

    onBack(): void {
        this.router.navigate(['/ingredient']);
    }

    onRefresh(): void {
        if (this.ingredientId) {
            this.loadIngredient();
        }
    }

    onRetry(): void {
        this.error = null;
        if (this.ingredientId) {
            this.loadIngredient();
        }
    }

    private loadIngredient(): void {
        if (!this.ingredientId) return;

        this.isLoading = true;
        this.error = null;

        this.recipeService.getIngredientDetails(this.ingredientId).pipe(
            takeUntil(this.destroy$)
        ).subscribe({
            next: (ingredientData) => {
                this.ingredientForm.patchValue(ingredientData);
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading ingredient:', error);
                this.error = 'Failed to load ingredient. Please try again.';
                this.isLoading = false;
            }
        });
    }
}