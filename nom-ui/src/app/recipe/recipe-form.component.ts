import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormArray, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of } from 'rxjs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { CdkDropList, CdkDrag, CdkDragHandle, CdkDragDrop } from '@angular/cdk/drag-drop';
import { RecipeService } from '../core/services/recipe.service';
import { IngredientService } from '../core/services/ingredient.service';
import { MeasurementService } from '../core/services/measurement.service';
import { LoadingService } from '../core/services/loading.service';
import { IngredientSearchResult } from '../core/models/ingredient-search-result.model';
import { MeasurementOption } from '../core/models/measurement.model';
import { ConfirmDeleteDialog, ConfirmDeleteDialogData } from '../shared/confirm-delete-dialog/confirm-delete-dialog.component';

@Component({
  selector: 'nom-recipe-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
  ],
  templateUrl: './recipe-form.component.html',
  styleUrl: './recipe-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecipeForm implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);
  private dialog = inject(MatDialog);
  private recipeService = inject(RecipeService);
  private ingredientService = inject(IngredientService);
  private measurementService = inject(MeasurementService);
  private loadingService = inject(LoadingService);

  // Mode
  isEditMode = signal(false);
  recipeId = signal<number | null>(null);

  // State
  loading = signal(true);
  saving = signal(false);
  errorMessage = signal('');

  // Image
  currentImageUrl = signal<string | null>(null);
  currentAssetId = signal<number | null>(null);
  uploadError = signal('');

  // Reference data
  measurements = signal<MeasurementOption[]>([]);

  // Ingredient autocomplete state per row
  ingredientOptions = signal<Map<number, IngredientSearchResult[]>>(new Map());
  private ingredientSearchSubjects = new Map<number, Subject<string>>();
  private rowCounter = 0;

  // Form
  recipeForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(511)]],
    description: ['', [Validators.maxLength(2047)]],
    ingredients: this.fb.array<FormGroup>([]),
    steps: this.fb.array<FormGroup>([]),
  });

  get ingredientsArray(): FormArray<FormGroup> {
    return this.recipeForm.get('ingredients') as FormArray<FormGroup>;
  }

  get stepsArray(): FormArray<FormGroup> {
    return this.recipeForm.get('steps') as FormArray<FormGroup>;
  }

  pageTitle = computed(() => this.isEditMode() ? 'Edit Recipe' : 'New Recipe');
  pageSubtitle = computed(() => this.isEditMode() ? 'Update your recipe details' : 'Create a new recipe');
  submitLabel = computed(() => this.isEditMode() ? 'Save Changes' : 'Create Recipe');

  ngOnInit(): void {
    this.measurementService.loadMeasurements().pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (data) => this.measurements.set(data),
    });

    this.route.params.pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(params => {
      const id = params['id'];
      if (id) {
        this.isEditMode.set(true);
        this.recipeId.set(Number(id));
        this.loadRecipe(Number(id));
      } else {
        this.isEditMode.set(false);
        this.addIngredient();
        this.addStep();
        this.loading.set(false);
      }
    });
  }

  // ── Ingredient rows ──

  addIngredient(): void {
    const rowId = this.rowCounter++;
    const group = this.fb.group({
      rowId: [rowId],
      ingredientId: [0, Validators.required],
      name: [''],
      searchText: [''],
      quantity: [null as number | null, [Validators.required, Validators.min(0.01)]],
      measurementId: [0, Validators.required],
      notes: [''],
    });
    this.ingredientsArray.push(group);
    this.setupIngredientSearch(rowId);
  }

  removeIngredient(index: number): void {
    const group = this.ingredientsArray.at(index);
    const rowId = group.get('rowId')?.value;
    this.ingredientsArray.removeAt(index);
    this.ingredientSearchSubjects.get(rowId)?.complete();
    this.ingredientSearchSubjects.delete(rowId);
    this.ingredientOptions.update(m => { const next = new Map(m); next.delete(rowId); return next; });
  }

  onIngredientInput(index: number, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    const group = this.ingredientsArray.at(index);
    const rowId = group.get('rowId')?.value;
    this.ingredientSearchSubjects.get(rowId)?.next(value);
  }

  onIngredientSelected(index: number, item: IngredientSearchResult): void {
    const group = this.ingredientsArray.at(index);
    group.patchValue({ ingredientId: item.id, name: item.name, searchText: item.name });
  }

  displayIngredient(item: IngredientSearchResult): string {
    return item?.name ?? '';
  }

  getIngredientOptions(index: number): IngredientSearchResult[] {
    const group = this.ingredientsArray.at(index);
    const rowId = group.get('rowId')?.value;
    return this.ingredientOptions().get(rowId) ?? [];
  }

  private setupIngredientSearch(rowId: number): void {
    const subject = new Subject<string>();
    this.ingredientSearchSubjects.set(rowId, subject);

    subject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(query => query.length >= 2 ? this.ingredientService.searchIngredients(query) : of([])),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(results => {
      this.ingredientOptions.update(m => new Map(m).set(rowId, results));
    });
  }

  // ── Step rows ──

  addStep(): void {
    const group = this.fb.group({
      description: ['', Validators.required],
      order: [this.stepsArray.length + 1],
    });
    this.stepsArray.push(group);
  }

  removeStep(index: number): void {
    this.stepsArray.removeAt(index);
    this.recalculateStepOrder();
  }

  onStepDrop(event: CdkDragDrop<FormGroup[]>): void {
    const control = this.stepsArray.at(event.previousIndex);
    this.stepsArray.removeAt(event.previousIndex);
    this.stepsArray.insert(event.currentIndex, control);
    this.recalculateStepOrder();
  }

  private recalculateStepOrder(): void {
    for (let i = 0; i < this.stepsArray.length; i++) {
      this.stepsArray.at(i).get('order')?.setValue(i + 1);
    }
  }

  // ── Submit ──

  onSubmit(): void {
    if (this.recipeForm.invalid || this.saving()) return;
    this.saving.set(true);
    this.errorMessage.set('');

    const formVal = this.recipeForm.getRawValue();
    const ingredients = formVal.ingredients.map(ing => ({
      ingredientId: ing['ingredientId'] as number,
      name: ing['name'] as string,
      quantity: ing['quantity'] as number,
      measurementId: ing['measurementId'] as number,
    }));
    const steps = formVal.steps.map(s => ({
      description: s['description'] as string,
      order: s['order'] as number,
    }));

    if (this.isEditMode()) {
      const id = this.recipeId()!;
      this.recipeService.updateRecipe(id, {
        id,
        name: formVal.name!,
        description: formVal.description ?? undefined,
        ingredients,
        steps,
      }).pipe(
        this.loadingService.loading('Saving recipe...'),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe({
        next: () => {
          this.saving.set(false);
          this.router.navigate(['/recipe', id]);
        },
        error: () => {
          this.saving.set(false);
          this.errorMessage.set('Failed to save recipe. Please try again.');
        },
      });
    } else {
      this.recipeService.createRecipe({
        name: formVal.name!,
        description: formVal.description ?? '',
        ingredients,
        steps,
      }).pipe(
        this.loadingService.loading('Creating recipe...'),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe({
        next: (res) => {
          this.saving.set(false);
          this.router.navigate(['/recipe', res.id]);
        },
        error: () => {
          this.saving.set(false);
          this.errorMessage.set('Failed to create recipe. Please try again.');
        },
      });
    }
  }

  // ── Delete ──

  onDelete(): void {
    const id = this.recipeId();
    if (!id) return;

    this.dialog.open(ConfirmDeleteDialog, {
      data: {
        title: 'Delete Recipe',
        message: 'Are you sure you want to delete this recipe? This cannot be undone.',
        confirmText: 'Delete Recipe',
      } as ConfirmDeleteDialogData,
    }).afterClosed().subscribe(confirmed => {
      if (!confirmed) return;
      this.recipeService.deleteRecipe(id).pipe(
        this.loadingService.loading('Deleting recipe...'),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe({
        next: () => this.router.navigate(['/recipes/mine']),
        error: () => this.errorMessage.set('Failed to delete recipe. Please try again.'),
      });
    });
  }

  // ── Image ──

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    // Validate size (10MB max)
    if (file.size > 10 * 1024 * 1024) {
      this.uploadError.set('Image must be under 10MB.');
      return;
    }

    this.uploadError.set('');
    const id = this.recipeId();
    if (!id) return;

    this.recipeService.uploadImage(id, file).pipe(
      this.loadingService.loading('Uploading image...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (result) => {
        this.currentImageUrl.set(`/api/recipe/${id}/image?t=${Date.now()}`);
        this.currentAssetId.set(result.id);
      },
      error: () => this.uploadError.set('Failed to upload image.'),
    });

    // Reset the input so the same file can be re-selected
    input.value = '';
  }

  onRemoveImage(): void {
    const id = this.recipeId();
    const assetId = this.currentAssetId();
    if (!id || !assetId) return;

    this.recipeService.deleteImage(id, assetId).pipe(
      this.loadingService.loading('Removing image...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.currentImageUrl.set(null);
        this.currentAssetId.set(null);
      },
      error: () => this.uploadError.set('Failed to remove image.'),
    });
  }

  // ── Load existing recipe ──

  private loadRecipe(id: number): void {
    this.recipeService.getRecipe(id).pipe(
      this.loadingService.loading('Loading recipe...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (recipe) => {
        this.recipeForm.patchValue({
          name: recipe.name,
          description: recipe.description,
        });

        // Populate ingredients
        if (recipe.ingredients) {
          for (const ing of recipe.ingredients) {
            const rowId = this.rowCounter++;
            const group = this.fb.group({
              rowId: [rowId],
              ingredientId: [ing.ingredientId, Validators.required],
              name: [ing.name],
              searchText: [ing.name],
              quantity: [ing.quantity, [Validators.required, Validators.min(0.01)]],
              measurementId: [ing.measurementId, Validators.required],
              notes: [ing.notes ?? ''],
            });
            this.ingredientsArray.push(group);
            this.setupIngredientSearch(rowId);
          }
        }
        if (this.ingredientsArray.length === 0) this.addIngredient();

        // Populate steps
        if (recipe.steps) {
          const sorted = [...recipe.steps].sort((a, b) => a.order - b.order);
          for (const step of sorted) {
            const group = this.fb.group({
              description: [step.description, Validators.required],
              order: [step.order],
            });
            this.stepsArray.push(group);
          }
        }
        if (this.stepsArray.length === 0) this.addStep();

        if (recipe.imageUrl) {
          this.currentImageUrl.set(recipe.imageUrl);
          // Load assets to get the current asset ID for deletion
          this.recipeService.getAssets(id).pipe(
            takeUntilDestroyed(this.destroyRef),
          ).subscribe({
            next: (assets) => {
              if (assets.length > 0) {
                this.currentAssetId.set(assets[0].id);
              }
            },
          });
        }

        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load recipe.');
        this.loading.set(false);
      },
    });
  }
}
