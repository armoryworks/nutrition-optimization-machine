import { Component, DestroyRef, effect, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { RecipeService } from '../core/services/recipe.service';
import { AuthService } from '../core/services/auth.service';
import { RecipeModel, RecipeDietMatchModel } from '../core/models/recipe.model';
import {
  IngredientSubstitutionModel,
  RecipeIngredientModel,
} from '../core/models/recipe-ingredient.model';
import {
  RecipeSubstitutionModel,
  RecipeAugmentationModel,
} from '../core/models/recipe-substitution.model';
import { NutritionLabel } from '../shared/components/nutrition-label/nutrition-label.component';
import { RecipeComments } from './recipe-comments.component';
import { RecipeRating } from './recipe-rating.component';

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** One rendered instruction step, after swap effects and augmentations apply. */
export interface DisplayStepModel {
  text: string;
  order: number;
  /** Text was replaced by a substitution's step effect. */
  altered: boolean;
  /** Pseudo-step injected by an enabled augmentation. */
  augmentation: boolean;
  temperatureFahrenheit: number | null;
  durationDeltaMinutes: number | null;
}

/** One rendered ingredient row: the recipe's own line, or an enabled add-in. */
export interface DisplayIngredientRowModel {
  ingredient: RecipeIngredientModel;
  swap: IngredientSubstitutionModel | null;
  addIn: boolean;
}

@Component({
  selector: 'nom-recipe-detail',
  imports: [MatIconModule, MatButtonModule, RouterLink, NutritionLabel, RecipeComments, RecipeRating],
  templateUrl: './recipe-detail.component.html',
  styleUrl: './recipe-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'onDocumentClick($event)',
  },
})
export class RecipeDetail {
  private route = inject(ActivatedRoute);
  private recipeService = inject(RecipeService);
  private destroyRef = inject(DestroyRef);
  authService = inject(AuthService);

  private routeParams = toSignal(this.route.params);

  recipe = signal<RecipeModel | null>(null);
  loading = signal(true);
  error = signal('');
  activeTab = signal(0);

  // Substitution state: original ingredientId -> chosen substitute.
  swaps = signal<Map<number, IngredientSubstitutionModel>>(new Map());
  viewOriginal = signal(false);
  subsOpenFor = signal<number | null>(null);
  variationSaved = signal(false);
  savingVariation = signal(false);

  // Diet tab: the caller's restriction hits (null = not yet loaded).
  dietMatches = signal<RecipeDietMatchModel[] | null>(null);

  // Recipe-scoped substitutions (with step effects) and optional add-ins.
  recipeSubs = signal<RecipeSubstitutionModel[]>([]);
  augmentations = signal<RecipeAugmentationModel[]>([]);
  enabledAugmentations = signal<Set<number>>(new Set());

  loggedIn = computed(() => this.authService.personId() != null);
  hasSwaps = computed(() => this.swaps().size > 0);

  // Ingredient names for the nutrition label's Ingredients view — reflects
  // active swaps, and appends sub-ingredient detail when the data has it.
  ingredientNames = computed(() => {
    return this.displayIngredients().map((row) => {
      if (row.swap) return row.swap.name;
      const subs = row.ingredient.subIngredients;
      return subs && subs.length > 0
        ? `${row.ingredient.name} (${subs.join(', ')})`
        : row.ingredient.name;
    }).filter((n) => !!n);
  });

  // Raw numeric average for the Rating tab — no stars in the rail.
  ratingDisplay = computed(() => {
    const r = this.recipe()?.rating;
    return (r ? Number(r) : 0).toFixed(1);
  });

  isAuthor = computed(() => {
    const r = this.recipe();
    const personId = this.authService.personId();
    return r != null && personId != null && r.authorId === personId;
  });

  /** Hide the seeded system account — only real people get a byline. */
  displayAuthor = computed(() => {
    const name = this.recipe()?.authorName;
    return name && name.toLowerCase() !== 'system' && name.toLowerCase() !== 'unknown' ? name : '';
  });

  /** Recipe-scoped substitutions grouped by the ORIGINAL ingredient's id. */
  recipeSubsByIngredient = computed(() => {
    const map = new Map<number, RecipeSubstitutionModel[]>();
    for (const sub of this.recipeSubs()) {
      const list = map.get(sub.ingredientId) ?? [];
      list.push(sub);
      map.set(sub.ingredientId, list);
    }
    return map;
  });

