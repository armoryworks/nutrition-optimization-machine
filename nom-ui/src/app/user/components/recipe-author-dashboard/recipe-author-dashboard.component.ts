// File: nom-ui/src/app/user/components/recipe-author-dashboard/recipe-author-dashboard.component.ts

import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { AsyncPipe } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Observable, of, catchError, combineLatest } from 'rxjs';
import { map } from 'rxjs/operators';

import { AmwButtonComponent, AmwInputComponent, AmwSelectComponent, AmwIconButtonComponent, AmwTooltipDirective, AmwIconComponent, AmwProgressSpinnerComponent, AmwMenuComponent, AmwMenuItemComponent, AmwMenuTriggerForDirective, AmwDialogService } from 'angular-material-wrap';

import { RecipeService } from '../../../recipe/services/recipe.service';
import { CurationService } from '../../../curation/services/curation.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { RecipeDashboardItemModel } from '../../../recipe/models/recipe-dashboard-item.model';
import { RecipeModel } from '../../../recipe/models/recipe.model';
import { SubmitForCurationRequestModel } from '../../../curation/models/submit-for-curation-request.model';
import { canSubmitForCuration, CurationStatus, isPendingCuration } from '../../../recipe/models/curation-status.enum';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

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
    AsyncPipe,
    RouterLink,
    FormsModule,
    AmwButtonComponent,
    AmwInputComponent,
    AmwSelectComponent,
    AmwIconButtonComponent,
    AmwTooltipDirective,
    AmwIconComponent,
    AmwProgressSpinnerComponent,
    AmwMenuComponent,
    AmwMenuItemComponent,
    AmwMenuTriggerForDirective,
  ],
  templateUrl: './recipe-author-dashboard.component.html',
  styleUrls: ['./recipe-author-dashboard.component.scss']
})
export class RecipeAuthorDashboardComponent implements OnInit {
  private recipeService = inject(RecipeService);
  private curationService = inject(CurationService);
  private notificationService = inject(NotificationService);
  private router = inject(Router);
  private dialogService = inject(AmwDialogService);

  recipes$!: Observable<RecipeDashboardItemModel[]>;
  ingredients$!: Observable<RecipeDashboardItemModel[]>;
  filteredRecipes$!: Observable<RecipeDashboardItemModel[]>;
  filteredIngredients$!: Observable<RecipeDashboardItemModel[]>;
  recipesCount$!: Observable<number>;
  ingredientsCount$!: Observable<number>;
  pendingCurationCount$!: Observable<number>;
  error: string | null = null;
  submittingItems = new Set<number>();

  // Filter signals for reactive filtering
  private recipeSearchSignal = signal('');
  private ingredientSearchSignal = signal('');
  private recipeStatusSignal = signal('');
  private ingredientStatusSignal = signal('');

  // Properties for ngModel binding (keep for backward compatibility)
  recipeSearchTerm = '';
  ingredientSearchTerm = '';
  recipeStatusFilter = '';
  ingredientStatusFilter = '';

  // Status options for AMW select
  statusOptions = [
    { value: '', label: 'All Statuses' },
    { value: 'DRAFT', label: 'Draft' },
    { value: 'NONCURATED', label: 'Non-Curated' },
    { value: 'PENDINGCURATION', label: 'Pending Curation' },
    { value: 'CURATED', label: 'Curated' },
    { value: 'REQUIRESREVISION', label: 'Requires Revision' },
    { value: 'REJECTED', label: 'Rejected' }
  ];



