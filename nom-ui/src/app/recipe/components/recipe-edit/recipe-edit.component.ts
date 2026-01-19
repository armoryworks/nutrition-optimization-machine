// File: nom-ui/src/app/recipe/components/recipe-edit/recipe-edit.component.ts

import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { FormArray, NonNullableFormBuilder, FormControl, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Location } from '@angular/common';
import { RecipeService } from '../../services/recipe.service';
import { of, Subject } from 'rxjs';
import { finalize, takeUntil, take, catchError } from 'rxjs/operators';
import { CdkDragDrop, moveItemInArray, DragDropModule } from '@angular/cdk/drag-drop';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwSelectComponent, AmwAutocompleteComponent, AmwCardComponent, AmwIconComponent } from 'angular-material-wrap';

import { IngredientSearchResponseModel } from '../../models/ingredient-search-response.model';
import { RecipeModel } from '../../models/recipe.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { IngredientCreateModalComponent, IngredientCreateModalData } from '../ingredient-create-modal/ingredient-create-modal.component';
import { IngredientModel } from '../../models/ingredient.model';
import { RecipeEditModel } from '../../models/recipe-edit.model';
import { RecipeStepModel } from '../../models/recipe-step.model';
import { CurationService } from '../../../curation/services/curation.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';

interface AutocompleteOption {
    value: any;
    label: string;
    disabled?: boolean;
}

