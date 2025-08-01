// File: nom-ui/src/app/recipe/components/recipe-edit/recipe-edit.component.ts

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormControl, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { RecipeService } from '../../services/recipe.service';
import { Observable, of, Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, startWith, finalize, takeUntil, take } from 'rxjs/operators';
import { CdkDragDrop, moveItemInArray, DragDropModule } from '@angular/cdk/drag-drop';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { IngredientSearchResponseModel } from '../../models/ingredient-search-response.model';
import { ReferenceItemModel } from '../../../common/models/reference-item.model'; // CORRECTED: Import from common/models
import { NotificationService } from '../../../utilities/services/notification.service';
import { IngredientCreateModalComponent, IngredientCreateModalData } from '../ingredient-create-modal/ingredient-create-modal.component';
import { IngredientModel } from '../../models/ingredient.model';
import { RecipeEditModel, RecipeIngredientModel, RecipeStepModel } from '../../models/recipe-edit.model';
import { CurationService } from '../../../curation/services/curation.service';

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
        MatSelectModule,
        MatDialogModule
    ],
    templateUrl: './recipe-edit.component.html',
    styleUrls: ['./recipe-edit.component.scss']
})
export class RecipeEditComponent implements OnInit, OnDestroy {
    recipeForm: FormGroup;
    isEditMode = false;
    recipeId: number | null = null;
    isLoading = true;
    pageTitle = 'Create Recipe';
    isSubmitting = false;

    ingredientSearchCtrl = new FormControl('');
    filteredIngredients$: Observable<IngredientSearchResponseModel[]>;
    measurementTypes$: Observable<ReferenceItemModel[]>;

    private destroy$ = new Subject<void>();

    constructor(
        private fb: FormBuilder,
        private route: ActivatedRoute,
        public router: Router,
        private recipeService: RecipeService,
        private notificationService: NotificationService,
        private dialog: MatDialog,
        private curationService: CurationService
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
        console.log('RecipeEditComponent ngOnInit called');
        this.route.paramMap.pipe(
            switchMap(params => {
                const id = params.get('id');
                console.log('Route param id:', id);
                if (id) {
                    this.isEditMode = true;
                    this.pageTitle = 'Edit Recipe';
                    this.recipeId = +id;
                    this.isLoading = true;
                    console.log('Loading recipe with ID:', +id);
                    return this.recipeService.getRecipe(+id);
                }
                this.isLoading = false;
                return of(null);
            }),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (recipeData) => {
                console.log('Recipe data received:', recipeData);
                if (this.isEditMode && recipeData) {
                    console.log('Populating form with recipe data');
                    // Populate the form with existing recipe data
                    this.recipeForm.patchValue({
                        name: recipeData.name,
                        description: recipeData.description
                    });

                    // Clear existing arrays and populate with recipe data
                    this.ingredients.clear();
                    recipeData.ingredients?.forEach(ingredient => {
                        console.log('Adding ingredient:', ingredient);
                        this.ingredients.push(this.fb.group({
                            ingredientId: [ingredient.ingredientId, Validators.required],
                            name: [ingredient.name],
                            quantity: [ingredient.quantity, [Validators.required, Validators.min(0.01)]],
                            measurementTypeId: [ingredient.measurementTypeId, Validators.required]
                        }));
                    });

                    this.steps.clear();
                    recipeData.steps?.forEach(step => {
                        console.log('Adding step:', step);
                        this.steps.push(this.fb.group({
                            instruction: [step.description, [Validators.required, Validators.maxLength(2047)]],
                            order: [step.order]
                        }));
                    });
                } else {
                    console.log('Not in edit mode or no recipe data');
                }
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading recipe:', error);
                this.isLoading = false;
            }
        });
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
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
        const ingredientGroup = this.createIngredientGroup(event.option.value);

        // Set a default measurement type immediately
        this.measurementTypes$.pipe(take(1)).subscribe(measurementTypes => {
            if (measurementTypes && measurementTypes.length > 0) {
                // Try to find a common measurement type like "each" or "piece"
                const defaultMeasurement = measurementTypes.find(mt =>
                    mt.name.toLowerCase().includes('each') ||
                    mt.name.toLowerCase().includes('piece') ||
                    mt.name.toLowerCase().includes('unit')
                ) || measurementTypes[0];

                ingredientGroup.patchValue({
                    measurementTypeId: defaultMeasurement.id
                });

                // Trigger form validation immediately after setting the value
                this.recipeForm.updateValueAndValidity();
            }
        });

