// File: nom-ui/src/app/recipe/components/recipe-edit/recipe-edit.component.ts

import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, NonNullableFormBuilder, FormControl, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Location } from '@angular/common';
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
import { RecipeModel } from '../../models/recipe.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { IngredientCreateModalComponent, IngredientCreateModalData } from '../ingredient-create-modal/ingredient-create-modal.component';
import { IngredientModel } from '../../models/ingredient.model';
import { RecipeEditModel } from '../../models/recipe-edit.model';
import { RecipeIngredientModel } from '../../models/recipe-ingredient.model';
import { RecipeStepModel } from '../../models/recipe-step.model';
import { CurationService } from '../../../curation/services/curation.service';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

@Component({
    selector: 'nom-recipe-edit',
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
    private nonNullableFb = inject(NonNullableFormBuilder);
    private route = inject(ActivatedRoute);
    router = inject(Router);
    private location = inject(Location);
    private recipeService = inject(RecipeService);
    private notificationService = inject(NotificationService);
    private dialog = inject(MatDialog);
    private curationService = inject(CurationService);
    private userInfoService = inject(UserInfoService);
    private referenceDataService = inject(ReferenceDataService);

    recipeForm: FormGroup;
    isEditMode = false;
    recipeId: number | null = null;
    isLoading = true;
    pageTitle = 'Create Recipe';
    isSubmitting = false;
    error: string | null = null;

    ingredientSearchCtrl = new FormControl('');
    filteredIngredients$: Observable<IngredientSearchResponseModel[]>;
    measurements$: Observable<any[]>;

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

    // Back navigation properties
    private referringPage = '/recipes';
    private referringPageTitle = 'Recipes';

    constructor() {
        this.recipeForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.maxLength(511)]],
            description: ['', [Validators.maxLength(2047)]],
            ingredients: this.nonNullableFb.array([]),
            steps: this.nonNullableFb.array([])
        });

        this.filteredIngredients$ = this.ingredientSearchCtrl.valueChanges.pipe(
            startWith(''),
            debounceTime(300),
            distinctUntilChanged(),
            switchMap(value => (value && typeof value === 'string' && value.length > 1) ? this.recipeService.searchIngredients(value) : of([]))
        );

        // Load measurements from the reference data service
        this.measurements$ = this.referenceDataService.getMeasurementTypes();
    }

    ngOnInit(): void {
        // Capture referring page information immediately at the start
        this.captureReferringPage();

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
            } else {
                // For create mode, set loading to false since no data needs to be loaded
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
        return this.nonNullableFb.group({
            IngredientId: [ingredient.id],
            name: [ingredient.name],
            quantity: [1, [Validators.required, Validators.min(0.01)]],
            measurementId: [1, [Validators.required]]
        });
    }

    onIngredientSelected(event: MatAutocompleteSelectedEvent): void {
        const ingredient = event.option.value as IngredientSearchResponseModel;

        // Check if ingredient is already added
        const existingIndex = this.ingredients.controls.findIndex(
            control => control.get('IngredientId')?.value === ingredient.id
        );

        if (existingIndex >= 0) {
            // Update quantity if already exists
            const existingControl = this.ingredients.at(existingIndex);
            const currentQuantity = existingControl.get('quantity')?.value || 0;
            existingControl.patchValue({ quantity: currentQuantity + 1 });
            this.notificationService.info(`Increased quantity of ${ingredient.name}`);
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
                data: { recipeId: this.recipeId || undefined }
            }
        );

        dialogRef.afterClosed().pipe(take(1)).subscribe(result => {
            if (result) {
                // Add the newly created ingredient to the form
                const newIngredient: IngredientSearchResponseModel = {
                    id: result.id,
                    name: result.name,
                    fdcId: result.fdcId,
                    matchedAlias: result.name
                };
                this.ingredients.push(this.createIngredientGroup(newIngredient));
                this.notificationService.success(`Ingredient "${result.name}" created and added to recipe`);
            }
        });
    }

    private loadRecipeData(recipe: RecipeModel): void {
        this.recipeForm.patchValue({
            name: recipe.name,
            description: recipe.description
        });

        // Load ingredients
        if (recipe.ingredients && recipe.ingredients.length > 0) {
            recipe.ingredients.forEach((ingredient: any) => {
                const ingredientGroup = this.nonNullableFb.group({
                    IngredientId: [ingredient.IngredientId || ingredient.id],
                    name: [ingredient.name],
                    quantity: [ingredient.quantity || 1, [Validators.required, Validators.min(0.01)]],
                    measurementId: [ingredient.measurementId || 1, [Validators.required]]
                });
                this.ingredients.push(ingredientGroup);
            });
        }

        // Load steps
        if (recipe.steps && recipe.steps.length > 0) {
            recipe.steps.forEach((step: RecipeStepModel, index: number) => {
                const stepGroup = this.nonNullableFb.group({
                    description: [step.description, [Validators.required]],
                    order: [index + 1] // Use array index + 1 as step order
                });
                this.steps.push(stepGroup);
            });
        }
    }

    get steps(): FormArray {
        return this.recipeForm.get('steps') as FormArray;
    }

    createStepGroup(): FormGroup {
        return this.nonNullableFb.group({
            description: ['', [Validators.required]],
            order: [this.steps.length + 1]
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
            id: this.recipeId || 0,
            name: formValue.name,
            description: formValue.description || 'No description provided',
            ingredients: formValue.ingredients.map((ingredient: any, index: number) => ({
                IngredientId: Number(ingredient.IngredientId),
                quantity: Number(ingredient.quantity),
                measurementId: Number(ingredient.measurementId),
                name: ingredient.name,
                stepNumber: index + 1
            })),
            steps: formValue.steps.map((step: any, index: number) => ({
                description: step.description,
                order: index + 1,
                stepNumber: index + 1
            }))
        };

        const request$ = this.isEditMode && this.recipeId
            ? this.recipeService.updateRecipe(this.recipeId, {
                id: this.recipeId,
                name: recipeData.name,
                description: recipeData.description || 'No description provided',
                ingredients: recipeData.ingredients,
                steps: recipeData.steps
            })
            : this.recipeService.createRecipe({
                name: recipeData.name,
                description: recipeData.description || 'No description provided',
                steps: recipeData.steps,
                ingredients: recipeData.ingredients
            });

        request$.pipe(
            finalize(() => this.isSubmitting = false),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (recipe) => {
                const action = this.isEditMode ? 'updated' : 'created';
                this.notificationService.success(`Recipe ${action} successfully`);
                this.router.navigate(['/recipes', recipe.id]);
            },
            error: (error) => {
                console.error('Error saving recipe:', error);
                this.error = `Failed to ${this.isEditMode ? 'update' : 'create'} recipe. Please try again.`;
            }
        });
    }

    onCancel(): void {
        this.navigateBack();
    }

    submitForCuration(): void {
        if (this.recipeForm.invalid || this.isSubmitting) {
            return;
        }

        if (!this.recipeId) {
            this.error = 'Cannot submit for curation: Recipe not found';
            return;
        }

        this.isSubmitting = true;
        this.error = null;

        const request = {
            entityId: this.recipeId,
            entityType: 'Recipe' as const
        };

        this.curationService.submitForCuration(request).pipe(
            finalize(() => this.isSubmitting = false),
            takeUntil(this.destroy$)
        ).subscribe({
            next: () => {
                this.notificationService.success('Recipe submitted for curation successfully');
                this.router.navigate(['/recipes', this.recipeId]);
            },
            error: (error) => {
                console.error('Error submitting recipe for curation:', error);
                this.error = 'Failed to submit recipe for curation. Please try again.';
            }
        });
    }

    onBack(): void {
        this.navigateBack();
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

    private captureReferringPage(): void {
        // Try to get referring page from query parameters first
        const returnTo = this.route.snapshot.queryParams['returnTo'];
        const returnToTitle = this.route.snapshot.queryParams['returnToTitle'];

        console.log('CaptureReferringPage - Query params:', { returnTo, returnToTitle });
        console.log('CaptureReferringPage - Before update - referringPageTitle:', this.referringPageTitle);

        if (returnTo) {
            this.referringPage = returnTo;
            this.referringPageTitle = returnToTitle || 'Previous Page';
            console.log('CaptureReferringPage - Using query params:', { returnTo, returnToTitle });
        } else {
            // Fallback: try to get from browser history or default to recipes
            const referrer = document.referrer;
            console.log('CaptureReferringPage - No query params, checking referrer:', referrer);
            if (referrer && referrer.includes(window.location.origin)) {
                // Extract path from referrer URL
                const url = new URL(referrer);
                console.log('CaptureReferringPage - Referrer URL:', url.pathname);
                if (url.pathname !== window.location.pathname) {
                    this.referringPage = url.pathname;
                    this.referringPageTitle = this.getPageTitleFromPath(url.pathname);
                    console.log('CaptureReferringPage - Using referrer:', { referringPage: this.referringPage, referringPageTitle: this.referringPageTitle });
                }
            }
        }

        console.log('Final referring page:', this.referringPage, 'Title:', this.referringPageTitle);
    }

    private getPageTitleFromPath(path: string): string {
        // Map common paths to readable titles
        const pathTitles: Record<string, string> = {
            '/user/dashboard': 'Dashboard',
            '/recipes': 'Recipes',
            '/recipes/ingredients': 'Ingredients',
            '/user/recipe-author-dashboard': 'Recipe Author Dashboard',
            '/': 'Home'
        };

        return pathTitles[path] || 'Previous Page';
    }

    private navigateBack(): void {
        console.log('navigateBack called - referringPage:', this.referringPage);
        console.log('navigateBack called - current pathname:', window.location.pathname);

        // Try to navigate back to referring page, fallback to browser back
        if (this.referringPage && this.referringPage !== window.location.pathname) {
            console.log('navigateBack - navigating to:', this.referringPage);
            this.router.navigate([this.referringPage]);
        } else {
            console.log('navigateBack - using browser back navigation');
            // Fallback to browser back navigation
            this.location.back();
        }
    }
}