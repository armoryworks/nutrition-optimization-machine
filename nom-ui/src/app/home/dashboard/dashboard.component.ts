import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ErrorBanner } from '../../shared/components/error-banner/error-banner.component';
import { toLocalDateString } from '../../core/utils/local-date';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { MealPlanService } from '../../core/services/meal-plan.service';
import { HouseholdStore } from '../../core/services/household-store';
import { MealPlanWeekResponse } from '../../core/models/meal-plan-week-response.model';
import { MealPlanDay } from '../../core/models/meal-plan-day.model';
import { MealPlanCell } from '../../core/models/meal-plan-cell.model';
import { HouseholdResponseModel } from '../../core/models/household-response.model';
import { RecipeSearchDialog, RecipeSearchDialogData, RecipeSearchDialogResult } from '../../plan/recipe-search-dialog/recipe-search-dialog.component';
import { ShuffleFlowService } from '../../plan/shuffle-flow.service';
import { AuthService } from '../../core/services/auth.service';
import { MacroGoalService } from '../../core/services/macro-goal.service';
import { EffectiveMacroGoal } from '../../core/models/macro-goal.model';

@Component({
  selector: 'nom-dashboard',
  imports: [DecimalPipe, RouterLink, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatTooltipModule, ErrorBanner],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Dashboard implements OnInit {
  private mealPlanService = inject(MealPlanService);
  private shuffleFlow = inject(ShuffleFlowService);
  private householdStore = inject(HouseholdStore);
  private dialog = inject(MatDialog);
  private destroyRef = inject(DestroyRef);
  private authService = inject(AuthService);
  private macroGoalService = inject(MacroGoalService);

  households = signal<HouseholdResponseModel[]>([]);
  weekData = signal<MealPlanWeekResponse | null>(null);
  loading = signal(true);
  error = signal('');
  shufflingToday = signal(false);

  hasHousehold = computed(() => this.households().length > 0);

  effectiveGoal = signal<EffectiveMacroGoal | null>(null);
  goalCalories = computed(() => this.effectiveGoal()?.caloriesTarget ?? null);
  goalProtein = computed(() => this.effectiveGoal()?.proteinGramsTarget ?? null);
  goalCarbs = computed(() => this.effectiveGoal()?.carbGramsTarget ?? null);
  goalFat = computed(() => this.effectiveGoal()?.fatGramsTarget ?? null);

  /** Percent toward a daily goal, capped at 100 for the progress bar. */
  goalPct(actual: number, goal: number): number {
    if (goal <= 0) return 0;
    return Math.min(100, Math.round((actual / goal) * 100));
  }

  today = computed(() => {
    const data = this.weekData();
    if (!data) return null;
    const todayStr = Dashboard.toDateString(new Date());
    return data.days.find(d => d.date === todayStr) ?? null;
  });

  todayCalories = computed(() => {
    const day = this.today();
    if (!day) return 0;
    return day.cells.reduce((sum, c) => sum + (c.totalCalories ?? 0), 0);
  });

  todayProtein = computed(() => {
    const day = this.today();
    if (!day) return 0;
    return day.cells.reduce((sum, c) => sum + (c.totalProteinGrams ?? 0), 0);
  });

  todayCarbs = computed(() => {
    const day = this.today();
    if (!day) return 0;
    return day.cells.reduce((sum, c) => sum + (c.totalCarbGrams ?? 0), 0);
  });

  todayFat = computed(() => {
    const day = this.today();
    if (!day) return 0;
    return day.cells.reduce((sum, c) => sum + (c.totalFatGrams ?? 0), 0);
  });

  filledMealsToday = computed(() => {
    const day = this.today();
    if (!day) return 0;
    return day.cells.filter(c => c.entries.length > 0).length;
  });

  totalMealSlots = computed(() => {
    const day = this.today();
    return day?.cells.length ?? 0;
  });

  weekLabel = computed(() => {
    const data = this.weekData();
    if (!data) return '';
    const start = new Date(data.weekStart + 'T00:00:00');
    const end = new Date(data.weekEnd + 'T00:00:00');
    const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
    return `${start.toLocaleDateString(undefined, opts)} – ${end.toLocaleDateString(undefined, opts)}, ${end.getFullYear()}`;
  });

  ngOnInit(): void {
    this.loadDashboardData();
    this.loadEffectiveGoal();
  }

  private loadEffectiveGoal(): void {
    const personId = this.authService.personId();
    if (!personId) return;
    this.macroGoalService
      .getEffectiveForPerson(personId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (goal) => this.effectiveGoal.set(goal.source === 'none' ? null : goal),
        error: () => this.effectiveGoal.set(null),
      });
  }

  isToday(dateStr: string): boolean {
    return dateStr === Dashboard.toDateString(new Date());
  }

  formatDayShort(dateStr: string): string {
    const date = new Date(dateStr + 'T00:00:00');
    return date.toLocaleDateString(undefined, { weekday: 'short' });
  }

  formatDayNumber(dateStr: string): string {
    const date = new Date(dateStr + 'T00:00:00');
    return date.toLocaleDateString(undefined, { day: 'numeric' });
  }

  getMealIcon(mealType: string): string {
    const icons: Record<string, string> = {
      'Breakfast': 'egg_alt',
      'Lunch': 'lunch_dining',
      'Dinner': 'dinner_dining',
      'Snack': 'cookie',
      'Snacks': 'cookie',
    };
    return icons[mealType] ?? 'restaurant';
  }

  hasNutritionToday(): boolean {
    return this.todayCalories() > 0;
  }

  onMealClick(day: MealPlanDay, cell: MealPlanCell): void {
    const householdId = this.households()[0]?.id;
    if (!householdId) return;

    const dialogRef = this.dialog.open(RecipeSearchDialog, {
      width: '560px',
      data: {
        householdId,
        date: day.date,
        mealTypeId: cell.mealTypeId,
        mealType: cell.mealType,
        entries: cell.entries,
      } as RecipeSearchDialogData,
    });

    dialogRef.afterClosed().subscribe((result: RecipeSearchDialogResult) => {
      if (result?.changed) this.loadWeek(householdId);
    });
  }

  shuffleTodayEmpty(): void {
    const householdId = this.households()[0]?.id;
    const day = this.today();
    if (!householdId || !day) return;

    this.shuffleFlow.run({
      householdId,
      days: [day],
      startDate: day.date,
      endDate: day.date,
      onShuffleStart: () => this.shufflingToday.set(true),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        this.weekData.set(response.week);
        this.shufflingToday.set(false);
      },
      error: () => { this.shufflingToday.set(false); },
    });
  }

  private loadDashboardData(): void {
    this.loading.set(true);
    this.householdStore.getHouseholds().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (list) => {
        this.households.set(list);
        if (list.length > 0) {
          this.loadWeek(list[0].id);
        } else {
          this.loading.set(false);
        }
      },
      error: () => {
        this.error.set('Unable to load households.');
        this.loading.set(false);
      },
    });
  }

  private loadWeek(householdId: number): void {
    const monday = Dashboard.getMonday(new Date());
    this.mealPlanService.getWeek(householdId, monday).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (data) => {
        this.weekData.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load meal plan.');
        this.loading.set(false);
      },
    });
  }

  static getMonday(date: Date): string {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1);
    d.setDate(diff);
    return Dashboard.toDateString(d);
  }

  static toDateString(date: Date): string {
    return toLocalDateString(date);
  }
}
