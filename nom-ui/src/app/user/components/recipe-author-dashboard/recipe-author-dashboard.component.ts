// File: nom-ui/src/app/user/components/recipe-author-dashboard/recipe-author-dashboard.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable, of, catchError } from 'rxjs';
import { map } from 'rxjs/operators';
import { MatDialog } from '@angular/material/dialog';

import { RecipeService } from '../../../recipe/services/recipe.service';
import { CurationService } from '../../../curation/services/curation.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { RecipeDashboardItemModel } from '../../../recipe/models/recipe-dashboard-item.model';
import { RecipeModel } from '../../../recipe/models/recipe.model';
import { SubmitForCurationRequestModel } from '../../../curation/models/submit-for-curation-request.model';
import { CurationStatus, canSubmitForCuration } from '../../../recipe/models/curation-status.enum';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';

interface MenuItem {
  icon: string;
  label: string;
  action: () => void;
  routerLink?: (string | number)[];
  disabled?: boolean;
}

@Component({
  selector: 'nom-recipe-author-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatMenuModule,
    MatTooltipModule
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
    private snackBar: MatSnackBar,
    private router: Router,
    private dialog: MatDialog
  ) { }

  ngOnInit(): void {
    this.recipes$ = this.recipeService.getRecipes().pipe(
      map(recipes => recipes.map(recipe => ({
        ...recipe,
        curationStatus: recipe.curationStatus || 'draft'
      }))),
      catchError((err: any) => {
        console.error('Error fetching recipes:', err);
        this.error = 'Could not load your recipes. Please try again later.';
        return of([]);
      })
    );

    this.ingredients$ = this.recipeService.getMyIngredients().pipe(
      map(ingredients => ingredients.map(ingredient => ({
        ...ingredient,
        curationStatus: ingredient.curationStatus || 'draft'
      }))),
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
      map(recipes => recipes.filter(r => canSubmitForCuration(r.curationStatus)).length)
    );

    // Debug: Log the recipes data
    this.recipes$.subscribe(recipes => {
      console.log('Dashboard - Recipes loaded:', recipes);
      recipes.forEach(recipe => {
        console.log(`Recipe ${recipe.id} (${recipe.name}): status="${recipe.curationStatus}", canSubmit=${canSubmitForCuration(recipe.curationStatus)}`);
      });
    });
  }

  submitRecipeForCuration(recipeId: number): void {
    this.submittingItems.add(recipeId);
    const request: SubmitForCurationRequestModel = {
      entityId: recipeId,
      entityType: 'Recipe'
    };

    this.curationService.submitForCuration(request).subscribe({
      next: () => {
        this.notificationService.success('Recipe submitted for curation successfully!');
        this.submittingItems.delete(recipeId);
        // Refresh the recipes list
        this.recipes$ = this.recipeService.getRecipes().pipe(
          map(recipes => recipes.map(recipe => ({
            ...recipe,
            curationStatus: recipe.curationStatus || 'draft'
          }))),
          catchError((err: any) => {
            console.error('Error fetching recipes:', err);
            this.error = 'Could not load your recipes. Please try again later.';
            return of([]);
          })
        );
      },
      error: (error: any) => {
        console.error('Error submitting recipe for curation:', error);
        this.notificationService.error('Failed to submit recipe for curation. Please try again.');
        this.submittingItems.delete(recipeId);
      }
    });
  }

  submitIngredientForCuration(ingredientId: number): void {
    this.submittingItems.add(ingredientId);
    const request: SubmitForCurationRequestModel = {
      entityId: ingredientId,
      entityType: 'Ingredient'
    };

    this.curationService.submitForCuration(request).subscribe({
      next: () => {
        this.notificationService.success('Ingredient submitted for curation successfully!');
        this.submittingItems.delete(ingredientId);
        // Refresh the ingredients list
        this.ingredients$ = this.recipeService.getMyIngredients().pipe(
          map(ingredients => ingredients.map(ingredient => ({
            ...ingredient,
            curationStatus: ingredient.curationStatus || 'draft'
          }))),
          catchError((err: any) => {
            console.error('Error fetching ingredients:', err);
            this.error = 'Could not load your ingredients. Please try again later.';
            return of([]);
          })
        );
      },
      error: (error: any) => {
        console.error('Error submitting ingredient for curation:', error);
        this.notificationService.error('Failed to submit ingredient for curation. Please try again.');
        this.submittingItems.delete(ingredientId);
      }
    });
  }

  isSubmitting(itemId: number): boolean {
    return this.submittingItems.has(itemId);
  }

  onEditRecipe(recipeId: number): void {
    this.router.navigate(['/recipes', recipeId, 'edit'], {
      queryParams: {
        returnTo: '/user/dashboard',
        returnToTitle: 'Dashboard'
      }
    });
  }

  onEditIngredient(ingredientId: number): void {
    this.router.navigate(['/recipes/ingredients', ingredientId, 'edit'], {
      queryParams: {
        returnTo: '/user/dashboard',
        returnToTitle: 'Dashboard'
      }
    });
  }

  onDeleteRecipe(recipeId: number): void {
    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        title: 'Delete Recipe',
        message: 'Are you sure you want to delete this recipe? This action cannot be undone.',
        confirmText: 'Delete',
        cancelText: 'Cancel',
        confirmColor: 'warn'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.recipeService.deleteRecipe(recipeId).subscribe({
          next: () => {
            this.notificationService.success('Recipe deleted successfully');
            // Refresh the recipes list
            this.recipes$ = this.recipeService.getMyRecipes().pipe(
              map((recipes: RecipeModel[]) => recipes.map((recipe: RecipeModel) => ({
                ...recipe,
                curationStatus: recipe.curationStatus || 'draft'
              }))),
              catchError((err: any) => {
                console.error('Error fetching recipes:', err);
                this.error = 'Could not load your recipes. Please try again later.';
                return of([]);
              })
            );
          },
          error: (error: any) => {
            console.error('Error deleting recipe:', error);
            this.notificationService.error('Failed to delete recipe. Please try again.');
          }
        });
      }
    });
  }

  onDeleteIngredient(ingredientId: number): void {
    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        title: 'Delete Ingredient',
        message: 'Are you sure you want to delete this ingredient? This action cannot be undone.',
        confirmText: 'Delete',
        cancelText: 'Cancel',
        confirmColor: 'warn'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.recipeService.deleteIngredient(ingredientId).subscribe({
          next: () => {
            this.notificationService.success('Ingredient deleted successfully');
            // Refresh the ingredients list
            this.ingredients$ = this.recipeService.getMyIngredients().pipe(
              map(ingredients => ingredients.map(ingredient => ({
                ...ingredient,
                curationStatus: ingredient.curationStatus || 'draft'
              }))),
              catchError((err: any) => {
                console.error('Error fetching ingredients:', err);
                this.error = 'Could not load your ingredients. Please try again later.';
                return of([]);
              })
            );
          },
          error: (error: any) => {
            console.error('Error deleting ingredient:', error);
            this.notificationService.error('Failed to delete ingredient. Please try again.');
          }
        });
      }
    });
  }

  getRecipeMenuItems(recipe: RecipeDashboardItemModel): MenuItem[] {
    const items: MenuItem[] = [
      { icon: 'edit', label: 'Edit Recipe', action: () => this.onEditRecipe(recipe.id) }
    ];
    if (canSubmitForCuration(recipe.curationStatus)) {
      items.push({
        icon: 'send',
        label: 'Submit for Curation',
        action: () => this.submitRecipeForCuration(recipe.id),
        disabled: this.isSubmitting(recipe.id)
      });
    }
    items.push({ icon: 'delete', label: 'Delete Recipe', action: () => this.onDeleteRecipe(recipe.id) });
    return items;
  }

  getIngredientMenuItems(ingredient: RecipeDashboardItemModel): MenuItem[] {
    const items: MenuItem[] = [
      { icon: 'edit', label: 'Edit Ingredient', action: () => this.onEditIngredient(ingredient.id) }
    ];
    if (canSubmitForCuration(ingredient.curationStatus)) {
      items.push({
        icon: 'send',
        label: 'Submit for Curation',
        action: () => this.submitIngredientForCuration(ingredient.id),
        disabled: this.isSubmitting(ingredient.id)
      });
    }
    items.push({ icon: 'delete', label: 'Delete Ingredient', action: () => this.onDeleteIngredient(ingredient.id) });
    return items;
  }

  // Navigation methods for proper back navigation
  navigateToNewIngredient(): void {
    this.router.navigate(['/recipes/ingredients/new'], {
      queryParams: {
        returnTo: '/user/dashboard',
        returnToTitle: 'Dashboard'
      }
    });
  }

  navigateToNewRecipe(): void {
    this.router.navigate(['/recipes/new'], {
      queryParams: {
        returnTo: '/user/dashboard',
        returnToTitle: 'Dashboard'
      }
    });
  }
}