  /**
   * Ingredient rows with the active swap applied (unless viewing the
   * original), plus enabled augmentations appended as add-in rows.
   */
  displayIngredients = computed<DisplayIngredientRowModel[]>(() => {
    const list = this.recipe()?.ingredients ?? [];
    const original = this.viewOriginal();
    const swaps = original ? null : this.swaps();
    const rows: DisplayIngredientRowModel[] = list.map((ingredient) => ({
      ingredient,
      swap: swaps?.get(ingredient.ingredientId) ?? null,
      addIn: false,
    }));
    if (!original) {
      const enabled = this.enabledAugmentations();
      for (const aug of this.augmentations()) {
        if (!enabled.has(aug.id)) continue;
        rows.push({
          ingredient: {
            ingredientId: aug.ingredientId,
            name: aug.ingredientName,
            quantity: aug.quantity ?? 0,
            measurementId: aug.measurementId ?? 0,
            measurement: aug.measurement,
          },
          swap: null,
          addIn: true,
        });
      }
    }
    return rows;
  });

  /**
   * Instruction steps after active swaps and add-ins apply.
   *
   * Substitutions WITH step effects rewrite the matching steps wholesale
   * (effect.stepNumber against the step's order) and carry temp/duration
   * badges; substitutions WITHOUT effects fall back to name find/replace.
   * Enabled augmentations with insertAfterStepNumber inject a pseudo-step
   * after that step.
   */
  displaySteps = computed<DisplayStepModel[]>(() => {
    const steps: DisplayStepModel[] = (this.recipe()?.steps ?? []).map((s) => ({
      text: s.description,
      order: s.order,
      altered: false,
      augmentation: false,
      temperatureFahrenheit: null,
      durationDeltaMinutes: null,
    }));
    if (this.viewOriginal()) return steps;

    const renames: [RegExp, string][] = [];
    for (const [origId, pick] of this.swaps()) {
      if (pick.stepEffects?.length) {
        for (const effect of pick.stepEffects) {
          const step = steps.find((s) => s.order === effect.stepNumber);
          if (!step) continue;
          step.text = effect.alteredDescription;
          step.altered = true;
          if (effect.newTemperatureFahrenheit != null) {
            step.temperatureFahrenheit = effect.newTemperatureFahrenheit;
          }
          if (effect.durationDeltaMinutes != null) {
            step.durationDeltaMinutes = effect.durationDeltaMinutes;
          }
        }
      } else {
        const orig = this.recipe()?.ingredients?.find((i) => i.ingredientId === origId);
        if (orig) renames.push([new RegExp(escapeRegExp(orig.name), 'gi'), pick.name]);
      }
    }
    if (renames.length > 0) {
      for (const step of steps) {
        if (step.altered) continue;
        for (const [from, to] of renames) step.text = step.text.replace(from, to);
      }
    }

    const enabled = this.enabledAugmentations();
    if (enabled.size === 0) return steps;
    const out: DisplayStepModel[] = [];
    for (const step of steps) {
      out.push(step);
      for (const aug of this.augmentations()) {
        if (!enabled.has(aug.id) || aug.insertAfterStepNumber !== step.order) continue;
        out.push({
          text: `Add ${aug.ingredientName}${aug.instructions ? ': ' + aug.instructions : ''}`,
          order: step.order,
          altered: false,
          augmentation: true,
          temperatureFahrenheit: aug.newTemperatureFahrenheit ?? null,
          durationDeltaMinutes: aug.durationDeltaMinutes ?? null,
        });
      }
    }
    return out;
  });

  constructor() {
    effect(() => {
      const params = this.routeParams();
      if (!params) return;
      const id = Number(params['id']);
      if (isNaN(id)) {
        this.loading.set(false);
        return;
      }
      this.loadRecipe(id);
    });
  }

