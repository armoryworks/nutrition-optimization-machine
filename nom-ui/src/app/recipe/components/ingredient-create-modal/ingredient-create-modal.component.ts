// File: nom-ui/src/app/recipe/components/ingredient-create-modal/ingredient-create-modal.component.ts

import { Component, EventEmitter, Input, OnInit, Output, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RecipeService } from '../../services/recipe.service';
import { IngredientSearchResponseModel } from '../../models/ingredient-search-response.model';
import { Observable, of } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, take } from 'rxjs/operators';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

export interface IngredientCreateModalData {
    ingredientName: string;
}

@Component({
    selector: 'app-ingredient-create-modal',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatDialogModule,
        MatProgressSpinnerModule,
        MatIconModule,
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        BaseFormComponent
    ],
    templateUrl: './ingredient-create-modal.component.html',
    styleUrls: ['./ingredient-create-modal.component.scss']
})
export class IngredientCreateModalComponent implements OnInit {
    ingredientForm: FormGroup;
    isLoading = false;
    isSubmitting = false;
    existingIngredient: IngredientSearchResponseModel | null = null;
    isCheckingDuplicate = false;

    formConfig: BaseFormConfig = {
        title: 'Create New Ingredient',
        subtitle: 'Define the core properties and nutritional information for this ingredient.',
        submitText: 'Create Ingredient',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '600px'
    };

    constructor(
        private fb: FormBuilder,
        private dialogRef: MatDialogRef<IngredientCreateModalComponent>,
        private recipeService: RecipeService,
        @Inject(MAT_DIALOG_DATA) public data: IngredientCreateModalData
    ) {
        this.ingredientForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(2047)]],
            description: ['', [Validators.maxLength(4095)]],
            nutrients: this.fb.array([])
        });

        // Set up duplicate checking
        this.setupDuplicateChecking();
    }

    ngOnInit(): void {
        // Pre-populate the name field with the search term
        if (this.data?.ingredientName) {
            this.ingredientForm.patchValue({
                name: this.data.ingredientName
            });
        }
    }

    private setupDuplicateChecking(): void {
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
            })
        ).subscribe({
            next: (ingredients) => {
                this.isCheckingDuplicate = false;
                // Check if any ingredient has the exact same name (case-insensitive)
                const exactMatch = ingredients.find(ing =>
                    ing.name.toLowerCase() === this.ingredientForm.get('name')?.value?.toLowerCase()
                );
                this.existingIngredient = exactMatch || null;
            },
            error: (error) => {
                console.error('Error checking for duplicate ingredients:', error);
                this.isCheckingDuplicate = false;
            }
        });
    }

    get nutrients(): FormArray {
        return this.ingredientForm.get('nutrients') as FormArray;
    }

    newNutrient(): FormGroup {
        return this.fb.group({
            nutrientId: ['', Validators.required],
            amount: [0, [Validators.required, Validators.min(0)]],
            measurementTypeId: ['', Validators.required]
        });
    }

    addNutrient(): void {
        this.nutrients.push(this.newNutrient());
    }

    removeNutrient(index: number): void {
        this.nutrients.removeAt(index);
    }

    onSubmit(): void {
        if (this.ingredientForm.invalid) {
            this.ingredientForm.markAllAsTouched();
            return;
        }

        // Check if ingredient already exists
        if (this.existingIngredient) {
            // Instead of creating a new ingredient, return the existing one
            this.dialogRef.close({
                id: this.existingIngredient.id,
                name: this.existingIngredient.name,
                description: this.ingredientForm.get('description')?.value || '',
                fdcId: this.existingIngredient.fdcId
            });
            return;
        }

        this.isSubmitting = true;
        const formValue = this.ingredientForm.value;

        // Create the ingredient request
        const createRequest = {
            name: formValue.name,
            description: formValue.description,
            nutrients: formValue.nutrients
        };

        this.recipeService.createIngredient(createRequest).subscribe({
            next: (newIngredient) => {
                // Get the full ingredient details to return
                this.recipeService.getIngredientDetails(newIngredient.id).subscribe({
                    next: (ingredientDetails) => {
                        this.dialogRef.close(ingredientDetails);
                    },
                    error: (error) => {
                        console.error('Error fetching created ingredient details:', error);
                        // Still close with basic info if we can't get full details
                        this.dialogRef.close({
                            id: newIngredient.id,
                            name: formValue.name,
                            description: formValue.description
                        });
                    }
                });
            },
            error: (error) => {
                console.error('Error creating ingredient:', error);
                this.isSubmitting = false;
                // Check if it's a duplicate key error
                if (error?.error?.errors?.name?.includes('duplicate')) {
                    // Refresh the duplicate check
                    this.setupDuplicateChecking();
                }
            }
        });
    }

    useExistingIngredient(): void {
        if (this.existingIngredient) {
            this.dialogRef.close({
                id: this.existingIngredient.id,
                name: this.existingIngredient.name,
                description: this.ingredientForm.get('description')?.value || '',
                fdcId: this.existingIngredient.fdcId
            });
        }
    }

    onCancel(): void {
        this.dialogRef.close();
    }
} 