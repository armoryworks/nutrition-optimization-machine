import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwProgressSpinnerComponent,
} from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-meal-plan-print',
  standalone: true,
  imports: [
    CommonModule,
    AmwCardComponent,
    AmwButtonComponent,
    AmwProgressSpinnerComponent,
  ],
  templateUrl: './meal-plan-print.component.html',
  styleUrl: './meal-plan-print.component.scss',
})
export class MealPlanPrintComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private mealPlanService = inject(MealPlanService);
  private notificationService = inject(NotificationService);

  // Signals
  mealPlanId = signal<number>(0);
  mealPlan = signal<MealPlanResponseModel | null>(null);
  weekMealPlans = signal<MealPlanResponseModel[]>([]);
  isLoading = signal(true);
  isPrinting = signal(false);
  error = signal<string | null>(null);
  viewMode = signal<'single' | 'week'>('single');

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.mealPlanId.set(+id);
        this.viewMode.set('single');
        this.loadMealPlan();
      } else {
        this.viewMode.set('week');
        this.loadWeekMealPlans();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadMealPlan(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService
      .getMealPlan(this.mealPlanId())
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (plan: MealPlanResponseModel) => {
          this.mealPlan.set(plan);
        },
        error: (err: unknown) => {
          this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
          console.error('Error loading meal plan:', err);
        },
      });
  }

  private loadWeekMealPlans(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService
      .getMealPlans()
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (plans: MealPlanResponseModel[]) => {
          const weekPlans = this.filterCurrentWeekPlans(plans);
          this.weekMealPlans.set(weekPlans);
        },
        error: (err: unknown) => {
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
    }).sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
  }

  onPrintMealPlan(): void {
    this.isPrinting.set(true);

    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      this.notificationService.error('Unable to open print window');
      this.isPrinting.set(false);
      return;
    }

    const html = this.viewMode() === 'single'
      ? this.generateSinglePrintHTML(this.mealPlan()!)
      : this.generateWeekPrintHTML(this.weekMealPlans());

    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.print();

    this.isPrinting.set(false);
    this.notificationService.success('Meal plan ready to print/save as PDF');
  }

  onBack(): void {
    if (this.mealPlanId()) {
      this.router.navigate(['/meal-plan', this.mealPlanId()]);
    } else {
      this.router.navigate(['/meal-plan']);
    }
  }

  private generateSinglePrintHTML(plan: MealPlanResponseModel): string {
    const date = new Date(plan.date).toLocaleDateString('en-US', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });

    return `
      <!DOCTYPE html>
      <html>
        <head>
          <title>Meal Plan - ${plan.title}</title>
          <style>
            ${this.getPrintStyles()}
          </style>
        </head>
        <body>
          <h1>Meal Plan</h1>
          <div class="meal-plan-single">
            <div class="meal-header">
              <h2>${plan.title}</h2>
              <p class="date">${date}</p>
              <p class="meal-type">${plan.mealType}</p>
            </div>
            ${plan.recipeName ? `<div class="recipe-info"><strong>Recipe:</strong> ${plan.recipeName}</div>` : ''}
            ${plan.notes ? `<div class="notes"><strong>Notes:</strong> ${plan.notes}</div>` : ''}
          </div>
        </body>
      </html>
    `;
  }

  private generateWeekPrintHTML(plans: MealPlanResponseModel[]): string {
    const groupedByDate = this.groupPlansByDate(plans);

    const tableRows = Array.from(groupedByDate.entries())
      .map(([dateStr, dayPlans]) => {
        const date = new Date(dateStr);
        const dayName = date.toLocaleDateString('en-US', { weekday: 'long' });
        const dateFormatted = date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });

        const breakfast = dayPlans.find(p => p.mealType === 'Breakfast');
        const lunch = dayPlans.find(p => p.mealType === 'Lunch');
        const dinner = dayPlans.find(p => p.mealType === 'Dinner');
        const snack = dayPlans.find(p => p.mealType === 'Snack');

        return `
          <tr>
            <td class="date-cell">
              <strong>${dayName}</strong><br>
              <span class="date-sub">${dateFormatted}</span>
            </td>
            <td>${breakfast?.title || '-'}</td>
            <td>${lunch?.title || '-'}</td>
            <td>${dinner?.title || '-'}</td>
            <td>${snack?.title || '-'}</td>
          </tr>
        `;
      })
      .join('');

    return `
      <!DOCTYPE html>
      <html>
        <head>
          <title>Weekly Meal Plan</title>
          <style>
            ${this.getPrintStyles()}
          </style>
        </head>
        <body>
          <h1>Weekly Meal Plan</h1>
          <p class="subtitle">Week of ${this.getWeekStartDate()}</p>
          <table class="week-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Breakfast</th>
                <th>Lunch</th>
                <th>Dinner</th>
                <th>Snack</th>
              </tr>
            </thead>
            <tbody>
              ${tableRows}
            </tbody>
          </table>
        </body>
      </html>
    `;
  }

  private groupPlansByDate(plans: MealPlanResponseModel[]): Map<string, MealPlanResponseModel[]> {
    const grouped = new Map<string, MealPlanResponseModel[]>();

    plans.forEach(plan => {
      const dateKey = new Date(plan.date).toISOString().split('T')[0];
      if (!grouped.has(dateKey)) {
        grouped.set(dateKey, []);
      }
      grouped.get(dateKey)!.push(plan);
    });

    return grouped;
  }

  private getWeekStartDate(): string {
    const now = new Date();
    const startOfWeek = new Date(now);
    startOfWeek.setDate(now.getDate() - now.getDay());
    return startOfWeek.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  }

  private getPrintStyles(): string {
    return `
      body {
        font-family: Arial, sans-serif;
        padding: 20px;
        max-width: 900px;
        margin: 0 auto;
      }
      h1 {
        font-size: 28px;
        margin-bottom: 10px;
        color: #333;
      }
      h2 {
        font-size: 22px;
        margin: 0 0 5px 0;
        color: #555;
      }
      .subtitle {
        color: #666;
        font-size: 14px;
        margin-bottom: 20px;
      }
      .meal-plan-single {
        border: 2px solid #ddd;
        border-radius: 8px;
        padding: 20px;
        margin-top: 20px;
      }
      .meal-header {
        margin-bottom: 15px;
        padding-bottom: 15px;
        border-bottom: 1px solid #eee;
      }
      .date {
        color: #666;
        margin: 5px 0;
        font-size: 14px;
      }
      .meal-type {
        display: inline-block;
        background-color: #f0f0f0;
        padding: 4px 12px;
        border-radius: 4px;
        font-size: 12px;
        font-weight: 500;
        color: #555;
      }
      .recipe-info, .notes {
        margin: 10px 0;
        font-size: 14px;
        line-height: 1.6;
      }
      .week-table {
        width: 100%;
        border-collapse: collapse;
        margin-top: 20px;
      }
      .week-table th,
      .week-table td {
        border: 1px solid #ddd;
        padding: 12px;
        text-align: left;
      }
      .week-table th {
        background-color: #f8f8f8;
        font-weight: 600;
        font-size: 14px;
      }
      .week-table td {
        font-size: 13px;
      }
      .date-cell {
        font-weight: 500;
        min-width: 120px;
      }
      .date-sub {
        color: #888;
        font-size: 12px;
        font-weight: normal;
      }
      @media print {
        body {
          padding: 0;
        }
        .week-table {
          page-break-inside: auto;
        }
        .week-table tr {
          page-break-inside: avoid;
          page-break-after: auto;
        }
      }
    `;
  }
}