        this.ingredients.push(ingredientGroup);
        this.ingredientSearchCtrl.setValue('');
        event.option.focus();
    }

    removeIngredient(index: number): void {
        this.ingredients.removeAt(index);
    }

    displayIngredient(ingredient: IngredientSearchResponseModel): string {
        return ingredient ? ingredient.name : '';
    }

    openCreateIngredientModal(): void {
        const searchValue = this.ingredientSearchCtrl.value;
        const ingredientName = typeof searchValue === 'string' ? searchValue : '';

        const dialogRef = this.dialog.open(IngredientCreateModalComponent, {
            width: '600px',
            maxHeight: '80vh',
            data: { ingredientName } as IngredientCreateModalData,
            disableClose: true
        });

        dialogRef.afterClosed().subscribe((result: any | undefined) => {
            if (result) {
                // Extract the ID properly - handle cases where it might be nested
                let ingredientId: number;

                console.log('Modal result:', result);
                if (typeof result.id === 'object' && result.id !== null) {
                    // If id is an object, try to get the actual ID from it
                    ingredientId = result.id.id || result.id.Id || result.id.ID;
                } else {
                    // If id is already a number, use it directly
                    ingredientId = result.id;
                }

                console.log('Modal result:', result);
                console.log('Extracted ingredient ID:', ingredientId);

                // Convert the created ingredient to the format expected by the form
                const ingredientSearchResponse: IngredientSearchResponseModel = {
                    id: ingredientId,
                    name: result.name,
                    fdcId: result.fdcId
                };

                // Add the newly created ingredient to the form
                const ingredientGroup = this.createIngredientGroup(ingredientSearchResponse);

                // Set a default measurement type immediately
                this.measurementTypes$.pipe(take(1)).subscribe(measurementTypes => {
                    if (measurementTypes && measurementTypes.length > 0) {
                        // Try to find a common measurement type like "each" or "piece"
                        const defaultMeasurement = measurementTypes.find(mt =>
                            mt.name.toLowerCase().includes('each') ||
                            mt.name.toLowerCase().includes('piece') ||
                            mt.name.toLowerCase().includes('unit')
                        ) || measurementTypes[0];

                        ingredientGroup.patchValue({
                            measurementTypeId: defaultMeasurement.id
                        });

                        // Trigger form validation immediately after setting the value
                        this.recipeForm.updateValueAndValidity();
                    }
                });

                this.ingredients.push(ingredientGroup);

                // Clear the search input
                this.ingredientSearchCtrl.setValue('');

                // Show success notification
                this.notificationService.success('Ingredient added successfully!');
            }
        });
    }

    get steps(): FormArray {
        return this.recipeForm.get('steps') as FormArray;
    }

    createStepGroup(): FormGroup {
        return this.fb.group({
            instruction: ['', [Validators.required, Validators.maxLength(2047)]],
            order: [0]
        });
    }

    addStep(): void {
        const stepGroup = this.createStepGroup();
        stepGroup.patchValue({ order: this.steps.length });
        this.steps.push(stepGroup);
    }

    removeStep(index: number): void {
        this.steps.removeAt(index);
    }

    dropStep(event: CdkDragDrop<string[]>) {
        moveItemInArray(this.steps.controls, event.previousIndex, event.currentIndex);
    }

    onSubmit(): void {
        if (this.recipeForm.invalid) {
            this.notificationService.error('Please fill in all required fields.');
            return;
        }

        this.isSubmitting = true;
        const formValue = this.recipeForm.value;

        const request = {
            name: formValue.name,
            description: formValue.description,
            authorId: 1, // TODO: Get from auth service
            ingredients: formValue.ingredients.map((ingredient: any) => ({
                ingredientId: ingredient.ingredientId,
                quantity: ingredient.quantity,
                measurementTypeId: ingredient.measurementTypeId
            })),
            steps: formValue.steps.map((step: any, index: number) => ({
                description: step.instruction,
                order: index
            }))
        };

        if (this.isEditMode && this.recipeId) {
            const updateRequest = { id: this.recipeId, ...request };
            this.recipeService.updateRecipe(this.recipeId, updateRequest).pipe(
                finalize(() => this.isSubmitting = false)
            ).subscribe({
                next: () => {
                    this.notificationService.success('Recipe updated successfully!');
                    this.router.navigate(['/recipes']);
                },
                error: (error) => {
                    console.error('Error updating recipe:', error);
                    this.notificationService.error('Failed to update recipe. Please try again.');
                }
            });
        } else {
            this.recipeService.createRecipe(request).pipe(
                finalize(() => this.isSubmitting = false)
            ).subscribe({
                next: (recipe) => {
                    this.notificationService.success('Recipe created successfully!');
                    this.router.navigate(['/recipes']);
                },
                error: (error) => {
                    console.error('Error creating recipe:', error);
                    this.notificationService.error('Failed to create recipe. Please try again.');
                }
            });
        }
    }

    submitForCuration(): void {
        if (!this.recipeId) {
            this.notificationService.error('Cannot submit for curation: Recipe not found.');
            return;
        }

        this.isSubmitting = true;
        this.curationService.submitForCuration({
            entityId: this.recipeId,
            entityType: 'Recipe'
        }).pipe(
            finalize(() => this.isSubmitting = false)
        ).subscribe({
            next: () => {
                this.notificationService.success('Recipe submitted for curation successfully!');
                this.router.navigate(['/user/dashboard']);
            },
            error: (error) => {
                console.error('Error submitting recipe for curation:', error);
                this.notificationService.error('Failed to submit recipe for curation. Please try again.');
            }
        });
    }
}