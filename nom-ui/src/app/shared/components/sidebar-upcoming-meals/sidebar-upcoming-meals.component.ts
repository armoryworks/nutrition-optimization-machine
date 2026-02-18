import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AmwIconComponent } from 'angular-material-wrap';
import { MealPlanService } from '../../../meal-plan/services/meal-plan.service';

interface DayMeals {
  date: Date;
  dayLabel: string;
  meals: { id: number; title: string; mealType: string; recipeName?: string }[];
}

@Component({
  selector: 'nom-sidebar-upcoming-meals',
  standalone: true,
  imports: [CommonModule, RouterModule, AmwIconComponent],
  templateUrl: './sidebar-upcoming-meals.component.html',
  styleUrls: ['./sidebar-upcoming-meals.component.scss'],
})
export class SidebarUpcomingMealsComponent implements OnInit {
  private mealPlanService = inject(MealPlanService);

  days = signal<DayMeals[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.loadUpcomingMeals();
  }

  private loadUpcomingMeals(): void {
    this.mealPlanService.getMealPlans().subscribe({
      next: (plans) => {
        const today = new Date();
        today.setHours(0, 0, 0, 0);

        const upcoming: DayMeals[] = [];
        for (let i = 0; i < 3; i++) {
          const date = new Date(today);
          date.setDate(date.getDate() + i);

          const dayPlans = plans
            .filter((p) => {
              const planDate = new Date(p.date);
              planDate.setHours(0, 0, 0, 0);
              return planDate.getTime() === date.getTime();
            })
            .map((p) => ({
              id: p.id,
              title: p.title || p.recipeName || 'Untitled',
              mealType: p.mealType || 'Meal',
              recipeName: p.recipeName,
            }));

          upcoming.push({
            date,
            dayLabel: i === 0 ? 'Today' : i === 1 ? 'Tomorrow' : date.toLocaleDateString('en-US', { weekday: 'long' }),
            meals: dayPlans,
          });
        }

        this.days.set(upcoming);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
