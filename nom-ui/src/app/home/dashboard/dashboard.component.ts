import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
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
import { ShuffleConfirmDialog, ShuffleConfirmResult } from '../../plan/shuffle-confirm-dialog.component';

@Component({
  selector: 'nom-dashboard',
  imports: [DecimalPipe, RouterLink, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatTooltipModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Dashboard implements OnInit {
  private mealPlanService = inject(MealPlanService);
  private householdStore = inject(HouseholdStore);
  private dialog = inject(MatDialog);
  private destroyRef = inject(DestroyRef);

  households = signal<HouseholdResponseModel[]>([]);
  weekData = signal<MealPlanWeekResponse | null>(null);
  loading = signal(true);
  error = signal('');
  shufflingToday = signal(false);

  hasHousehold = computed(() => this.households().length > 0);

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

    const unshoppedEntries = (c: { entries: { shoppingCompletedAt: string | null }[] }) =>
      c.entries.filter(e => !e.shoppingCompletedAt);
    const hasFilledSlots = day.cells.some(c => unshoppedEntries(c).length > 0);
    const hasEmptySlots = day.cells.some(c => c.entries.length === 0);
    const todayStr = day.date;

    if (hasFilledSlots) {
      const dialogRef = this.dialog.open(ShuffleConfirmDialog, { width: '400px' });
      dialogRef.afterClosed().subscribe((result: ShuffleConfirmResult) => {
        if (result === 'empty') {
          this.callShuffle(householdId, todayStr, false);
        } else if (result === 'replace') {
          this.callShuffle(householdId, todayStr, true);
        }
      });
    } else if (hasEmptySlots) {
      this.callShuffle(householdId, todayStr, false);
    }
  }

  private callShuffle(householdId: number, date: string, replaceExisting: boolean): void {
    this.shufflingToday.set(true);

    this.mealPlanService.shuffle({
      householdId,
      startDate: date,
      endDate: date,
      replaceExisting,
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
