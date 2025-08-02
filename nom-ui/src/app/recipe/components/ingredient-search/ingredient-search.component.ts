// File: nom-ui/src/app/recipe/components/ingredient-search/ingredient-search.component.ts

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  Observable,
  Subject,
  of,
  debounceTime,
  distinctUntilChanged,
  switchMap,
  catchError,
  takeUntil,
  tap,
} from 'rxjs';
import { RecipeService } from '../../services/recipe.service';
import { IngredientSearchResponseModel } from '../../models/ingredient-search-response.model';
import { IngredientModel } from '../../models/ingredient.model';
import { IngredientDetailsComponent } from '../ingredient-details/ingredient-details.component';
import { BaseListComponent, BaseListConfig } from '../../../common/components/base-list/base-list.component';

@Component({
  selector: 'app-ingredient-search',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatAutocompleteModule,
    MatProgressSpinnerModule,
    IngredientDetailsComponent,
    BaseListComponent,
  ],
  templateUrl: './ingredient-search.component.html',
  styleUrls: ['./ingredient-search.component.scss'],
})
export class IngredientSearchComponent implements OnInit, OnDestroy {
  searchControl = new FormControl('');
  filteredIngredients$: Observable<IngredientSearchResponseModel[]> | undefined;
  selectedIngredient: IngredientModel | null = null;
  isLoading = false;
  error: string | null = null;
  private destroy$ = new Subject<void>();

  listConfig: BaseListConfig = {
    title: 'Ingredient Search',
    subtitle: 'Search for ingredients to view their nutritional information',
    showSearch: true,
    showFilters: false,
    showPagination: false,
    maxWidth: '800px'
  };

  constructor(private recipeService: RecipeService) { }

  ngOnInit(): void {
    this.filteredIngredients$ = this.searchControl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap((term) => {
        if (term && typeof term === 'string' && term.length > 2) {
          this.isLoading = true;
          this.error = null;
          const retObs = this.recipeService.searchIngredients(term).pipe(
            catchError((error) => {
              console.error('Error searching ingredients:', error);
              this.error = 'Failed to search ingredients. Please try again.';
              this.isLoading = false;
              return of([]);
            })
          );
          retObs.subscribe((data) => {
            this.isLoading = false;
          });
          return retObs;
        } else {
          return of([]);
        }
      }),
      tap(() => (this.isLoading = false)),
      takeUntil(this.destroy$)
    );
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  displayFn(ingredient: IngredientSearchResponseModel): string {
    return ingredient && ingredient.name ? ingredient.name : '';
  }

  onIngredientSelected(event: MatAutocompleteSelectedEvent): void {
    const selected: IngredientSearchResponseModel = event.option.value;
    this.isLoading = true;
    this.error = null;
    this.recipeService
      .getIngredientDetails(selected.id)
      .pipe(
        takeUntil(this.destroy$),
        catchError((error) => {
          console.error('Error loading ingredient details:', error);
          this.error = 'Failed to load ingredient details.';
          this.isLoading = false;
          return of(null);
        })
      )
      .subscribe((details) => {
        this.selectedIngredient = details;
        this.isLoading = false;
      });
  }
}
