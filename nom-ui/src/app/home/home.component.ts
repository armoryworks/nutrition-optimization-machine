import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { RecipeSearchService } from '../core/services/recipe-search.service';
import { AuthService } from '../core/services/auth.service';
import { RecipeSearchResult } from '../core/models/recipe-search-result.model';
import { Dashboard } from './dashboard/dashboard.component';

interface RecipeCategory {
  label: string;
  icon: string;
  recipes: RecipeSearchResult[];
}

@Component({
  selector: 'nom-home',
  imports: [MatIconModule, RouterLink, Dashboard, ErrorBanner],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Home implements OnInit {
  private recipeSearch = inject(RecipeSearchService);
  private authService = inject(AuthService);
  private destroyRef = inject(DestroyRef);

  isLoggedIn = computed(() => this.authService.isLoggedIn());
  categories = signal<RecipeCategory[]>([]);
  loading = signal(true);
  error = signal('');

  ngOnInit(): void {
    if (!this.isLoggedIn()) {
      this.loadRecipes();
    }
  }

  loadRecipes(): void {
    this.loading.set(true);
    this.error.set('');

    this.recipeSearch.getPopular(50).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        this.categories.set(this.groupByCategory(response.results));
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load recipes. Please try again.');
        this.loading.set(false);
      }
    });
  }

  private shuffle<T>(array: T[]): void {
    for (let i = array.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [array[i], array[j]] = [array[j], array[i]];
    }
  }

  private groupByCategory(recipes: RecipeSearchResult[]): RecipeCategory[] {
    const categoryConfig: { label: string; icon: string }[] = [
      { label: 'Breakfast', icon: 'egg_alt' },
      { label: 'Lunch', icon: 'lunch_dining' },
      { label: 'Dinner', icon: 'dinner_dining' },
      { label: 'Dessert', icon: 'cake' },
      { label: 'Snacks', icon: 'cookie' },
    ];

    const grouped = new Map<string, RecipeSearchResult[]>();

    for (const recipe of recipes) {
      for (const cat of recipe.categories) {
        const key = cat.toLowerCase();
        if (!grouped.has(key)) {
          grouped.set(key, []);
        }
        grouped.get(key)!.push(recipe);
      }
    }

    return categoryConfig
      .map(config => {
        const all = grouped.get(config.label.toLowerCase()) ?? [];
        this.shuffle(all);
        return { label: config.label, icon: config.icon, recipes: all.slice(0, 4) };
      })
      .filter(cat => cat.recipes.length > 0);
  }
}
