import { Component, inject, signal, computed, effect, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormControl, FormRecord, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { IngredientService } from '../core/services/ingredient.service';
import { LoadingService } from '../core/services/loading.service';
import { NutrientService } from '../core/services/nutrient.service';
import { NutrientModel } from '../core/models/nutrient.model';
import { NutrientValueInput } from '../core/models/ingredient-nutrient.model';

/** Nutrition-facts-label order; everything else lives under "More nutrients". */
const LABEL_NUTRIENT_IDS = [5035, 5000, 5001, 5002, 5004, 5003, 5005, 5007, 5006];

@Component({
  selector: 'nom-ingredient-form',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './ingredient-form.component.html',
  styleUrl: './ingredient-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IngredientForm {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private ingredientService = inject(IngredientService);
  private loadingService = inject(LoadingService);
  private nutrientService = inject(NutrientService);
  private destroyRef = inject(DestroyRef);

  private routeParams = toSignal(this.route.params);

  isEditMode = signal(false);
  ingredientId = signal<number | null>(null);
  loading = signal(false);
  saving = signal(false);
  errorMessage = signal('');
  aliases = signal<{ id: number; name: string }[]>([]);

  // ── Nutrition (per 100 g) ──
  /** All nutrient definitions from the API. */
  allNutrients = signal<NutrientModel[]>([]);
  /** The nutrition-facts-label nutrients, in label order. */
  labelNutrients = computed(() => {
    const byId = new Map(this.allNutrients().map((n) => [n.id, n]));
    return LABEL_NUTRIENT_IDS.map((id) => byId.get(id)).filter((n): n is NutrientModel => !!n);
  });
  /** Everything else (vitamins, minerals…), alphabetical from the API. */
  otherNutrients = computed(() => this.allNutrients().filter((n) => !LABEL_NUTRIENT_IDS.includes(n.id)));
  showMoreNutrients = signal(false);
  /** One optional numeric control per nutrient id; blank = not entered. */
  nutritionForm: FormRecord<FormControl<number | null>> = this.fb.record<FormControl<number | null>>({});
  /** True once the user changed any nutrition value — only then does Save send nutrients. */
  private nutritionDirty = false;

  ingredientForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    pluralName: ['', [Validators.maxLength(255)]],
    description: ['', [Validators.maxLength(1023)]],
  });

  pageTitle = computed(() => this.isEditMode() ? 'Edit Ingredient' : 'New Ingredient');
  pageSubtitle = computed(() => this.isEditMode() ? 'Update ingredient details' : 'Create a custom ingredient');

  constructor() {
    this.nutrientService.getAll().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (list) => {
        this.allNutrients.set(list);
        for (const n of list) {
          if (!this.nutritionForm.contains(String(n.id))) {
            this.nutritionForm.addControl(String(n.id), this.fb.control<number | null>(null, [Validators.min(0)]));
          }
        }
        this.applyLoadedNutrients();
      },
      error: () => this.errorMessage.set('Could not load the nutrient list.'),
    });
    this.nutritionForm.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.nutritionForm.dirty) this.nutritionDirty = true;
    });

    effect(() => {
      const params = this.routeParams();
      const id = params?.['id'];
      if (id) {
        this.isEditMode.set(true);
        this.ingredientId.set(Number(id));
        this.loadIngredient(Number(id));
      }
    });
  }

  private loadIngredient(id: number): void {
    this.loading.set(true);
    this.ingredientService.getIngredient(id).pipe(
      this.loadingService.loading('Loading ingredient...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (ing) => {
        this.ingredientForm.patchValue({
          name: ing.name,
          pluralName: ing.pluralName,
          description: ing.description,
        });
        this.loadedNutrients = ing.nutrients ?? [];
        this.applyLoadedNutrients();
        this.aliases.set(ing.aliases ?? []);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load ingredient.');
        this.loading.set(false);
      },
    });
  }

  /** Stored facts from the API, applied to the controls once both they and the nutrient list exist. */
  private loadedNutrients: { nutrientId: number; amount: number }[] = [];

  private applyLoadedNutrients(): void {
    if (this.loadedNutrients.length === 0 || this.allNutrients().length === 0) return;
    const patch: Record<string, number> = {};
    for (const n of this.loadedNutrients) patch[String(n.nutrientId)] = n.amount;
    this.nutritionForm.patchValue(patch, { emitEvent: false });
    this.nutritionForm.markAsPristine();
    this.nutritionDirty = false;
  }

  unitFor(n: NutrientModel): string {
    return n.defaultMeasurementSymbol || n.defaultMeasurementName;
  }

  key(n: NutrientModel): string {
    return String(n.id);
  }

  /** Entered facts as the API wants them; blanks are omitted. */
  private enteredNutrients(): NutrientValueInput[] {
    return Object.entries(this.nutritionForm.getRawValue())
      .filter(([, v]) => v !== null && v !== undefined && !Number.isNaN(v))
      .map(([id, v]) => ({ nutrientId: Number(id), amount: Number(v) }));
  }

  private describeSaveError(err: unknown, fallback: string): string {
    const e = err as { error?: { message?: string } | string } | undefined;
    const apiMessage = typeof e?.error === 'object' ? e?.error?.message : undefined;
    return apiMessage ?? fallback;
  }

  onSubmit(): void {
    if (this.ingredientForm.invalid || this.nutritionForm.invalid) {
      this.ingredientForm.markAllAsTouched();
      this.nutritionForm.markAllAsTouched();
      this.errorMessage.set('Please fix the highlighted fields.');
      return;
    }
    if (this.saving()) return;
    this.saving.set(true);
    this.errorMessage.set('');

    const form = this.ingredientForm.getRawValue();

    if (this.isEditMode()) {
      const id = this.ingredientId()!;
      this.ingredientService.updateIngredient(id, {
        id,
        name: form.name!,
        pluralName: form.pluralName ?? '',
        description: form.description ?? '',
        // Only send nutrition when it was edited — omitting leaves stored facts untouched.
        ...(this.nutritionDirty ? { nutrients: this.enteredNutrients() } : {}),
      }).pipe(
        this.loadingService.loading('Saving ingredient...'),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe({
        next: () => {
          this.saving.set(false);
          this.router.navigate(['/ingredients/mine']);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(this.describeSaveError(err, 'Failed to save ingredient.'));
        },
      });
    } else {
      const nutrients = this.enteredNutrients();
      this.ingredientService.createIngredient({
        name: form.name!,
        pluralName: form.pluralName ?? '',
        description: form.description ?? '',
        ...(nutrients.length ? { nutrients } : {}),
      }).pipe(
        this.loadingService.loading('Creating ingredient...'),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe({
        next: () => {
          this.saving.set(false);
          this.router.navigate(['/ingredients/mine']);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(this.describeSaveError(err, 'Failed to create ingredient.'));
        },
      });
    }
  }
}
