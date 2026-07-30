import { Component, inject, computed, ChangeDetectionStrategy } from '@angular/core';
import { toLocalDateString } from '../../core/utils/local-date';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, startWith, map, switchMap, of } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MealPlanService } from '../../core/services/meal-plan.service';
import { HouseholdService } from '../../core/services/household.service';
import { MealPlanWeekResponse } from '../../core/models/meal-plan-week-response.model';
import { MealPlanEntry } from '../../core/models/meal-plan-entry.model';

interface UpcomingMeal {
  dayLabel: string;
  mealType: string;
  entries: MealPlanEntry[];
}

@Component({
  selector: 'nom-sidebar',
  imports: [RouterLink, MatIconModule, MatButtonModule],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Sidebar {
  private router = inject(Router);
  private mealPlanService = inject(MealPlanService);
  private householdService = inject(HouseholdService);

  private navEnd = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(e => e.urlAfterRedirects),
      startWith(this.router.url),
    ),
  );

  isHomePage = computed(() => {
    const url = this.navEnd();
    return url === '/home' || url === '/';
  });

  weekData = toSignal(
    this.householdService.getHouseholds().pipe(
      switchMap(list => {
        if (list.length === 0) return of(null);
        const monday = Sidebar.getMonday(new Date());
        return this.mealPlanService.getWeek(list[0].id, monday);
      }),
    ),
    { initialValue: null as MealPlanWeekResponse | null },
  );

  upcomingShoppingSummary = computed(() => {
    const data = this.weekData();
    if (!data) return null;
    const todayStr = Sidebar.toDateString(new Date());
    const recipeIds = new Set<number>();
    for (const day of data.days) {
      if (day.date < todayStr) continue;
      for (const cell of day.cells) {
        for (const entry of cell.entries) {
          if (entry.recipeId) recipeIds.add(entry.recipeId);
        }
      }
    }
    return recipeIds.size > 0 ? recipeIds.size : 0;
  });

  upcomingMeals = computed<UpcomingMeal[]>(() => {
    const data = this.weekData();
    if (!data) return [];

    const todayStr = Sidebar.toDateString(new Date());
    const meals: UpcomingMeal[] = [];

    for (const day of data.days) {
      if (day.date < todayStr) continue;

      const dayLabel = day.date === todayStr
        ? 'Today'
        : new Date(day.date + 'T00:00:00').toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });

      for (const cell of day.cells) {
        if (cell.entries.length > 0) {
          meals.push({ dayLabel, mealType: cell.mealType, entries: cell.entries });
        }
      }

      if (meals.length >= 6) break;
    }

    return meals.slice(0, 6);
  });

  static getMonday(date: Date): string {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1);
    d.setDate(diff);
    return Sidebar.toDateString(d);
  }

  static toDateString(date: Date): string {
    return toLocalDateString(date);
  }
}
