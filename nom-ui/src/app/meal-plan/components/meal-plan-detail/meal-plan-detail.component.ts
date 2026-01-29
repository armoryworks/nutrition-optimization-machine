import { Component, OnInit, inject, signal } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AmwButtonComponent, AmwIconComponent, AmwChipComponent, AmwMenuComponent, AmwMenuItemComponent, AmwMenuTriggerForDirective, AmwDialogService, AmwProgressBarComponent } from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-meal-plan-detail',
  standalone: true,
  imports: [
    TitleCasePipe,
    AmwProgressBarComponent,
    AmwButtonComponent,
    AmwIconComponent,
    AmwChipComponent,
    AmwMenuComponent,
    AmwMenuItemComponent,
    AmwMenuTriggerForDirective
  ],
  templateUrl: './meal-plan-detail.component.html',
  styleUrls: ['./meal-plan-detail.component.scss']
})
export class MealPlanDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private mealPlanService = inject(MealPlanService);
  private notificationService = inject(NotificationService);
  private dialogService = inject(AmwDialogService);

  mealPlan = signal<MealPlanResponseModel | null>(null);
  isLoading = signal(true);
  error = signal<string | null>(null);
  mealPlanId = signal(0);

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.mealPlanId.set(+params['id']);
      this.loadMealPlan();
    });
  }

  loadMealPlan(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService.getMealPlan(this.mealPlanId()).subscribe({
      next: (mealPlan) => {
        this.mealPlan.set(mealPlan);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading meal plan:', error);
        this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
        this.isLoading.set(false);
      }
    });
  }

  onBack(): void {
    this.router.navigate(['/meal-plan']);
  }

  onRetry(): void {
    this.loadMealPlan();
  }

  onEditMealPlan(): void {
    this.router.navigate(['/meal-plan', this.mealPlanId(), 'edit']);
  }

  onDeleteMealPlan(): void {
    if (!this.mealPlan()) return;

    this.dialogService.confirm(
      `Are you sure you want to delete "${this.mealPlan()!.recipeName}"? This action cannot be undone.`,
      'Delete Meal Plan'
    ).subscribe(result => {
      if (result) {
        this.mealPlanService.deleteMealPlan(this.mealPlanId()).subscribe({
          next: () => {
            this.notificationService.success('Meal plan deleted successfully');
            this.router.navigate(['/meal-plan']);
          },
          error: (error) => {
            console.error('Error deleting meal plan:', error);
            this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.DELETE_FAILED);
          }
        });
      }
    });
  }

  onDuplicateMealPlan(): void {
    if (!this.mealPlan()) return;

    this.dialogService.confirm(
      `Are you sure you want to duplicate "${this.mealPlan()!.recipeName}"?`,
      'Duplicate Meal Plan'
    ).subscribe(result => {
      if (result) {
        // TODO: Implement duplicate meal plan functionality
        this.notificationService.info('Duplicate functionality coming soon');
      }
    });
  }

  getMealTypeIcon(mealType: string): string {
    switch (mealType?.toLowerCase()) {
      case 'breakfast': return 'wb_sunny';
      case 'lunch': return 'restaurant';
      case 'dinner': return 'dinner_dining';
      case 'snack': return 'local_cafe';
      default: return 'restaurant_menu';
    }
  }

  getMealTypeColor(mealType: string): 'primary' | 'accent' | 'warn' {
    switch (mealType?.toLowerCase()) {
      case 'breakfast': return 'primary';
      case 'lunch': return 'accent';
      case 'dinner': return 'warn';
      case 'snack': return 'primary';
      default: return 'primary';
    }
  }

  formatDate(date: Date | string): string {
    const dateObj = date instanceof Date ? date : new Date(date);
    return dateObj.toLocaleDateString('en-US', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  }

  isToday(date: Date | string): boolean {
    const today = new Date();
    const planDate = date instanceof Date ? date : new Date(date);
    return planDate.toDateString() === today.toDateString();
  }

  isPast(date: Date | string): boolean {
    const today = new Date();
    const planDate = date instanceof Date ? date : new Date(date);
    return planDate < today;
  }
} 