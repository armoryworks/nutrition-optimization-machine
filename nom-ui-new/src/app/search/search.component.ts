import { Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { RecipeSearchService } from '../core/services/recipe-search.service';
import { RecipeSearchResult } from '../core/models/recipe-search.model';

@Component({
  selector: 'nom-search',
  imports: [MatIconModule, RouterLink],
  templateUrl: './search.component.html',
  styleUrl: './search.component.scss'
})
export class Search implements OnInit {
  private route = inject(ActivatedRoute);
  private recipeSearch = inject(RecipeSearchService);

  query = signal('');
  results = signal<RecipeSearchResult[]>([]);
  totalCount = signal(0);
  loading = signal(false);
  error = signal('');

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const q = params['q'] || '';
      this.query.set(q);
      this.performSearch(q);
    });
  }

  performSearch(query: string): void {
    this.loading.set(true);
    this.error.set('');

    if (!query.trim()) {
      this.recipeSearch.getPopular(50).subscribe({
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

    this.recipeSearch.search({
      query: query.trim(),
      page: 1,
      pageSize: 50,
      includeIngredients: false,
      includeSteps: false,
      includeNutrition: false,
    }).subscribe({
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
