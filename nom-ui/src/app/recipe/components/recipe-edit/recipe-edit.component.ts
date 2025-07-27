// File: nom-ui/src/app/recipe/components/recipe-edit/recipe-edit.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormControl, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { RecipeService } from '../../services/recipe.service';
import { Observable, of } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, startWith, finalize } from 'rxjs/operators';
import { CdkDragDrop, moveItemInArray, DragDropModule } from '@angular/cdk/drag-drop';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { IngredientSearchResponseModel } from '../../models/ingredient-search-response.model';
import { ReferenceItemModel } from '../../../common/models/reference-item.model'; // CORRECTED: Import from common/models
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'app-recipe-edit',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        RouterModule,
        DragDropModule,
        MatProgressSpinnerModule,
        MatIconModule,
        MatAutocompleteModule,
        MatCardModule,
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule
    ],
    templateUrl: './recipe-edit.component.html',
    styleUrls: ['./recipe-edit.component.scss']
})
export class RecipeEditComponent implements OnInit {
    recipeForm: FormGroup;
    isEditMode = false;
    recipeId: number | null = null;
    isLoading = true;
    pageTitle = 'Create Recipe';
    isSubmitting = false;

    ingredientSearchCtrl = new FormControl('');
    filteredIngredients$: Observable<IngredientSearchResponseModel[]>;
    measurementTypes$: Observable<ReferenceItemModel[]>;

    constructor(
        private fb: FormBuilder,
        private route: ActivatedRoute,
        public router: Router,
        private recipeService: RecipeService,
        private notificationService: NotificationService
    ) {
        this.recipeForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(511)]],
            description: ['', [Validators.maxLength(2047)]],
            ingredients: this.fb.array([]),
            steps: this.fb.array([])
        });

        this.filteredIngredients$ = this.ingredientSearchCtrl.valueChanges.pipe(
            startWith(''),
            debounceTime(300),
            distinctUntilChanged(),
            switchMap(value => (value && typeof value === 'string' && value.length > 1) ? this.recipeService.searchIngredients(value) : of([]))
        );

        this.measurementTypes$ = this.recipeService.getMeasurementTypes();
    }

    ngOnInit(): void {
        this.isLoading = false;
    }

    get ingredients(): FormArray {
        return this.recipeForm.get('ingredients') as FormArray;
    }

    createIngredientGroup(ingredient: IngredientSearchResponseModel): FormGroup {
        return this.fb.group({
            ingredientId: [ingredient.id, Validators.required],
            name: [ingredient.name],
            quantity: [1, [Validators.required, Validators.min(0.01)]],
            measurementTypeId: ['', Validators.required]
        });
    }

    onIngredientSelected(event: MatAutocompleteSelectedEvent): void {
        this.ingredients.push(this.createIngredientGroup(event.option.value));
        this.ingredientSearchCtrl.setValue('');
        event.option.focus();
    }

    removeIngredient(index: number): void {
        this.ingredients.removeAt(index);
    }

    displayIngredient(ingredient: IngredientSearchResponseModel): string {
        return ingredient ? ingredient.name : '';
    }

    get steps(): FormArray {
        return this.recipeForm.get('steps') as FormArray;
    }

    createStepGroup(): FormGroup {
        return this.fb.group({
            description: ['', Validators.required]
        });
    }

    addStep(): void {
        this.steps.push(this.createStepGroup());
    }

    removeStep(index: number): void {
        this.steps.removeAt(index);
    }

    dropStep(event: CdkDragDrop<string[]>) {
        moveItemInArray(this.steps.controls, event.previousIndex, event.currentIndex);
    }

    onSubmit(): void {
        if (this.recipeForm.invalid) {
            this.recipeForm.markAllAsTouched();
            this.notificationService.warning('Please correct the errors before submitting.');
            return;
        }

        this.isSubmitting = true;
        const formValue = this.recipeForm.value;

        if (this.isEditMode && this.recipeId) {
            // Update existing recipe
            const request = { id: this.recipeId, ...formValue };
            this.recipeService.updateRecipe(this.recipeId, request).pipe(
                finalize(() => this.isSubmitting = false)
            ).subscribe({
                next: () => {
                    this.notificationService.success('Recipe updated successfully!');
                    this.router.navigate(['/user/dashboard']);
                },
                error: (err) => {
                    this.notificationService.error('Failed to update recipe. Please try again.');
                    console.error(err);
                }
            });
        } else {
            // Create new recipe
            this.recipeService.createRecipe(formValue).pipe(
                finalize(() => this.isSubmitting = false)
            ).subscribe({
                next: () => {
                    this.notificationService.success('Recipe created successfully!');
                    this.router.navigate(['/user/dashboard']);
                },
                error: (err) => {
                    this.notificationService.error('Failed to create recipe. Please try again.');
                    console.error(err);
                }
            });
        }
    }
}