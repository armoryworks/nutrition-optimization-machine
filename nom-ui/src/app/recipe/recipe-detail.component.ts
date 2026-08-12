import { Component, DestroyRef, effect, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { RecipeService } from '../core/services/recipe.service';
import { AuthService } from '../core/services/auth.service';
import { RecipeModel, RecipeDietMatchModel } from '../core/models/recipe.model';
import { IngredientSubstitutionModel } from '../core/models/recipe-ingredient.model';
import { NutritionLabel } from '../shared/components/nutrition-label/nutrition-label.component';
import { RecipeComments } from './recipe-comments.component';
import { RecipeRating } from './recipe-rating.component';

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
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

  /** Ingredient rows with the active swap applied (unless viewing the original). */
  displayIngredients = computed(() => {
    const list = this.recipe()?.ingredients ?? [];
    const swaps = this.viewOriginal() ? null : this.swaps();
    return list.map((ingredient) => ({
      ingredient,
      swap: swaps?.get(ingredient.ingredientId) ?? null,
    }));
  });

  /** Step text with swapped ingredient names substituted in. */
  displaySteps = computed(() => {
    const steps = (this.recipe()?.steps ?? []).map((s) => s.description);
    if (this.viewOriginal() || this.swaps().size === 0) return steps;
    const renames: [RegExp, string][] = [];
    for (const [origId, pick] of this.swaps()) {
      const orig = this.recipe()?.ingredients?.find((i) => i.ingredientId === origId);
      if (orig) renames.push([new RegExp(escapeRegExp(orig.name), 'gi'), pick.name]);
    }
    return steps.map((text) => {
      let out = text;
      for (const [from, to] of renames) out = out.replace(from, to);
      return out;
    });
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

    this.recipeService.getRecipe(id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (recipe) => {
        this.recipe.set(recipe);
        this.applySavedVariation(recipe);
        this.loading.set(false);
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
