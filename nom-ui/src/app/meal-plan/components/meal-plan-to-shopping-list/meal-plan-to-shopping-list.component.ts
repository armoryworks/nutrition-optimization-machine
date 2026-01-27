import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil, finalize, forkJoin } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwCheckboxComponent,
  AmwInlineLoadingComponent,
  AmwIconComponent,
  loading,
} from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { ShoppingService } from '../../../shopping/services/shopping.service';
import { RecipeService } from '../../../recipe/services/recipe.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { IngredientModel } from '../../../recipe/models/ingredient.model';
import {
  ShoppingListCreateRequestModel,
  ShoppingListItemCreateRequestModel
} from '../../../shopping/models/shopping.interfaces';
import { NotificationService } from '../../../utilities/services/notification.service';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

interface ConsolidatedIngredient {
  name: string;
  quantity: number;
  unit: string;
  recipeNames: string[];
  isSelected: boolean;
}

@Component({
  selector: 'nom-meal-plan-to-shopping-list',
  standalone: true,
  imports: [
    AmwCardComponent,
    AmwButtonComponent,
    AmwCheckboxComponent,
    AmwInlineLoadingComponent,
    AmwIconComponent,
  ],
  templateUrl: './meal-plan-to-shopping-list.component.html',
  styleUrl: './meal-plan-to-shopping-list.component.scss',
})
export class MealPlanToShoppingListComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private mealPlanService = inject(MealPlanService);
  private shoppingService = inject(ShoppingService);
  private recipeService = inject(RecipeService);
  private notificationService = inject(NotificationService);
  private userInfoService = inject(UserInfoService);

  // Signals
  mealPlanId = signal<number>(0);
  mealPlans = signal<MealPlanResponseModel[]>([]);
  ingredients = signal<ConsolidatedIngredient[]>([]);
  isLoading = signal(true);
  isCreating = signal(false);
  error = signal<string | null>(null);

  // Computed
  selectedCount = computed(() => this.ingredients().filter(i => i.isSelected).length);
  totalCount = computed(() => this.ingredients().length);
  allSelected = computed(() => {
    const items = this.ingredients();
    return items.length > 0 && items.every(i => i.isSelected);
  });

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.mealPlanId.set(+id);
        this.loadMealPlanIngredients();
      } else {
        // Load all meal plans for the week
        this.loadAllMealPlans();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadMealPlanIngredients(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService
      .getMealPlan(this.mealPlanId())
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (mealPlan) => {
          this.mealPlans.set([mealPlan]);
          if (mealPlan.recipeId) {
            this.loadRecipeIngredients([mealPlan.recipeId], [mealPlan.recipeName || '']);
          } else {
            this.ingredients.set([]);
          }
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
          console.error('Error loading meal plan:', err);
        },
      });
  }

  private loadAllMealPlans(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService
      .getMealPlans()
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (plans) => {
          // Filter to current week
          const weekPlans = this.filterCurrentWeekPlans(plans);
          this.mealPlans.set(weekPlans);

          const recipeIds = weekPlans
            .filter(p => p.recipeId)
            .map(p => p.recipeId!);
          const recipeNames = weekPlans
            .filter(p => p.recipeId)
            .map(p => p.recipeName || '');

          if (recipeIds.length > 0) {
            this.loadRecipeIngredients(recipeIds, recipeNames);
          } else {
            this.ingredients.set([]);
          }
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
          console.error('Error loading meal plans:', err);
        },
      });
  }

  private filterCurrentWeekPlans(plans: MealPlanResponseModel[]): MealPlanResponseModel[] {
    const now = new Date();
    const startOfWeek = new Date(now);
    startOfWeek.setDate(now.getDate() - now.getDay());
    startOfWeek.setHours(0, 0, 0, 0);

    const endOfWeek = new Date(startOfWeek);
    endOfWeek.setDate(startOfWeek.getDate() + 7);

    return plans.filter(plan => {
      const planDate = new Date(plan.date);
      return planDate >= startOfWeek && planDate < endOfWeek;
    });
  }

  private loadRecipeIngredients(recipeIds: number[], recipeNames: string[]): void {
    const recipeRequests = recipeIds.map(id => this.recipeService.getRecipe(id));

    forkJoin(recipeRequests)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (recipes) => {
          const consolidated = this.consolidateIngredients(recipes, recipeNames);
          this.ingredients.set(consolidated);
        },
        error: (err) => {
          console.error('Error loading recipe ingredients:', err);
          this.ingredients.set([]);
        },
      });
  }

  private consolidateIngredients(recipes: any[], recipeNames: string[]): ConsolidatedIngredient[] {
    const ingredientMap = new Map<string, ConsolidatedIngredient>();

    recipes.forEach((recipe, index) => {
      if (!recipe.ingredients) return;

      recipe.ingredients.forEach((ingredient: IngredientModel) => {
        // Note: IngredientModel is master data and doesn't contain recipe-specific quantities
        // In a full implementation, quantities would come from RecipeIngredient join table
        // For now, using default values
        const key = ingredient.name.toLowerCase();

        if (ingredientMap.has(key)) {
          const existing = ingredientMap.get(key)!;
          existing.quantity += 1; // Default increment
          if (!existing.recipeNames.includes(recipeNames[index])) {
            existing.recipeNames.push(recipeNames[index]);
          }
        } else {
          ingredientMap.set(key, {
            name: ingredient.name,
            quantity: 1, // Default quantity
            unit: 'serving', // Default unit
            recipeNames: [recipeNames[index]],
            isSelected: true,
          });
        }
      });
    });

    return Array.from(ingredientMap.values()).sort((a, b) => a.name.localeCompare(b.name));
  }

  onToggleIngredient(index: number, checked: boolean | null): void {
    if (checked === null) return;
    const items = [...this.ingredients()];
    items[index].isSelected = checked;
    this.ingredients.set(items);
  }

  onToggleAll(checked: boolean | null): void {
    if (checked === null) return;
    const items = this.ingredients().map(i => ({ ...i, isSelected: checked }));
    this.ingredients.set(items);
  }

  onCreateShoppingList(): void {
    const selected = this.ingredients().filter(i => i.isSelected);
    if (selected.length === 0) {
      this.notificationService.error('Please select at least one ingredient');
      return;
    }

    this.isCreating.set(true);
    this.error.set(null);

    const listName = this.mealPlanId()
      ? `Meal Plan - ${this.mealPlans()[0]?.recipeName}`
      : `Meal Plan - Week of ${new Date().toLocaleDateString()}`;

    const createRequest: ShoppingListCreateRequestModel = {
      householdId: this.getCurrentHouseholdId(),
      name: listName,
      description: `Generated from meal plan(s)`,
    };

    this.shoppingService
      .createShoppingList(createRequest)
      .pipe(
        loading('Creating shopping list...'),
        takeUntil(this.destroy$),
        finalize(() => this.isCreating.set(false))
      )
      .subscribe({
        next: (response) => {
          // Add items to the shopping list
          this.addItemsToList(response.id, selected);
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.SHOPPING.SAVE_FAILED);
          this.notificationService.error(ERROR_MESSAGES.SHOPPING.SAVE_FAILED);
          console.error('Error creating shopping list:', err);
        },
      });
  }

  private addItemsToList(listId: number, items: ConsolidatedIngredient[]): void {
    const itemRequests = items.map(ingredient => {
      const request: ShoppingListItemCreateRequestModel = {
        shoppingListId: listId,
        name: ingredient.name,
        quantity: ingredient.quantity,
        measurementUnit: ingredient.unit,
        notes: `From: ${ingredient.recipeNames.join(', ')}`,
      };
      return this.shoppingService.addItem(request);
    });

    forkJoin(itemRequests)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.notificationService.success('Shopping list created successfully');
          this.router.navigate(['/shopping', listId]);
        },
        error: (err) => {
          this.notificationService.error(ERROR_MESSAGES.SHOPPING.ITEM_ADD_FAILED);
          console.error('Error adding items:', err);
          // Still navigate to the list even if some items failed
          this.router.navigate(['/shopping', listId]);
        },
      });
  }

  onBack(): void {
    if (this.mealPlanId()) {
      this.router.navigate(['/meal-plan', this.mealPlanId()]);
    } else {
      this.router.navigate(['/meal-plan']);
    }
  }

  onRetry(): void {
    if (this.mealPlanId()) {
      this.loadMealPlanIngredients();
    } else {
      this.loadAllMealPlans();
    }
  }

  private getCurrentHouseholdId(): number {
    return this.userInfoService.getHouseholdId();
  }
}
