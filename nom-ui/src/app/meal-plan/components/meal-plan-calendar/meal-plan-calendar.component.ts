import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwInlineLoadingComponent,
  AmwIconComponent,
  AmwIconButtonComponent,
} from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

interface DayMeals {
  date: Date;
  dayName: string;
  dayNumber: number;
  isToday: boolean;
  isPast: boolean;
  breakfast?: MealPlanResponseModel;
  lunch?: MealPlanResponseModel;
  dinner?: MealPlanResponseModel;
  snack?: MealPlanResponseModel;
}

@Component({
  selector: 'nom-meal-plan-calendar',
  standalone: true,
  imports: [
    AmwCardComponent,
    AmwButtonComponent,
    AmwInlineLoadingComponent,
    AmwIconComponent,
    AmwIconButtonComponent,
  ],
  templateUrl: './meal-plan-calendar.component.html',
  styleUrl: './meal-plan-calendar.component.scss',
})
export class MealPlanCalendarComponent implements OnInit, OnDestroy {
  private router = inject(Router);
  private mealPlanService = inject(MealPlanService);

  // Signals
  mealPlans = signal<MealPlanResponseModel[]>([]);
  currentDate = signal(new Date());
  viewMode = signal<'week' | 'month'>('week');
  isLoading = signal(true);
  error = signal<string | null>(null);

  // Computed
  weekDays = computed(() => this.getWeekDays(this.currentDate()));
  weekMeals = computed(() => this.organizeWeekMeals(this.mealPlans(), this.weekDays()));

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.loadMealPlans();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadMealPlans(): void {
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
          this.mealPlans.set(plans);
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
          console.error('Error loading meal plans:', err);
        },
      });
  }

  private getWeekDays(currentDate: Date): Date[] {
    const startOfWeek = new Date(currentDate);
    const day = startOfWeek.getDay();
    const diff = startOfWeek.getDate() - day; // Sunday is 0
    startOfWeek.setDate(diff);

    const days: Date[] = [];
    for (let i = 0; i < 7; i++) {
      const date = new Date(startOfWeek);
      date.setDate(startOfWeek.getDate() + i);
      days.push(date);
    }
    return days;
  }

  private organizeWeekMeals(plans: MealPlanResponseModel[], weekDays: Date[]): DayMeals[] {
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    return weekDays.map((date) => {
      const dayPlans = plans.filter((plan) => {
        const planDate = new Date(plan.date);
        return this.isSameDay(planDate, date);
      });

      const dayMeals: DayMeals = {
        date,
        dayName: date.toLocaleDateString('en-US', { weekday: 'short' }),
        dayNumber: date.getDate(),
        isToday: this.isSameDay(date, today),
        isPast: date < today,
      };

      dayPlans.forEach((plan) => {
        const mealType = plan.mealType?.toLowerCase();
        if (mealType === 'breakfast') dayMeals.breakfast = plan;
        else if (mealType === 'lunch') dayMeals.lunch = plan;
        else if (mealType === 'dinner') dayMeals.dinner = plan;
        else if (mealType === 'snack') dayMeals.snack = plan;
      });

      return dayMeals;
    });
  }

  private isSameDay(date1: Date, date2: Date): boolean {
    return (
      date1.getFullYear() === date2.getFullYear() &&
      date1.getMonth() === date2.getMonth() &&
      date1.getDate() === date2.getDate()
    );
  }

  onPreviousWeek(): void {
    const newDate = new Date(this.currentDate());
    newDate.setDate(newDate.getDate() - 7);
    this.currentDate.set(newDate);
  }

  onNextWeek(): void {
    const newDate = new Date(this.currentDate());
    newDate.setDate(newDate.getDate() + 7);
    this.currentDate.set(newDate);
  }

  onToday(): void {
    this.currentDate.set(new Date());
  }

  onAddMeal(date: Date, mealType: string): void {
    this.router.navigate(['/meal-plan/create'], {
      queryParams: {
        date: date.toISOString(),
        mealType: mealType,
      },
    });
  }

  onViewMeal(mealId: number): void {
    this.router.navigate(['/meal-plan', mealId]);
  }

  onRetry(): void {
    this.loadMealPlans();
  }

  getMealTypeIcon(mealType: string): string {
    switch (mealType?.toLowerCase()) {
      case 'breakfast':
        return 'wb_sunny';
      case 'lunch':
        return 'restaurant';
      case 'dinner':
        return 'dinner_dining';
      case 'snack':
        return 'local_cafe';
      default:
        return 'restaurant_menu';
    }
  }

  getCurrentWeekLabel(): string {
    const weekDays = this.weekDays();
    if (weekDays.length === 0) return '';

    const firstDay = weekDays[0];
    const lastDay = weekDays[weekDays.length - 1];

    const formatOptions: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };

    if (firstDay.getMonth() === lastDay.getMonth()) {
      return `${firstDay.toLocaleDateString('en-US', { month: 'short' })} ${firstDay.getDate()} - ${lastDay.getDate()}, ${firstDay.getFullYear()}`;
    } else {
      return `${firstDay.toLocaleDateString('en-US', formatOptions)} - ${lastDay.toLocaleDateString('en-US', formatOptions)}, ${firstDay.getFullYear()}`;
    }
  }
}
