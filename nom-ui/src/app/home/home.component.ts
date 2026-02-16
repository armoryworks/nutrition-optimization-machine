import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AmwIconComponent, AmwButtonComponent, AmwInlineLoadingComponent } from 'angular-material-wrap';
import { RecipeSearchService } from '../recipe/services/recipe-search.service';
import { RecipeSearchResult } from '../recipe/models/recipe-search.model';

@Component({
  selector: 'nom-home',
  standalone: true,
  imports: [
    RouterLink,
    AmwIconComponent,
    AmwButtonComponent,
    AmwInlineLoadingComponent,
  ],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent implements OnInit {
  private recipeSearchService = inject(RecipeSearchService);
  private router = inject(Router);

  recipes = signal<RecipeSearchResult[]>([]);
  isLoading = signal(false);
  error = signal('');

  mealGroups = ['Breakfast', 'Lunch', 'Dinner', 'Snacks'];

  ngOnInit(): void {
    this.loadRecipes();
  }

  loadRecipes(): void {
    this.isLoading.set(true);
    this.error.set('');

    this.recipeSearchService.getPopularRecipes(24).subscribe({
      next: (response) => {
        this.recipes.set(response.recipes || response.results || []);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to load recipes.');
        this.isLoading.set(false);
      },
    });
  }

  getRecipesForGroup(group: string): RecipeSearchResult[] {
    return this.recipes().filter(r =>
      r.categories?.some(c => c.toLowerCase() === group.toLowerCase())
    );
  }

  getUncategorizedRecipes(): RecipeSearchResult[] {
    const categorized = new Set<number>();
    for (const group of this.mealGroups) {
      for (const r of this.getRecipesForGroup(group)) {
        categorized.add(r.id);
      }
    }
    return this.recipes().filter(r => !categorized.has(r.id));
  }

  hasAnyCategorizedRecipes(): boolean {
    return this.mealGroups.some(g => this.getRecipesForGroup(g).length > 0);
  }

  getGroupIcon(group: string): string {
    switch (group) {
      case 'Breakfast': return 'wb_sunny';
      case 'Lunch': return 'restaurant';
      case 'Dinner': return 'dinner_dining';
      case 'Snacks': return 'cookie';
      default: return 'restaurant';
    }
  }

  navigateToRecipe(id: number): void {
    this.router.navigate(['/recipe', id]);
  }

  navigateToSearch(): void {
    this.router.navigate(['/search']);
  }
}
