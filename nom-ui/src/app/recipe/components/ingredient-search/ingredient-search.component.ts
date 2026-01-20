// File: nom-ui/src/app/recipe/components/ingredient-search/ingredient-search.component.ts

import { Component, OnDestroy, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { AmwAutocompleteComponent, AmwCardComponent, AmwIconComponent, AmwProgressBarComponent } from 'angular-material-wrap';
import { Subject, of, catchError, takeUntil, finalize } from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { IngredientSearchResponseModel } from '../../models/ingredient-search-response.model';
import { IngredientModel } from '../../models/ingredient.model';
import { IngredientDetailsComponent } from '../ingredient-details/ingredient-details.component';

interface AutocompleteOption {
  value: any;
  label: string;
  disabled?: boolean;
}

@Component({
  selector: 'nom-ingredient-search',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwAutocompleteComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwProgressBarComponent,
    IngredientDetailsComponent,
  ],
  templateUrl: './ingredient-search.component.html',
  styleUrls: ['./ingredient-search.component.scss'],
})
export class IngredientSearchComponent implements OnDestroy {
  private recipeService = inject(RecipeService);

  searchControl = new FormControl<IngredientSearchResponseModel | null>(null);
  autocompleteOptions = signal<AutocompleteOption[]>([]);
  selectedIngredient = signal<IngredientModel | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);
  private destroy$ = new Subject<void>();

  // Store the full ingredient data for lookup
  private ingredientCache = new Map<number, IngredientSearchResponseModel>();

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  displayFn = (value: any): string => {
    if (value && typeof value === 'object' && value.name) {
      return value.name;
    }
    return value?.label || '';
  };

  onInputChanged(term: string): void {
    if (term && term.length > 2) {
      this.isLoading.set(true);
      this.error.set(null);

      this.recipeService.searchIngredients(term)
        .pipe(
          takeUntil(this.destroy$),
          finalize(() => this.isLoading.set(false)),
          catchError((error) => {
            console.error('Error searching ingredients:', error);
            this.error.set('Failed to search ingredients. Please try again.');
            return of([]);
          })
        )
        .subscribe((ingredients) => {
          // Cache the ingredients and convert to AutocompleteOption format
          this.ingredientCache.clear();
          const options = ingredients.map((ingredient) => {
            this.ingredientCache.set(ingredient.id, ingredient);
            return {
              value: ingredient.id,
              label: ingredient.name
            };
          });
          this.autocompleteOptions.set(options);
        });
    } else {
      this.autocompleteOptions.set([]);
    }
  }

  onOptionSelected(option: AutocompleteOption): void {
    const ingredientId = option.value;
    const ingredient = this.ingredientCache.get(ingredientId);

    if (!ingredient) {
      return;
    }

    this.isLoading.set(true);
    this.error.set(null);
    this.recipeService
      .getIngredientDetails(ingredient.id)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false)),
        catchError((error) => {
          console.error('Error loading ingredient details:', error);
          this.error.set('Failed to load ingredient details.');
          return of(null);
        })
      )
      .subscribe((details) => {
        this.selectedIngredient.set(details);
      });
  }
}
