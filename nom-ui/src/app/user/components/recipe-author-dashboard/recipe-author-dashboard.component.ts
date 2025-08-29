// File: nom-ui/src/app/user/components/recipe-author-dashboard/recipe-author-dashboard.component.ts

import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Observable, of, catchError, combineLatest, BehaviorSubject } from 'rxjs';
import { map } from 'rxjs/operators';
import { MatDialog } from '@angular/material/dialog';

import { RecipeService } from '../../../recipe/services/recipe.service';
import { CurationService } from '../../../curation/services/curation.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { RecipeDashboardItemModel } from '../../../recipe/models/recipe-dashboard-item.model';
import { RecipeModel } from '../../../recipe/models/recipe.model';
import { SubmitForCurationRequestModel } from '../../../curation/models/submit-for-curation-request.model';
import { canSubmitForCuration, CurationStatus, isPendingCuration } from '../../../recipe/models/curation-status.enum';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';

// Helper function to convert curation status ID to status string
function getCurationStatusFromId(statusId: number): string {
  switch (statusId) {
    case CurationStatus.NonCurated:
      return 'NonCurated';
    case CurationStatus.PendingCuration:
      return 'PendingCuration';
    case CurationStatus.RequiresRevision:
      return 'RequiresRevision';
    case CurationStatus.Curated:
      return 'Curated';
    case CurationStatus.Rejected:
      return 'Rejected';
    default:
      return 'NonCurated';
  }
}

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
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatMenuModule,
    MatTooltipModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule
  ],
  templateUrl: './recipe-author-dashboard.component.html',
  styleUrls: ['./recipe-author-dashboard.component.scss']
})
export class RecipeAuthorDashboardComponent implements OnInit {
  private recipeService = inject(RecipeService);
  private curationService = inject(CurationService);
  private notificationService = inject(NotificationService);
  private snackBar = inject(MatSnackBar);
  private router = inject(Router);
  private dialog = inject(MatDialog);

  recipes$!: Observable<RecipeDashboardItemModel[]>;
  ingredients$!: Observable<RecipeDashboardItemModel[]>;
  filteredRecipes$!: Observable<RecipeDashboardItemModel[]>;
  filteredIngredients$!: Observable<RecipeDashboardItemModel[]>;
  recipesCount$!: Observable<number>;
  ingredientsCount$!: Observable<number>;
  pendingCurationCount$!: Observable<number>;
  error: string | null = null;
  submittingItems = new Set<number>();

  // Filter properties
  recipeSearchTerm = '';
  ingredientSearchTerm = '';
  recipeStatusFilter = '';
  ingredientStatusFilter = '';

  // Filter subjects for reactive filtering
  private recipeSearchSubject = new BehaviorSubject<string>('');
  private ingredientSearchSubject = new BehaviorSubject<string>('');
  private recipeStatusSubject = new BehaviorSubject<string>('');
  private ingredientStatusSubject = new BehaviorSubject<string>('');



  ngOnInit(): void {
    this.recipes$ = this.recipeService.getRecipes().pipe(
      map(recipes => recipes.map(recipe => ({
        ...recipe,
        curationStatus: recipe.curationStatus || 'draft'
      }))),
      catchError((err: Error | string | unknown) => {
        console.error('Error fetching recipes:', err);
        this.error = 'Could not load your recipes. Please try again later.';
        return of([]);
      })
    );

    this.ingredients$ = this.recipeService.getMyIngredients().pipe(
      map(ingredients => ingredients.map(ingredient => ({
        ...ingredient,
        curationStatus: ingredient.curationStatus || 'NonCurated'
      }))),
      catchError((err: Error | string | unknown) => {
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

    this.pendingCurationCount$ = combineLatest([this.recipes$, this.ingredients$]).pipe(
      map(([recipes, ingredients]) => {
        const pendingRecipes = recipes.filter(r => isPendingCuration(r.curationStatus));
        const pendingIngredients = ingredients.filter(i => isPendingCuration(i.curationStatus));
        return pendingRecipes.length + pendingIngredients.length;
      })
    );

    // Set up filtered observables
    this.filteredRecipes$ = combineLatest([
      this.recipes$,
      this.recipeSearchSubject,
      this.recipeStatusSubject
    ]).pipe(
      map(([recipes, searchTerm, statusFilter]) => this.filterItems(recipes, searchTerm, statusFilter))
    );

    this.filteredIngredients$ = combineLatest([
      this.ingredients$,
      this.ingredientSearchSubject,
      this.ingredientStatusSubject
    ]).pipe(
      map(([ingredients, searchTerm, statusFilter]) => this.filterItems(ingredients, searchTerm, statusFilter))
    );
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
          catchError((err: Error | string | unknown) => {
            console.error('Error fetching recipes:', err);
            this.error = 'Could not load your recipes. Please try again later.';
            return of([]);
          })
        );
      },
      error: (error: Error | string | unknown) => {
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
            curationStatus: ingredient.curationStatus || 'NonCurated'
          }))),
          catchError((err: Error | string | unknown) => {
            console.error('Error fetching ingredients:', err);
            this.error = 'Could not load your ingredients. Please try again later.';
            return of([]);
          })
        );
      },
      error: (error: Error | string | unknown) => {
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
              catchError((err: Error | string | unknown) => {
                console.error('Error fetching recipes:', err);
                this.error = 'Could not load your recipes. Please try again later.';
                return of([]);
              })
            );
          },
          error: (error: Error | string | unknown) => {
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
                curationStatus: ingredient.curationStatus || 'NonCurated'
              }))),
              catchError((err: Error | string | unknown) => {
                console.error('Error fetching ingredients:', err);
                this.error = 'Could not load your ingredients. Please try again later.';
                return of([]);
              })
            );
          },
          error: (error: Error | string | unknown) => {
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

  // Filter methods
  private filterItems(items: RecipeDashboardItemModel[], searchTerm: string, statusFilter: string): RecipeDashboardItemModel[] {
    return items.filter(item => {
      // Name filter (case-insensitive contains)
      const matchesName = !searchTerm ||
        item.name.toLowerCase().includes(searchTerm.toLowerCase());

      // Status filter
      const matchesStatus = !statusFilter ||
        item.curationStatus?.toUpperCase() === statusFilter.toUpperCase();

      return matchesName && matchesStatus;
    });
  }

  // Filter event handlers
  onRecipeSearch(): void {
    this.recipeSearchSubject.next(this.recipeSearchTerm);
  }

  onIngredientSearch(): void {
    this.ingredientSearchSubject.next(this.ingredientSearchTerm);
  }

  onRecipeStatusFilter(): void {
    this.recipeStatusSubject.next(this.recipeStatusFilter);
  }

  onIngredientStatusFilter(): void {
    this.ingredientStatusSubject.next(this.ingredientStatusFilter);
  }
}