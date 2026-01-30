import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';

import { AmwButtonComponent, AmwIconComponent, AmwInputComponent, AmwCardComponent, AmwPopoverComponent } from 'angular-material-wrap';
import { RecipeSearchService } from '../../services/recipe-search.service';
import { RecipeSearchResult } from '../../models/recipe-search.model';

@Component({
  selector: 'nom-recipe-search',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    AmwButtonComponent,
    AmwIconComponent,
    AmwInputComponent,
    AmwCardComponent,
    AmwPopoverComponent
  ],
  templateUrl: './recipe-search.component.html',
  styleUrls: ['./recipe-search.component.scss']
})
export class RecipeSearchComponent implements OnInit, OnDestroy {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private recipeSearchService = inject(RecipeSearchService);
  private destroy$ = new Subject<void>();

  // Fallback placeholder image - plate and utensils SVG
  readonly placeholderImage = `data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='400' height='400' viewBox='0 0 400 400'%3E%3Crect fill='%23374151' width='400' height='400'/%3E%3Cg fill='%239CA3AF' transform='translate(100,100)'%3E%3Ccircle cx='100' cy='100' r='90' fill='none' stroke='%239CA3AF' stroke-width='8'/%3E%3Ccircle cx='100' cy='100' r='60' fill='none' stroke='%239CA3AF' stroke-width='4'/%3E%3Cpath d='M30 40 L30 100 M30 40 L30 20 M25 20 L25 50 M35 20 L35 50 M20 20 L20 45 M40 20 L40 45' stroke='%239CA3AF' stroke-width='4' stroke-linecap='round' fill='none'/%3E%3Cpath d='M170 40 L170 100 M170 20 L175 60 L170 60 Z' stroke='%239CA3AF' stroke-width='4' stroke-linecap='round' fill='%239CA3AF'/%3E%3C/g%3E%3C/svg%3E`;

  searchControl = new FormControl('');
  searchResults = signal<RecipeSearchResult[]>([]);
  isLoading = signal(false);
  hasSearched = signal(false);
  hasError = signal(false);
  errorMessage = signal('');
  searchQuery = signal('');
  totalCount = signal(0);
  currentPage = signal(1);
  pageSize = 20;

  ngOnInit(): void {
    // Read query parameter and perform initial search
    this.route.queryParams.pipe(takeUntil(this.destroy$)).subscribe(params => {
      const query = params['q'];
      if (query) {
        this.searchControl.setValue(query);
        this.performSearch(query);
      }
    });

    // Set up search input debounce
    this.searchControl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(value => {
      if (value && value.trim()) {
        this.updateUrlAndSearch(value.trim());
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private updateUrlAndSearch(query: string): void {
    // Update URL without triggering navigation
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: query },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
    this.performSearch(query);
  }

  private performSearch(query: string): void {
    this.isLoading.set(true);
    this.hasSearched.set(true);
    this.hasError.set(false);
    this.errorMessage.set('');
    this.searchQuery.set(query);

    this.recipeSearchService.searchRecipes({
      query: query,
      isPublic: true,
      isApproved: true,
      page: this.currentPage(),
      pageSize: this.pageSize,
      includeIngredients: false,
      includeSteps: false,
      includeNutrition: false
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (response) => {
        this.searchResults.set(response.recipes || response.results || []);
        this.totalCount.set(response.totalCount || 0);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Search failed:', error);
        this.searchResults.set([]);
        this.totalCount.set(0);
        this.hasError.set(true);
        this.errorMessage.set(error?.message || 'Unable to search recipes. Please try again.');
        this.isLoading.set(false);
      }
    });
  }

  onSearchSubmit(): void {
    const query = this.searchControl.value?.trim();
    if (query) {
      this.updateUrlAndSearch(query);
    }
  }

  viewRecipe(recipe: RecipeSearchResult): void {
    this.router.navigate(['/recipe', recipe.id]);
  }

  // Public user methods
  browsePublicRecipes(): void {
    this.recipeSearchService.getPopularRecipes(20).pipe(takeUntil(this.destroy$)).subscribe({
      next: (response) => {
        this.searchResults.set(response.recipes || response.results || []);
        this.totalCount.set(response.totalCount);
        this.hasSearched.set(true);
      },
      error: (error) => {
        console.error('Failed to load popular recipes:', error);
      }
    });
  }

  // Navigation methods
  onLogin(): void {
    this.router.navigate(['/login']);
  }

  onRegister(): void {
    this.router.navigate(['/register']);
  }

  onImageError(event: Event): void {
    const img = event.target as HTMLImageElement;
    if (img.src !== this.placeholderImage) {
      img.src = this.placeholderImage;
    }
  }

}
