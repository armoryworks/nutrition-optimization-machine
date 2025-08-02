import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';
import { BaseDetailComponent, BaseDetailConfig } from '../../../common/components/base-detail/base-detail.component';

@Component({
  selector: 'app-meal-plan-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatDialogModule,
    MatListModule,
    MatMenuModule,
    BaseDetailComponent,
  ],
  templateUrl: './meal-plan-detail.component.html',
  styleUrls: ['./meal-plan-detail.component.scss']
})
export class MealPlanDetailComponent implements OnInit {
  mealPlan: MealPlanResponseModel | null = null;
  isLoading = true;
  error: string | null = null;
  mealPlanId: number = 0;

  detailConfig: BaseDetailConfig = {
    title: 'Meal Plan Details',
    subtitle: 'View and manage meal plan information',
    showBackButton: true,
    maxWidth: '800px',
  };

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private mealPlanService: MealPlanService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) { }

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.mealPlanId = +params['id'];
      this.loadMealPlan();
    });
  }

  loadMealPlan(): void {
    this.isLoading = true;
    this.error = null;

    this.mealPlanService.getMealPlan(this.mealPlanId).subscribe({
      next: (mealPlan) => {
        this.mealPlan = mealPlan;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading meal plan:', error);
        this.error = 'Failed to load meal plan details';
        this.isLoading = false;
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
    this.router.navigate(['/meal-plan', this.mealPlanId, 'edit']);
  }

  onDeleteMealPlan(): void {
    if (!this.mealPlan) return;

    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        title: 'Delete Meal Plan',
        message: `Are you sure you want to delete "${this.mealPlan.recipeName}"? This action cannot be undone.`,
        confirmText: 'Delete',
        cancelText: 'Cancel',
        confirmColor: 'warn'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.mealPlanService.deleteMealPlan(this.mealPlanId).subscribe({
          next: () => {
            this.snackBar.open('Meal plan deleted successfully', 'Close', {
              duration: 3000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
            this.router.navigate(['/meal-plan']);
          },
          error: (error) => {
            console.error('Error deleting meal plan:', error);
            this.snackBar.open('Failed to delete meal plan', 'Close', {
              duration: 5000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
          }
        });
      }
    });
  }

  onDuplicateMealPlan(): void {
    if (!this.mealPlan) return;

    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        title: 'Duplicate Meal Plan',
        message: `Are you sure you want to duplicate "${this.mealPlan.recipeName}"?`,
        confirmText: 'Duplicate',
        cancelText: 'Cancel',
        confirmColor: 'primary'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        // TODO: Implement duplicate meal plan functionality
        this.snackBar.open('Duplicate functionality coming soon', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
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

  getMealTypeColor(mealType: string): string {
    switch (mealType?.toLowerCase()) {
      case 'breakfast': return 'primary';
      case 'lunch': return 'accent';
      case 'dinner': return 'warn';
      case 'snack': return 'primary';
      default: return 'primary';
    }
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  }

  isToday(dateString: string): boolean {
    const today = new Date();
    const planDate = new Date(dateString);
    return planDate.toDateString() === today.toDateString();
  }

  isPast(dateString: string): boolean {
    const today = new Date();
    const planDate = new Date(dateString);
    return planDate < today;
  }
} 