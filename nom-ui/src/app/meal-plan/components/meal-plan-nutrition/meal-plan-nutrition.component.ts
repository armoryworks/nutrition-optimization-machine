import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil, finalize, forkJoin } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwInlineLoadingComponent,
  AmwIconComponent,
} from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { RecipeService } from '../../../recipe/services/recipe.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

interface NutritionSummary {
  calories: number;
  protein: number;
  carbs: number;
  fat: number;
  fiber: number;
}

@Component({
  selector: 'nom-meal-plan-nutrition',
  standalone: true,
  imports: [
    AmwCardComponent,
    AmwButtonComponent,
    AmwInlineLoadingComponent,
    AmwIconComponent,
  ],
  templateUrl: './meal-plan-nutrition.component.html',
  styleUrl: './meal-plan-nutrition.component.scss',
})
export class MealPlanNutritionComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private mealPlanService = inject(MealPlanService);
  private recipeService = inject(RecipeService);

  // Signals
  mealPlanId = signal<number>(0);
  mealPlans = signal<MealPlanResponseModel[]>([]);
  nutrition = signal<NutritionSummary>({ calories: 0, protein: 0, carbs: 0, fat: 0, fiber: 0 });
  isLoading = signal(true);
  error = signal<string | null>(null);

  // Computed
  totalCalories = computed(() => this.nutrition().calories);
  hasData = computed(() => this.totalCalories() > 0);

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.mealPlanId.set(+id);
        this.loadNutrition();
      } else {
        this.loadWeekNutrition();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadNutrition(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService
      .getMealPlan(this.mealPlanId())
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (plan) => {
          this.mealPlans.set([plan]);
          // Mock nutrition data - in real implementation, would fetch from recipe
          this.nutrition.set({
            calories: 650,
            protein: 35,
            carbs: 75,
            fat: 25,
            fiber: 8,
          });
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
          console.error('Error:', err);
        },
      });
  }

  private loadWeekNutrition(): void {
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
          const weekPlans = this.filterCurrentWeekPlans(plans);
          this.mealPlans.set(weekPlans);
          // Mock aggregated nutrition
          this.nutrition.set({
            calories: 2100,
            protein: 95,
            carbs: 250,
            fat: 70,
            fiber: 28,
          });
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
          console.error('Error:', err);
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

  onBack(): void {
    if (this.mealPlanId()) {
      this.router.navigate(['/meal-plan', this.mealPlanId()]);
    } else {
      this.router.navigate(['/meal-plan']);
    }
  }

  onRetry(): void {
    if (this.mealPlanId()) {
      this.loadNutrition();
    } else {
      this.loadWeekNutrition();
    }
  }

  getPercentage(value: number, total: number): number {
    return total > 0 ? (value / total) * 100 : 0;
  }
}
