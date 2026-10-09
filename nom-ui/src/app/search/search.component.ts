import { Component, inject, signal, computed, effect, untracked, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subscription, catchError, map, of, switchMap } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RecipeSearchService } from '../core/services/recipe-search.service';
import { HouseholdStore } from '../core/services/household-store';
import { AuthService } from '../core/services/auth.service';
import { RecipeSearchResult } from '../core/models/recipe-search-result.model';
import { RecipeSearchResponse } from '../core/models/recipe-search-response.model';
import { RecipeSearchFilterOption, RecipeSearchFilterOptions } from '../core/models/recipe-search-filter-options.model';
import {
  EMPTY_FILTERS,
  MAX_TIME_OPTIONS,
  MIN_RATING_OPTIONS,
  SORT_OPTIONS,
  SearchFilterPill,
  SearchFilters,
  SearchSort,
  filterPills,
  filtersToParams,
  hasActiveFilters,
  parseSearchFilters,
  removeFilter,
  toggleId,
} from './search-filters';

@Component({
  selector: 'nom-search',
  imports: [
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatChipsModule,
    MatSlideToggleModule,
    RouterLink,
  ],
  templateUrl: './search.component.html',
  styleUrl: './search.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'closeFilterPanel()' },
})
export class Search {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private recipeSearch = inject(RecipeSearchService);
  private householdStore = inject(HouseholdStore);
  private authService = inject(AuthService);
  private destroyRef = inject(DestroyRef);

  query = signal('');
  /** Active "recipes containing this ingredient" filter (from ?ingredientId=). */
  ingredientId = signal<number | null>(null);
  ingredientName = signal('');
  results = signal<RecipeSearchResult[]>([]);
  totalCount = signal(0);
  loading = signal(false);
  error = signal('');

  filters = signal<SearchFilters>(EMPTY_FILTERS);
  filterOptions = signal<RecipeSearchFilterOptions | null>(null);
  filterPanelOpen = signal(false);
  householdId = signal<number | null>(null);

  readonly sortOptions = SORT_OPTIONS;
  readonly maxTimeOptions = MAX_TIME_OPTIONS;
  readonly minRatingOptions = MIN_RATING_OPTIONS;

  canFilterByKitchen = computed(() => this.authService.isLoggedIn() && this.householdId() !== null);
  pills = computed(() => {
    const filters = this.filters();
    return filterPills(this.canFilterByKitchen() ? filters : { ...filters, kitchen: false }, this.filterOptions());
  });
  toolGroups = computed(() => {
    const groups = new Map<string, RecipeSearchFilterOption[]>();
    for (const tool of this.filterOptions()?.kitchenTools ?? []) {
      const name = tool.group || 'Other';
      groups.set(name, [...(groups.get(name) ?? []), tool]);
    }
    return [...groups].map(([name, tools]) => ({ name, tools }));
  });

  private queryParams = toSignal(this.route.queryParams);
  private searchSub?: Subscription;

  constructor() {
    this.recipeSearch.getFilterOptions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (options) => this.filterOptions.set(options),
      error: () => this.filterOptions.set(null),
    });

    effect(() => {
      if (!this.authService.isLoggedIn()) {
        this.householdId.set(null);
        return;
      }
      untracked(() => this.loadHousehold());
    });

    effect(() => {
      const params = this.queryParams();
      const q = params?.['q'] || '';
      const ingredientId = Number(params?.['ingredientId']) || null;
      this.query.set(q);
      this.ingredientId.set(ingredientId);
      this.ingredientName.set(ingredientId ? params?.['ingredient'] || 'this ingredient' : '');
      this.filters.set(parseSearchFilters(params));
      untracked(() => this.performSearch(q));
    });
  }

  performSearch(query: string): void {
    this.searchSub?.unsubscribe();
    this.loading.set(true);
    this.error.set('');

    const filters = this.filters();
    const ingredientId = this.ingredientId();
    const browsing = !query.trim() && !ingredientId && !hasActiveFilters(filters) && filters.sort === 'relevance';

    const request$: Observable<RecipeSearchResponse> = browsing
      ? this.recipeSearch.getPopular(50)
      : this.kitchenHouseholdId(filters).pipe(
          switchMap((householdId) => {
            const sort = SORT_OPTIONS.find((o) => o.value === filters.sort) ?? SORT_OPTIONS[0];
            return this.recipeSearch.search({
              query: query.trim(),
              ingredientIds: ingredientId ? [ingredientId] : undefined,
              categoryIds: filters.mealTypeIds.length ? filters.mealTypeIds : undefined,
              recipeTypeIds: filters.courseIds.length ? filters.courseIds : undefined,
              toolIds: filters.toolIds.length ? filters.toolIds : undefined,
              maxTotalTime: filters.maxTime ?? undefined,
              minRating: filters.minRating ?? undefined,
              cookableForHouseholdId: householdId ?? undefined,
              sortBy: sort.sortBy,
              sortDirection: sort.sortDirection,
              page: 1,
              pageSize: 50,
              includeIngredients: false,
              includeSteps: false,
              includeNutrition: false,
            });
          }),
        );

    this.searchSub = request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        this.results.set(response.results);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(browsing ? 'Failed to load recipes.' : 'Search failed. Please try again.');
        this.loading.set(false);
      }
    });
  }

  toggleFilterPanel(): void {
    this.filterPanelOpen.update((open) => !open);
  }

  closeFilterPanel(): void {
    this.filterPanelOpen.set(false);
  }

  setSort(sort: SearchSort): void {
    this.applyFilters({ ...this.filters(), sort });
  }

  toggleMealType(id: number): void {
    this.applyFilters({ ...this.filters(), mealTypeIds: toggleId(this.filters().mealTypeIds, id) });
  }

  toggleCourse(id: number): void {
    this.applyFilters({ ...this.filters(), courseIds: toggleId(this.filters().courseIds, id) });
  }

  toggleTool(id: number): void {
    this.applyFilters({ ...this.filters(), toolIds: toggleId(this.filters().toolIds, id) });
  }

  setMaxTime(minutes: number): void {
    this.applyFilters({ ...this.filters(), maxTime: this.filters().maxTime === minutes ? null : minutes });
  }

  setMinRating(stars: number): void {
    this.applyFilters({ ...this.filters(), minRating: this.filters().minRating === stars ? null : stars });
  }

  setKitchen(kitchen: boolean): void {
    this.applyFilters({ ...this.filters(), kitchen });
  }

  removePill(pill: SearchFilterPill): void {
    this.applyFilters(removeFilter(this.filters(), pill));
  }

  clearFilters(): void {
    this.applyFilters({ ...EMPTY_FILTERS, sort: this.filters().sort });
  }

  private applyFilters(filters: SearchFilters): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: filtersToParams(filters),
      queryParamsHandling: 'merge',
    });
  }

  private kitchenHouseholdId(filters: SearchFilters): Observable<number | null> {
    if (!filters.kitchen || !this.authService.isLoggedIn()) return of(null);
    return this.householdStore.getHouseholds().pipe(
      map((households) => households[0]?.id ?? null),
      catchError(() => of(null)),
    );
  }

  private loadHousehold(): void {
    this.householdStore.getHouseholds().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (households) => this.householdId.set(households[0]?.id ?? null),
      error: () => this.householdId.set(null),
    });
  }
}
