import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwInputComponent,
  AmwInlineLoadingComponent,
  AmwIconComponent,
} from 'angular-material-wrap';

import { RecipeService } from '../../../recipe/services/recipe.service';
import { MealPlanService } from '../../services/meal-plan.service';
import { RecipeModel } from '../../../recipe/models/recipe.model';
import { MealPlanCreateRequestModel } from '../../models/meal-plan-create-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-meal-plan-recipe-selection',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwCardComponent,
    AmwInputComponent,
    AmwInlineLoadingComponent,
  ],
  templateUrl: './meal-plan-recipe-selection.component.html',
  styleUrl: './meal-plan-recipe-selection.component.scss',
})
export class MealPlanRecipeSelectionComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private recipeService = inject(RecipeService);
  private mealPlanService = inject(MealPlanService);
  private notificationService = inject(NotificationService);
  private userInfoService = inject(UserInfoService);
  private fb = inject(FormBuilder);

  // Signals
  recipes = signal<RecipeModel[]>([]);
  filteredRecipes = signal<RecipeModel[]>([]);
  selectedRecipe = signal<RecipeModel | null>(null);
  isLoading = signal(true);
  isAdding = signal(false);
  error = signal<string | null>(null);
  searchTerm = signal('');

  // Query params
  date = signal<Date>(new Date());
  mealTypeId = signal<number>(0);
  mealType = signal<string>('');

  // Computed
  hasRecipes = computed(() => this.filteredRecipes().length > 0);

  // Form
  mealDetailsForm: FormGroup;

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  constructor() {
    this.mealDetailsForm = this.fb.group({
      date: [new Date(), Validators.required],
      notes: [''],
    });
  }

  ngOnInit(): void {
    // Get query params
    this.route.queryParams.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      if (params['date']) {
        const date = new Date(params['date']);
        this.date.set(date);
        this.mealDetailsForm.patchValue({ date });
      }
      if (params['mealType']) {
        this.mealType.set(params['mealType']);
        this.mealTypeId.set(this.getMealTypeId(params['mealType']));
      }
    });

    this.loadRecipes();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadRecipes(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.recipeService
      .getRecipes()
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (recipes) => {
          this.recipes.set(recipes);
          this.filteredRecipes.set(recipes);
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.RECIPE.LOAD_FAILED);
          console.error('Error loading recipes:', err);
        },
      });
  }

  onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    const term = input.value.toLowerCase();
    this.searchTerm.set(term);

    if (!term) {
      this.filteredRecipes.set(this.recipes());
      return;
    }

    const filtered = this.recipes().filter(
      (recipe) =>
        recipe.name.toLowerCase().includes(term) ||
        recipe.description?.toLowerCase().includes(term) ||
        recipe.authorName?.toLowerCase().includes(term)
    );
    this.filteredRecipes.set(filtered);
  }

  onSelectRecipe(recipe: RecipeModel): void {
    this.selectedRecipe.set(recipe);
  }

  onClearSelection(): void {
    this.selectedRecipe.set(null);
  }

  onAddToMealPlan(): void {
    if (this.mealDetailsForm.invalid || !this.selectedRecipe()) return;

    const recipe = this.selectedRecipe()!;
    const date = this.mealDetailsForm.value.date;
    const notes = this.mealDetailsForm.value.notes;

    this.isAdding.set(true);
    this.error.set(null);

    const request: MealPlanCreateRequestModel = {
      householdId: this.getCurrentHouseholdId(),
      date: date,
      mealTypeId: this.mealTypeId(),
      recipeId: recipe.id,
      title: recipe.name,
      notes: notes || undefined,
    };

    this.mealPlanService
      .createMealPlan(request)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isAdding.set(false))
      )
      .subscribe({
        next: () => {
          this.notificationService.success(`Added "${recipe.name}" to meal plan`);
          this.router.navigate(['/meal-plan']);
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.MEAL_PLAN.SAVE_FAILED);
          this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.SAVE_FAILED);
          console.error('Error adding recipe:', err);
        },
      });
  }

  onBack(): void {
    this.router.navigate(['/meal-plan']);
  }

  onRetry(): void {
    this.loadRecipes();
  }

  private getMealTypeId(mealType: string): number {
    switch (mealType.toLowerCase()) {
      case 'breakfast': return 1;
      case 'lunch': return 2;
      case 'dinner': return 3;
      case 'snack': return 4;
      default: return 1;
    }
  }

  getIngredientCount(recipe: RecipeModel): number {
    return recipe.ingredients?.length || 0;
  }

  getRatingDisplay(recipe: RecipeModel): string {
    if (!recipe.rating || recipe.rating === 0) return 'No ratings';
    return `${recipe.rating.toFixed(1)} ★ (${recipe.ratingCount || 0} ratings)`;
  }

  private getCurrentHouseholdId(): number {
    return this.userInfoService.getHouseholdId();
  }
}
