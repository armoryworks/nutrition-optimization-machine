import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MealPlanService } from '../../core/services/meal-plan.service';
import { HouseholdService } from '../../core/services/household.service';
import { MealPlanWeekResponse, MealPlanDay, MealPlanEntry } from '../../core/models/meal-plan.model';

interface UpcomingMeal {
  dayLabel: string;
  mealType: string;
  entries: MealPlanEntry[];
}

@Component({
  selector: 'nom-sidebar',
  imports: [RouterLink, MatIconModule, MatButtonModule],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss'
})
export class Sidebar implements OnInit {
  private mealPlanService = inject(MealPlanService);
  private householdService = inject(HouseholdService);

  weekData = signal<MealPlanWeekResponse | null>(null);

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

  ngOnInit(): void {
    this.loadUpcoming();
  }

  private loadUpcoming(): void {
    this.householdService.getHouseholds().subscribe({
      next: (list) => {
        if (list.length > 0) {
          const monday = Sidebar.getMonday(new Date());
          this.mealPlanService.getWeek(list[0].id, monday).subscribe({
            next: (data) => this.weekData.set(data),
          });
        }
      },
    });
  }

  static getMonday(date: Date): string {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1);
    d.setDate(diff);
    return Sidebar.toDateString(d);
  }

  static toDateString(date: Date): string {
    return date.toISOString().split('T')[0];
  }
}
