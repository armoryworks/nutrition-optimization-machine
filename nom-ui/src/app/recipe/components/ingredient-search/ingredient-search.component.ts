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
import { IngredientDetailsComponent } from '../ingredient-details/ingredient-details.component'; // Import details component

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
    IngredientDetailsComponent, // Add details component to imports
  ],
  templateUrl: './ingredient-search.component.html',
  styleUrls: ['./ingredient-search.component.scss'],
})
export class IngredientSearchComponent implements OnInit, OnDestroy {
  searchControl = new FormControl('');
  filteredIngredients$: Observable<IngredientSearchResponseModel[]> | undefined;
  selectedIngredient: IngredientModel | null = null;
  isLoading = false;
  private destroy$ = new Subject<void>();

  constructor(private recipeService: RecipeService) {}

  ngOnInit(): void {
    this.filteredIngredients$ = this.searchControl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap((term) => {
        if (term && typeof term === 'string' && term.length > 2) {
          this.isLoading = true;
          const retObs = this.recipeService.searchIngredients(term).pipe(
            catchError(() => {
              this.isLoading = false;
              return of([]); // On error, return an empty array
            })
          );
          retObs.subscribe((data) => { 
            this.isLoading = false;
            console.info('made it here');
          });
          return retObs;
        } else {
          return of([]); // If term is too short, return empty array
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
    this.recipeService
      .getIngredientDetails(selected.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe((details) => {
        this.selectedIngredient = details;
        this.isLoading = false;
      });
  }
}