@Component({
    selector: 'nom-recipe-edit',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        RouterModule,
        DragDropModule,
        MatProgressBarModule,
        MatDialogModule,
        AmwInputComponent,
        AmwTextareaComponent,
        AmwButtonComponent,
        AmwSelectComponent,
        AmwAutocompleteComponent,
        AmwCardComponent,
        AmwIconComponent
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
    private referenceDataService = inject(ReferenceDataService);

    recipeForm: FormGroup;
    isEditMode = signal(false);
    recipeId = signal<number | null>(null);
    isLoading = signal(true);
    pageTitle = signal('Create Recipe');
    isSubmitting = signal(false);
    error = signal<string | null>(null);

    ingredientSearchCtrl = new FormControl<IngredientSearchResponseModel | null>(null);
    ingredientAutocompleteOptions = signal<AutocompleteOption[]>([]);
    private ingredientCache = new Map<number, IngredientSearchResponseModel>();
    measurements$: any;
    private measurementsCache: any[] = [];

    private destroy$ = new Subject<void>();

    // Back navigation properties
    private referringPage = signal('/recipes');
    private referringPageTitle = signal('Recipes');

    constructor() {
        this.recipeForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.maxLength(511)]],
            description: ['', [Validators.maxLength(2047)]],
            ingredients: this.nonNullableFb.array([]),
            steps: this.nonNullableFb.array([])
        });

        // Load measurements from the reference data service
        this.measurements$ = this.referenceDataService.getMeasurementTypes();
        this.measurements$.pipe(takeUntil(this.destroy$)).subscribe((measurements: any) => {
            this.measurementsCache = measurements;
        });
    }

    // Helper method for AMW select options
    getMeasurementOptions(): { value: number; label: string }[] {
        return this.measurementsCache.map(m => ({
            value: m.id,
            label: m.name
        }));
    }

    ngOnInit(): void {
        // Capture referring page information immediately at the start
        this.captureReferringPage();

        this.route.params.pipe(takeUntil(this.destroy$)).subscribe(params => {
            const id = params['id'];
            if (id) {
                this.recipeId.set(+id);
                this.isEditMode.set(true);
                this.pageTitle.set('Edit Recipe');
                this.loadRecipe();
            } else {
                // For create mode, set loading to false since no data needs to be loaded
                this.isLoading.set(false);
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

    onIngredientInputChanged(term: string): void {
        if (term && term.length > 1) {
            this.recipeService.searchIngredients(term)
                .pipe(
                    takeUntil(this.destroy$),
                    catchError(() => of([]))
                )
                .subscribe((ingredients) => {
                    this.ingredientCache.clear();
                    const options = ingredients.map((ingredient) => {
                        this.ingredientCache.set(ingredient.id, ingredient);
                        return {
                            value: ingredient.id,
                            label: ingredient.name
                        };
                    });
                    this.ingredientAutocompleteOptions.set(options);
                });
        } else {
            this.ingredientAutocompleteOptions.set([]);
        }
    }

    onIngredientAutocompleteSelected(option: AutocompleteOption): void {
        const ingredient = this.ingredientCache.get(option.value);
        if (!ingredient) return;

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

        this.ingredientSearchCtrl.setValue(null);
        this.ingredientAutocompleteOptions.set([]);
    }

    removeIngredient(index: number): void {
        this.ingredients.removeAt(index);
    }

    displayIngredient = (value: any): string => {
        if (value && typeof value === 'object' && value.name) {
            return value.name;
        }
        return value?.label || '';
    };

    openCreateIngredientModal(): void {
        const dialogRef = this.dialog.open<IngredientCreateModalComponent, IngredientCreateModalData, IngredientModel>(
            IngredientCreateModalComponent,
            {
                width: '500px',
                data: { recipeId: this.recipeId() || undefined }
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
        if (this.recipeForm.invalid || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);

        const formValue = this.recipeForm.value;
        const recipeData: RecipeEditModel = {
            id: this.recipeId() || 0,
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

        const request$ = this.isEditMode() && this.recipeId()
            ? this.recipeService.updateRecipe(this.recipeId()!, {
                id: this.recipeId()!,
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
            finalize(() => this.isSubmitting.set(false)),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (recipe) => {
                const action = this.isEditMode() ? 'updated' : 'created';
                this.notificationService.success(`Recipe ${action} successfully`);
                this.router.navigate(['/recipes', recipe.id]);
            },
            error: (error) => {
                console.error('Error saving recipe:', error);
                this.error.set(`Failed to ${this.isEditMode() ? 'update' : 'create'} recipe. Please try again.`);
            }
        });
    }

    onCancel(): void {
        this.navigateBack();
    }

    submitForCuration(): void {
        if (this.recipeForm.invalid || this.isSubmitting()) {
            return;
        }

        if (!this.recipeId()) {
            this.error.set('Cannot submit for curation: Recipe not found');
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);

        const request = {
            entityId: this.recipeId()!,
            entityType: 'Recipe' as const
        };

        this.curationService.submitForCuration(request).pipe(
            finalize(() => this.isSubmitting.set(false)),
            takeUntil(this.destroy$)
        ).subscribe({
            next: () => {
                this.notificationService.success('Recipe submitted for curation successfully');
                this.router.navigate(['/recipes', this.recipeId()]);
            },
            error: (error) => {
                console.error('Error submitting recipe for curation:', error);
                this.error.set('Failed to submit recipe for curation. Please try again.');
            }
        });
    }

    onBack(): void {
        this.navigateBack();
    }

    onRefresh(): void {
        if (this.recipeId()) {
            this.loadRecipe();
        }
    }

    onRetry(): void {
        this.error.set(null);
        if (this.recipeId()) {
            this.loadRecipe();
        }
    }

    private loadRecipe(): void {
        if (!this.recipeId()) return;

        this.isLoading.set(true);
        this.error.set(null);

        this.recipeService.getRecipe(this.recipeId()!).pipe(
            finalize(() => this.isLoading.set(false)),
            takeUntil(this.destroy$)
        ).subscribe({
            next: (recipe) => {
                this.loadRecipeData(recipe);
            },
            error: (error) => {
                console.error('Error loading recipe:', error);
                this.error.set('Failed to load recipe. Please try again.');
            }
        });
    }

    private captureReferringPage(): void {
        // Try to get referring page from query parameters first
        const returnTo = this.route.snapshot.queryParams['returnTo'];
        const returnToTitle = this.route.snapshot.queryParams['returnToTitle'];

        console.log('CaptureReferringPage - Query params:', { returnTo, returnToTitle });
        console.log('CaptureReferringPage - Before update - referringPageTitle:', this.referringPageTitle());

        if (returnTo) {
            this.referringPage.set(returnTo);
            this.referringPageTitle.set(returnToTitle || 'Previous Page');
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
                    this.referringPage.set(url.pathname);
                    this.referringPageTitle.set(this.getPageTitleFromPath(url.pathname));
                    console.log('CaptureReferringPage - Using referrer:', { referringPage: this.referringPage(), referringPageTitle: this.referringPageTitle() });
                }
            }
        }

        console.log('Final referring page:', this.referringPage(), 'Title:', this.referringPageTitle());
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
        console.log('navigateBack called - referringPage:', this.referringPage());
        console.log('navigateBack called - current pathname:', window.location.pathname);

        // Try to navigate back to referring page, fallback to browser back
        if (this.referringPage() && this.referringPage() !== window.location.pathname) {
            console.log('navigateBack - navigating to:', this.referringPage());
            this.router.navigate([this.referringPage()]);
        } else {
            console.log('navigateBack - using browser back navigation');
            // Fallback to browser back navigation
            this.location.back();
        }
    }
}