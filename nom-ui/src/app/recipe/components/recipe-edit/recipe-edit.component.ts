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
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { IngredientSearchResponseModel } from '../../models/ingredient-search-response.model';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { IngredientCreateModalComponent, IngredientCreateModalData } from '../ingredient-create-modal/ingredient-create-modal.component';
import { IngredientModel } from '../../models/ingredient.model';
import { RecipeEditModel, RecipeIngredientModel, RecipeStepModel } from '../../models/recipe-edit.model';
import { CurationService } from '../../../curation/services/curation.service';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

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
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        MatDialogModule,
        BaseFormComponent,
        BasePageComponent
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
    error: string | null = null;

    ingredientSearchCtrl = new FormControl('');
    filteredIngredients$: Observable<IngredientSearchResponseModel[]>;
    measurementTypes$: Observable<ReferenceItemModel[]>;

    pageConfig: BasePageConfig = {
        title: 'Create Recipe',
        subtitle: 'Add a new recipe to your collection',
        showBackButton: true,
        maxWidth: '800px'
    };

    formConfig: BaseFormConfig = {
        title: '',
        subtitle: '',
        submitText: 'Create Recipe',
        showCancelButton: true,
        cancelText: 'Cancel',
        maxWidth: '100%'
    };

    private destroy$ = new Subject<void>();

    constructor(
        private fb: FormBuilder,
        private route: ActivatedRoute,
        public router: Router,
        private recipeService: RecipeService,
        private notificationService: NotificationService,
        private dialog: MatDialog,
        private curationService: CurationService,
        private userInfoService: UserInfoService
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
        this.route.params.pipe(takeUntil(this.destroy$)).subscribe(params => {
            const id = params['id'];
            if (id) {
                this.recipeId = +id;
                this.isEditMode = true;
                this.pageTitle = 'Edit Recipe';
                this.pageConfig.title = 'Edit Recipe';
                this.pageConfig.subtitle = 'Update your recipe';
                this.formConfig.submitText = 'Update Recipe';
                this.loadRecipe();
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
            ingredientId: [ingredient.id],
            name: [ingredient.name],
            quantity: [1, [Validators.required, Validators.min(0.01)]],
            measurementTypeId: [1, [Validators.required]]
        });
    }

    onIngredientSelected(event: MatAutocompleteSelectedEvent): void {
        const ingredient = event.option.value as IngredientSearchResponseModel;

        // Check if ingredient is already added
        const existingIndex = this.ingredients.controls.findIndex(
            control => control.get('ingredientId')?.value === ingredient.id
        );

        if (existingIndex >= 0) {
            // Update quantity if already exists
            const existingControl = this.ingredients.at(existingIndex);
            const currentQuantity = existingControl.get('quantity')?.value || 0;
            existingControl.patchValue({ quantity: currentQuantity + 1 });
            this.notificationService.showInfo(`Increased quantity of ${ingredient.name}`);
        } else {
            // Add new ingredient
            this.ingredients.push(this.createIngredientGroup(ingredient));
        }

        this.ingredientSearchCtrl.setValue('');
    }

    removeIngredient(index: number): void {
        this.ingredients.removeAt(index);
    }

    displayIngredient(ingredient: IngredientSearchResponseModel): string {
        return ingredient ? ingredient.name : '';
    }

    openCreateIngredientModal(): void {
        const dialogRef = this.dialog.open<IngredientCreateModalComponent, IngredientCreateModalData, IngredientModel>(
            IngredientCreateModalComponent,
            {
                width: '500px',
                data: { recipeId: this.recipeId }
            }
        );

        dialogRef.afterClosed().pipe(take(1)).subscribe(result => {
            if (result) {
                // Add the newly created ingredient to the form
                const newIngredient: IngredientSearchResponseModel = {
                    id: result.id,
                    name: result.name,
                    description: result.description || '',
                    nutritionPer100g: result.nutritionPer100g
                };
                this.ingredients.push(this.createIngredientGroup(newIngredient));
                this.notificationService.showSuccess(`Ingredient "${result.name}" created and added to recipe`);
            }
        });
    }

    private loadRecipeData(recipe: any): void {
        this.recipeForm.patchValue({
            name: recipe.name,
            description: recipe.description
        });

        // Load ingredients
        if (recipe.ingredients && recipe.ingredients.length > 0) {
            recipe.ingredients.forEach((ingredient: any) => {
                const ingredientGroup = this.fb.group({
                    ingredientId: [ingredient.ingredientId],
                    name: [ingredient.name],
                    quantity: [ingredient.quantity, [Validators.required, Validators.min(0.01)]],
                    measurementTypeId: [ingredient.measurementTypeId, [Validators.required]]
                });
                this.ingredients.push(ingredientGroup);
            });
        }

        // Load steps
        if (recipe.steps && recipe.steps.length > 0) {
            recipe.steps.forEach((step: any) => {
                const stepGroup = this.fb.group({
                    instruction: [step.instruction, [Validators.required]],
                    stepNumber: [step.stepNumber]
                });
                this.steps.push(stepGroup);
            });
        }
    }

    get steps(): FormArray {
        return this.recipeForm.get('steps') as FormArray;
    }

    createStepGroup(): FormGroup {
        return this.fb.group({
            instruction: ['', [Validators.required]],
            stepNumber: [0]
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
        if (this.recipeForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        this.error = null;

        const formValue = this.recipeForm.value;
        const recipeData: RecipeEditModel = {
            name: formValue.name,
            description: formValue.description,
            ingredients: formValue.ingredients.map((ingredient: RecipeIngredientModel, index: number) => ({
                ...ingredient,
                stepNumber: index + 1
            })),
            steps: formValue.steps.map((step: RecipeStepModel, index: number) => ({
                ...step,
                stepNumber: index + 1
            }))
        };

        const request$ = this.isEditMode && this.recipeId
            ? this.recipeService.updateRecipe(this.recipeId, recipeData)
            : this.recipeService.createRecipe(recipeData);

        request$.pipe(
            finalize(() => this.isSubmitting = false),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (recipe) => {
                const action = this.isEditMode ? 'updated' : 'created';
                this.notificationService.showSuccess(`Recipe ${action} successfully`);
                this.router.navigate(['/recipe', recipe.id]);
            },
            error: (error) => {
                console.error('Error saving recipe:', error);
                this.error = `Failed to ${this.isEditMode ? 'update' : 'create'} recipe. Please try again.`;
            }
        });
    }

    onCancel(): void {
        this.router.navigate(['/recipe']);
    }

    submitForCuration(): void {
        if (this.recipeForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        this.error = null;

        const formValue = this.recipeForm.value;
        const recipeData: RecipeEditModel = {
            name: formValue.name,
            description: formValue.description,
            ingredients: formValue.ingredients.map((ingredient: RecipeIngredientModel, index: number) => ({
                ...ingredient,
                stepNumber: index + 1
            })),
            steps: formValue.steps.map((step: RecipeStepModel, index: number) => ({
                ...step,
                stepNumber: index + 1
            }))
        };

        this.recipeService.createRecipe(recipeData).pipe(
            finalize(() => this.isSubmitting = false),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (recipe) => {
                this.notificationService.showSuccess('Recipe created and submitted for curation');
                this.router.navigate(['/recipe', recipe.id]);
            },
            error: (error) => {
                console.error('Error submitting recipe for curation:', error);
                this.error = 'Failed to submit recipe for curation. Please try again.';
            }
        });
    }

    onBack(): void {
        this.router.navigate(['/recipe']);
    }

    onRefresh(): void {
        if (this.recipeId) {
            this.loadRecipe();
        }
    }

    onRetry(): void {
        this.error = null;
        if (this.recipeId) {
            this.loadRecipe();
        }
    }

    private loadRecipe(): void {
        if (!this.recipeId) return;

        this.isLoading = true;
        this.error = null;

        this.recipeService.getRecipe(this.recipeId).pipe(
            finalize(() => this.isLoading = false),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (recipe) => {
                this.loadRecipeData(recipe);
            },
            error: (error) => {
                console.error('Error loading recipe:', error);
                this.error = 'Failed to load recipe. Please try again.';
            }
        });
    }
}