  loadRecipe(id: number): void {
    this.loading.set(true);
    this.error.set('');
    this.swaps.set(new Map());
    this.viewOriginal.set(false);
    this.subsOpenFor.set(null);
    this.variationSaved.set(false);
    this.dietMatches.set(null);
    this.recipeSubs.set([]);
    this.augmentations.set([]);
    this.enabledAugmentations.set(new Set());

    this.recipeService.getRecipe(id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (recipe) => {
        this.recipe.set(recipe);
        this.applySavedVariation(recipe);
        this.loading.set(false);
        this.loadEnhancements(id);
        if (this.loggedIn()) {
          this.recipeService.getDietMatches(id).pipe(
            takeUntilDestroyed(this.destroyRef),
          ).subscribe({
            next: (matches) => this.dietMatches.set(matches),
            error: () => this.dietMatches.set([]),
          });
        }
      },
      error: (err) => {
        if (err.status === 404) {
          this.recipe.set(null);
        } else {
          this.error.set('Failed to load recipe.');
        }
        this.loading.set(false);
      }
    });
  }

  /** Recipe-scoped substitutions + augmentations load alongside the recipe; failures just mean none show. */
  private loadEnhancements(id: number): void {
    this.recipeService.getSubstitutions(id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (subs) => this.recipeSubs.set(subs),
      error: () => this.recipeSubs.set([]),
    });
    this.recipeService.getAugmentations(id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (augs) => this.augmentations.set(augs),
      error: () => this.augmentations.set([]),
    });
  }

  /** The caller's saved default variation applies on load; original stays a toggle away. */
  private applySavedVariation(recipe: RecipeModel): void {
    if (!recipe.variation?.length) return;
    const map = new Map<number, IngredientSubstitutionModel>();
    for (const item of recipe.variation) {
      map.set(item.ingredientId, {
        ingredientId: item.substituteIngredientId,
        name: item.substituteName,
        quantity: item.quantity,
        measurement: item.measurement,
        measurementId: item.measurementId,
      });
    }
    this.swaps.set(map);
    this.variationSaved.set(true);
  }

  toggleSubsPopover(ingredientId: number, event: Event): void {
    event.stopPropagation();
    this.subsOpenFor.update((cur) => (cur === ingredientId ? null : ingredientId));
  }

  onDocumentClick(_event: Event): void {
    if (this.subsOpenFor() !== null) this.subsOpenFor.set(null);
  }

  pickSubstitution(originalId: number, sub: IngredientSubstitutionModel): void {
    this.swaps.update((m) => new Map(m).set(originalId, sub));
    this.subsOpenFor.set(null);
    this.viewOriginal.set(false);
    this.variationSaved.set(false);
  }

  /** Recipe-scoped substitutions offered for one of the recipe's ingredient lines. */
  recipeSubsFor(ingredientId: number): RecipeSubstitutionModel[] {
    return this.recipeSubsByIngredient().get(ingredientId) ?? [];
  }

  /** Explicit substitute quantity wins; otherwise original quantity × ratio. */
  recipeSubQuantity(ingredient: RecipeIngredientModel, sub: RecipeSubstitutionModel): number {
    if (sub.substituteQuantity != null) return sub.substituteQuantity;
    return Math.round(ingredient.quantity * sub.ratio * 100) / 100;
  }

  pickRecipeSubstitution(ingredient: RecipeIngredientModel, sub: RecipeSubstitutionModel): void {
    this.pickSubstitution(ingredient.ingredientId, {
      ingredientId: sub.substituteIngredientId,
      name: sub.substituteName,
      quantity: this.recipeSubQuantity(ingredient, sub),
      measurement: sub.substituteMeasurement ?? ingredient.measurement,
      measurementId: sub.substituteMeasurementId ?? ingredient.measurementId,
      notes: sub.notes,
      stepEffects: sub.stepEffects,
    });
  }

  toggleAugmentation(augmentationId: number): void {
    this.enabledAugmentations.update((set) => {
      const next = new Set(set);
      if (next.has(augmentationId)) {
        next.delete(augmentationId);
      } else {
        next.add(augmentationId);
      }
      return next;
    });
  }

  revertSwap(originalId: number): void {
    this.swaps.update((m) => {
      const next = new Map(m);
      next.delete(originalId);
      return next;
    });
    this.variationSaved.set(false);
  }

  saveAsDefault(): void {
    const r = this.recipe();
    if (!r || this.swaps().size === 0 || this.savingVariation()) return;
    this.savingVariation.set(true);
    const items = [...this.swaps().entries()].map(([ingredientId, sub]) => ({
      ingredientId,
      substituteIngredientId: sub.ingredientId,
    }));
    this.recipeService.saveVariation(r.id, items).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.variationSaved.set(true);
        this.savingVariation.set(false);
      },
      error: () => this.savingVariation.set(false),
    });
  }
}
