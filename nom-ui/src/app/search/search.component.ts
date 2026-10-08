import { Component, inject, signal, computed, effect, untracked, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RecipeSearchService } from '../core/services/recipe-search.service';
import { ReferenceService } from '../core/services/reference.service';
import { AuthService } from '../core/services/auth.service';
import { RecipeSearchResult } from '../core/models/recipe-search-result.model';
import { RecipeSearchResponse } from '../core/models/recipe-search-response.model';
import { DietConflictsPipe } from '../core/utils/diet-conflict';

type SortOption = 'newest' | 'rating' | 'quickest' | 'name';

const PAGE_SIZE = 48;

const SORTS: Record<SortOption, { sortBy?: string; sortDirection?: string }> = {
  newest: {},
  rating: { sortBy: 'rating', sortDirection: 'desc' },
  quickest: { sortBy: 'totalTime', sortDirection: 'asc' },
  name: { sortBy: 'name', sortDirection: 'asc' },
};

@Component({
  selector: 'nom-search',
  imports: [MatIconModule, MatFormFieldModule, MatSelectModule, MatSlideToggleModule, RouterLink, DietConflictsPipe],
  templateUrl: './search.component.html',
  styleUrl: './search.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Search {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private recipeSearch = inject(RecipeSearchService);
  private referenceService = inject(ReferenceService);
  private destroyRef = inject(DestroyRef);
  readonly isLoggedIn = inject(AuthService).isLoggedIn;

  query = signal('');
  /** Active "recipes containing this ingredient" filter (from ?ingredientId=). */
  ingredientId = signal<number | null>(null);
  ingredientName = signal('');
  maxTime = signal<number | null>(null);
  minRating = signal<number | null>(null);
  recipeTypeId = signal<number | null>(null);
  sort = signal<SortOption>('newest');
  mineOnly = signal(false);
  results = signal<RecipeSearchResult[]>([]);
  totalCount = signal(0);
  page = signal(1);
  loading = signal(false);
  loadingMore = signal(false);
  error = signal('');

  readonly timeOptions = [15, 30, 45, 60];
  readonly ratingOptions = [3, 4];
  readonly sortOptions: { value: SortOption; label: string }[] = [
    { value: 'newest', label: 'Newest' },
    { value: 'rating', label: 'Top rated' },
    { value: 'quickest', label: 'Quickest' },
    { value: 'name', label: 'Name A–Z' },
  ];
  readonly recipeTypes = toSignal(this.referenceService.getRecipeTypes(), { initialValue: [] });

  hasFilters = computed(() =>
    this.maxTime() !== null || this.minRating() !== null || this.recipeTypeId() !== null
    || this.mineOnly() || this.sort() !== 'newest');
  canLoadMore = computed(() => this.results().length < this.totalCount());

  private queryParams = toSignal(this.route.queryParams);

  constructor() {
    effect(() => {
      const params = this.queryParams();
      const q = params?.['q'] || '';
      const ingredientId = Number(params?.['ingredientId']) || null;
      this.query.set(q);
      this.ingredientId.set(ingredientId);
      this.ingredientName.set(ingredientId ? params?.['ingredient'] || 'this ingredient' : '');
      this.maxTime.set(Number(params?.['time']) || null);
      this.minRating.set(Number(params?.['rating']) || null);
      this.recipeTypeId.set(Number(params?.['type']) || null);
      this.sort.set(params?.['sort'] in SORTS ? params?.['sort'] : 'newest');
      this.mineOnly.set(params?.['mine'] === '1');
      untracked(() => this.performSearch(q));
    });
  }

  setFilter(key: 'time' | 'rating' | 'type' | 'sort' | 'mine', value: string | number | null): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [key]: value === '' ? null : value },
      queryParamsHandling: 'merge',
    });
  }

  clearFilters(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { time: null, rating: null, type: null, sort: null, mine: null },
      queryParamsHandling: 'merge',
    });
  }

  performSearch(query: string): void {
    this.loading.set(true);
    this.error.set('');
    this.page.set(1);

    if (!query.trim() && !this.ingredientId() && !this.hasFilters()) {
      this.recipeSearch.getPopular(50).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (response) => {
          this.results.set(response.results);
          this.totalCount.set(response.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Failed to load recipes.');
          this.loading.set(false);
        }
      });
      return;
    }

    this.fetchPage(1).subscribe({
      next: (response) => {
        this.results.set(response.results);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Search failed. Please try again.');
        this.loading.set(false);
      }
    });
  }

  loadMore(): void {
    const next = this.page() + 1;
    this.loadingMore.set(true);
    this.fetchPage(next).subscribe({
      next: (response) => {
        const seen = new Set(this.results().map(r => r.id));
        this.results.update(rs => [...rs, ...response.results.filter(r => !seen.has(r.id))]);
        this.totalCount.set(response.totalCount);
        this.page.set(next);
        this.loadingMore.set(false);
      },
      error: () => this.loadingMore.set(false),
    });
  }

  private fetchPage(page: number) {
    const ingredientId = this.ingredientId();
    const typeId = this.recipeTypeId();
    return this.recipeSearch.search({
      query: this.query().trim(),
      ingredientIds: ingredientId ? [ingredientId] : undefined,
      cuisineTypeIds: typeId ? [typeId] : undefined,
      maxTotalTime: this.maxTime() ?? undefined,
      minRating: this.minRating() ?? undefined,
      mineOnly: this.mineOnly() || undefined,
      ...SORTS[this.sort()],
      page,
      pageSize: PAGE_SIZE,
      includeIngredients: false,
      includeSteps: false,
      includeNutrition: false,
    }).pipe(takeUntilDestroyed<RecipeSearchResponse>(this.destroyRef));
  }
}
