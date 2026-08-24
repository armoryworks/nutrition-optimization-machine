import { Component, inject, signal, effect, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { RecipeSearchService } from '../core/services/recipe-search.service';
import { RecipeSearchResult } from '../core/models/recipe-search-result.model';

@Component({
  selector: 'nom-search',
  imports: [MatIconModule, RouterLink],
  templateUrl: './search.component.html',
  styleUrl: './search.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Search {
  private route = inject(ActivatedRoute);
  private recipeSearch = inject(RecipeSearchService);
  private destroyRef = inject(DestroyRef);

  query = signal('');
  /** Active "recipes containing this ingredient" filter (from ?ingredientId=). */
  ingredientId = signal<number | null>(null);
  ingredientName = signal('');
  results = signal<RecipeSearchResult[]>([]);
  totalCount = signal(0);
  loading = signal(false);
  error = signal('');

  private queryParams = toSignal(this.route.queryParams);

  constructor() {
    effect(() => {
      const params = this.queryParams();
      const q = params?.['q'] || '';
      const ingredientId = Number(params?.['ingredientId']) || null;
      this.query.set(q);
      this.ingredientId.set(ingredientId);
      this.ingredientName.set(ingredientId ? params?.['ingredient'] || 'this ingredient' : '');
      this.performSearch(q);
    });
  }

  performSearch(query: string): void {
    this.loading.set(true);
    this.error.set('');

    if (!query.trim() && !this.ingredientId()) {
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

    const ingredientId = this.ingredientId();
    this.recipeSearch.search({
      query: query.trim(),
      ingredientIds: ingredientId ? [ingredientId] : undefined,
      page: 1,
      pageSize: 50,
      includeIngredients: false,
      includeSteps: false,
      includeNutrition: false,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
}