  ngOnInit(): void {
    this.recipes$ = this.recipeService.getRecipes().pipe(
      map(recipes => {
        const mappedRecipes = recipes.map(recipe => ({
          ...recipe,
          curationStatus: recipe.curationStatus || 'draft'
        }));

        // Debug logging to see actual status values
        console.log('Recipe statuses:', mappedRecipes.map(r => ({ name: r.name, status: r.curationStatus })));

        return mappedRecipes;
      }),
      catchError((err: Error | string | unknown) => {
        console.error('Error fetching recipes:', err);
        this.error = ERROR_MESSAGES.RECIPE.LOAD_FAILED;
        return of([]);
      })
    );

    this.ingredients$ = this.recipeService.getMyIngredients().pipe(
      map(ingredients => {
        const mappedIngredients = ingredients.map(ingredient => ({
          ...ingredient,
          curationStatus: ingredient.curationStatus || 'NonCurated'
        }));

        // Debug logging to see actual status values
        console.log('Ingredient statuses:', mappedIngredients.map(i => ({ name: i.name, status: i.curationStatus })));

        return mappedIngredients;
      }),
      catchError((err: Error | string | unknown) => {
        console.error('Error fetching ingredients:', err);
        this.error = ERROR_MESSAGES.INGREDIENT.LOAD_FAILED;
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

    // Set up filtered observables with signal-based filtering
    const recipeSearch$ = combineLatest([this.recipes$]).pipe(
      map(([recipes]) => this.filterItems(recipes, this.recipeSearchSignal(), this.recipeStatusSignal()))
    );

    const ingredientSearch$ = combineLatest([this.ingredients$]).pipe(
      map(([ingredients]) => this.filterItems(ingredients, this.ingredientSearchSignal(), this.ingredientStatusSignal()))
    );

    // Initially set filtered observables to base observables
    this.filteredRecipes$ = this.recipes$.pipe(
      map(recipes => this.filterItems(recipes, this.recipeSearchSignal(), this.recipeStatusSignal()))
    );

    this.filteredIngredients$ = this.ingredients$.pipe(
      map(ingredients => this.filterItems(ingredients, this.ingredientSearchSignal(), this.ingredientStatusSignal()))
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
            this.error = ERROR_MESSAGES.RECIPE.LOAD_FAILED;
            return of([]);
          })
        );
      },
      error: (error: Error | string | unknown) => {
        console.error('Error submitting recipe for curation:', error);
        this.notificationService.error(ERROR_MESSAGES.CURATION.SUBMIT_FAILED);
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
            this.error = ERROR_MESSAGES.INGREDIENT.LOAD_FAILED;
            return of([]);
          })
        );
      },
      error: (error: Error | string | unknown) => {
        console.error('Error submitting ingredient for curation:', error);
        this.notificationService.error(ERROR_MESSAGES.CURATION.SUBMIT_FAILED);
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
    this.dialogService.confirm(
      'Are you sure you want to delete this recipe? This action cannot be undone.',
      'Delete Recipe'
    ).subscribe(confirmed => {
      if (confirmed) {
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
                this.error = ERROR_MESSAGES.RECIPE.LOAD_FAILED;
                return of([]);
              })
            );
          },
          error: (error: Error | string | unknown) => {
            console.error('Error deleting recipe:', error);
            this.notificationService.error(ERROR_MESSAGES.RECIPE.DELETE_FAILED);
          }
        });
      }
    });
  }

  onDeleteIngredient(ingredientId: number): void {
    this.dialogService.confirm(
      'Are you sure you want to delete this ingredient? This action cannot be undone.',
      'Delete Ingredient'
    ).subscribe(confirmed => {
      if (confirmed) {
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
                this.error = ERROR_MESSAGES.INGREDIENT.LOAD_FAILED;
                return of([]);
              })
            );
          },
          error: (error: Error | string | unknown) => {
            console.error('Error deleting ingredient:', error);
            this.notificationService.error(ERROR_MESSAGES.INGREDIENT.DELETE_FAILED);
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

      // Status filter with multiple format handling
      const matchesStatus = !statusFilter || this.statusMatches(item.curationStatus, statusFilter);

      return matchesName && matchesStatus;
    });
  }

  // Helper method to match different curation status formats
  private statusMatches(itemStatus: string | undefined, filterStatus: string): boolean {
    if (!itemStatus) return false;

    // Normalize both statuses for comparison
    const normalizedItemStatus = this.normalizeStatus(itemStatus);
    const normalizedFilterStatus = this.normalizeStatus(filterStatus);

    return normalizedItemStatus === normalizedFilterStatus;
  }

  // Normalize status values to a consistent format
  private normalizeStatus(status: string): string {
    const normalized = status.toLowerCase().replace(/[-_\s]/g, '');

    // Map various formats to consistent values
    switch (normalized) {
      case 'draft':
        return 'draft';
      case 'noncurated':
      case 'non-curated':
        return 'noncurated';
      case 'pendingcuration':
      case 'pending-curation':
      case 'pendingcura':
        return 'pendingcuration';
      case 'curated':
        return 'curated';
      case 'requiresrevision':
      case 'requires-revision':
        return 'requiresrevision';
      case 'rejected':
        return 'rejected';
      default:
        return normalized;
    }
  }

  // Filter event handlers
  onRecipeSearch(): void {
    this.recipeSearchSignal.set(this.recipeSearchTerm);
    this.updateFilteredRecipes();
  }

  onIngredientSearch(): void {
    this.ingredientSearchSignal.set(this.ingredientSearchTerm);
    this.updateFilteredIngredients();
  }

  onRecipeStatusFilter(): void {
    this.recipeStatusSignal.set(this.recipeStatusFilter);
    this.updateFilteredRecipes();
  }

  onIngredientStatusFilter(): void {
    this.ingredientStatusSignal.set(this.ingredientStatusFilter);
    this.updateFilteredIngredients();
  }

  private updateFilteredRecipes(): void {
    this.filteredRecipes$ = this.recipes$.pipe(
      map(recipes => this.filterItems(recipes, this.recipeSearchSignal(), this.recipeStatusSignal()))
    );
  }

  private updateFilteredIngredients(): void {
    this.filteredIngredients$ = this.ingredients$.pipe(
      map(ingredients => this.filterItems(ingredients, this.ingredientSearchSignal(), this.ingredientStatusSignal()))
    );
  }
}