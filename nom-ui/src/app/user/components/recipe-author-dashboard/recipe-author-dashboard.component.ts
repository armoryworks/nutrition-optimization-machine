// File: nom-ui/src/app/user/components/recipe-author-dashboard/recipe-author-dashboard.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { RecipeService } from '../../../recipe/services/recipe.service';
import { CurationService } from '../../../curation/services/curation.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { RecipeDashboardItemModel } from '../../../recipe/models/recipe-dashboard-item.model';
import { SubmitForCurationRequestModel } from '../../../curation/models/submit-for-curation-request.model';

@Component({
  selector: 'app-recipe-author-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  templateUrl: './recipe-author-dashboard.component.html',
  styleUrls: ['./recipe-author-dashboard.component.scss']
})
export class RecipeAuthorDashboardComponent implements OnInit {
  recipes$!: Observable<RecipeDashboardItemModel[]>;
  ingredients$!: Observable<RecipeDashboardItemModel[]>;
  recipesCount$!: Observable<number>;
  ingredientsCount$!: Observable<number>;
  pendingCurationCount$!: Observable<number>;
  error: string | null = null;
  submittingItems: Set<number> = new Set();

  constructor(
    private recipeService: RecipeService,
    private curationService: CurationService,
    private notificationService: NotificationService,
    private snackBar: MatSnackBar
  ) { }

  ngOnInit(): void {
    this.recipes$ = this.recipeService.getMyRecipes().pipe(
      catchError((err: any) => {
        console.error('Error fetching recipes:', err);
        this.error = 'Could not load your recipes. Please try again later.';
        return of([]);
      })
    );

    this.ingredients$ = this.recipeService.getMyIngredients().pipe(
      catchError((err: any) => {
        console.error('Error fetching ingredients:', err);
        this.error = 'Could not load your ingredients. Please try again later.';
        return of([]);
      })
    );

    // Create observables for stats
    this.recipesCount$ = this.recipes$.pipe(
      map(recipes => recipes.length)
    );

    this.ingredientsCount$ = this.ingredients$.pipe(
      map(ingredients => ingredients.length)
    );

    this.pendingCurationCount$ = this.recipes$.pipe(
      map(recipes => recipes.filter(r => r.curationStatus === 'NonCurated').length)
    );
  }

  submitRecipeForCuration(recipeId: number): void {
    if (this.submittingItems.has(recipeId)) return;
    this.submittingItems.add(recipeId);
    const request: SubmitForCurationRequestModel = { entityId: recipeId, entityType: 'Recipe' };
    this.curationService.submitForCuration(request).subscribe({
      next: () => {
        this.notificationService.success('Recipe submitted for curation successfully');
        this.submittingItems.delete(recipeId);
        this.recipes$ = this.recipeService.getMyRecipes().pipe(
          catchError((err: any) => {
            console.error('Error fetching recipes:', err);
            this.error = 'Could not load your recipes. Please try again later.';
            return of([]);
          })
        );
      },
      error: (error: any) => {
        console.error('Error submitting recipe for curation:', error);
        this.notificationService.error('Failed to submit recipe for curation');
        this.submittingItems.delete(recipeId);
      }
    });
  }

  submitIngredientForCuration(ingredientId: number): void {
    if (this.submittingItems.has(ingredientId)) return;
    this.submittingItems.add(ingredientId);
    const request: SubmitForCurationRequestModel = { entityId: ingredientId, entityType: 'Ingredient' };
    this.curationService.submitForCuration(request).subscribe({
      next: () => {
        this.notificationService.success('Ingredient submitted for curation successfully');
        this.submittingItems.delete(ingredientId);
        // Refresh the ingredients list (when implemented)
      },
      error: (error: any) => {
        console.error('Error submitting ingredient for curation:', error);
        this.notificationService.error('Failed to submit ingredient for curation');
        this.submittingItems.delete(ingredientId);
      }
    });
  }

  canSubmitForCuration(status: string): boolean {
    return status === 'NonCurated' || status === 'Draft';
  }

  isSubmitting(itemId: number): boolean {
    return this.submittingItems.has(itemId);
  }
}