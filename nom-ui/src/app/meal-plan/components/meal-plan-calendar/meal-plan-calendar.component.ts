import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCalendarFullComponent,
  AmwInlineLoadingComponent,
  AmwButtonComponent,
} from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

// Local interfaces matching AMW Calendar types
interface CalendarEvent<T = unknown> {
  id: string;
  data: T;
  title?: string;
  description?: string;
  start: Date;
  end?: Date;
  allDay?: boolean;
  color?: string;
  icon?: string;
  editable?: boolean;
  deletable?: boolean;
  draggable?: boolean;
}

interface CalendarConfig<T = unknown> {
  displayProperty: keyof T;
  editable?: boolean;
  deletable?: boolean;
  draggable?: boolean;
  allowCreate?: boolean;
  eventRenderer?: (event: CalendarEvent<T>) => string;
  eventColor?: (event: CalendarEvent<T>) => string;
}

interface CalendarEventChangeEvent<T = unknown> {
  event: CalendarEvent<T>;
  type: 'create' | 'add' | 'update' | 'delete' | 'move';
}

type CalendarView = 'month' | 'week' | 'day' | 'agenda';

// Meal type time defaults (hours)
const MEAL_TIMES: Record<string, number> = {
  breakfast: 7,
  lunch: 12,
  dinner: 18,
  snack: 15,
};

// Meal type colors
const MEAL_COLORS: Record<string, string> = {
  breakfast: '#FFA000', // Amber
  lunch: '#43A047',     // Green
  dinner: '#E53935',    // Red
  snack: '#8E24AA',     // Purple
};

// Meal type icons
const MEAL_ICONS: Record<string, string> = {
  breakfast: 'wb_sunny',
  lunch: 'restaurant',
  dinner: 'dinner_dining',
  snack: 'local_cafe',
};

@Component({
  selector: 'nom-meal-plan-calendar',
  standalone: true,
  imports: [
    AmwCalendarFullComponent,
    AmwInlineLoadingComponent,
    AmwButtonComponent,
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
  currentView = signal<CalendarView>('week');
  isLoading = signal(true);
  error = signal<string | null>(null);

  // Convert meal plans to calendar events
  calendarEvents = computed(() => this.convertToCalendarEvents(this.mealPlans()));

  // Calendar configuration
  calendarConfig = computed<CalendarConfig<MealPlanResponseModel>>(() => ({
    displayProperty: 'recipeName',
    editable: true,
    deletable: true,
    draggable: true,
    allowCreate: true,
    eventRenderer: (event) => event.data?.recipeName || event.data?.title || 'Meal',
    eventColor: (event) => {
      const mealType = event.data?.mealType?.toLowerCase() || '';
      return MEAL_COLORS[mealType] || 'var(--mat-sys-primary)';
    },
  }));

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

  private convertToCalendarEvents(plans: MealPlanResponseModel[]): CalendarEvent<MealPlanResponseModel>[] {
    return plans.map((plan) => {
      const mealType = plan.mealType?.toLowerCase() || 'lunch';
      const startDate = new Date(plan.date);
      const hour = MEAL_TIMES[mealType] ?? 12;
      startDate.setHours(hour, 0, 0, 0);

      const endDate = new Date(startDate);
      endDate.setHours(hour + 1); // 1 hour duration

      return {
        id: plan.id.toString(),
        data: plan,
        title: plan.recipeName || plan.title,
        description: plan.notes || plan.description,
        start: startDate,
        end: endDate,
        allDay: false,
        color: MEAL_COLORS[mealType] || 'var(--mat-sys-primary)',
        icon: MEAL_ICONS[mealType] || 'restaurant_menu',
        editable: true,
        deletable: true,
        draggable: true,
      };
    });
  }

  onEventClick(event: CalendarEvent<MealPlanResponseModel>): void {
    if (event.data?.id) {
      this.router.navigate(['/meal-plan', event.data.id]);
    }
  }

  onEventChange(changeEvent: CalendarEventChangeEvent<MealPlanResponseModel>): void {
    const { event, type } = changeEvent;

    switch (type) {
      case 'create':
      case 'add':
        // Navigate to create page with pre-filled date/time
        this.router.navigate(['/meal-plan/create'], {
          queryParams: {
            date: event.start.toISOString(),
            mealType: this.getMealTypeFromTime(event.start),
          },
        });
        break;
      case 'delete':
        // Handle delete - could call service directly or navigate
        if (event.data?.id) {
          this.deleteMealPlan(event.data.id);
        }
        break;
      case 'move':
      case 'update':
        // Handle move/update
        if (event.data?.id) {
          this.updateMealPlanDate(event.data.id, event.start);
        }
        break;
    }
  }

  onCellClick(cellEvent: { date: Date; time?: Date }): void {
    const date = cellEvent.time || cellEvent.date;
    this.router.navigate(['/meal-plan/create'], {
      queryParams: {
        date: date.toISOString(),
        mealType: this.getMealTypeFromTime(date),
      },
    });
  }

  onViewChange(view: CalendarView): void {
    this.currentView.set(view);
  }

  onDateChange(date: Date): void {
    this.currentDate.set(date);
  }

  onRetry(): void {
    this.loadMealPlans();
  }

  private getMealTypeFromTime(date: Date): string {
    const hour = date.getHours();
    if (hour < 10) return 'breakfast';
    if (hour < 14) return 'lunch';
    if (hour < 17) return 'snack';
    return 'dinner';
  }

  private deleteMealPlan(id: number): void {
    this.mealPlanService
      .deleteMealPlan(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          // Remove from local state
          this.mealPlans.update((plans) => plans.filter((p) => p.id !== id));
        },
        error: (err) => {
          console.error('Error deleting meal plan:', err);
        },
      });
  }

  private updateMealPlanDate(id: number, newDate: Date): void {
    const plan = this.mealPlans().find((p) => p.id === id);
    if (!plan) return;

    const updatedPlan = { ...plan, date: newDate };
    this.mealPlanService
      .updateMealPlan(id, updatedPlan)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          // Update local state
          this.mealPlans.update((plans) =>
            plans.map((p) => (p.id === id ? { ...p, date: newDate } : p))
          );
        },
        error: (err) => {
          console.error('Error updating meal plan:', err);
        },
      });
  }
}
