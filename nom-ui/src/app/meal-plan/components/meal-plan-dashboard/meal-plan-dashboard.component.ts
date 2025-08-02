import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
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
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

@Component({
    selector: 'app-meal-plan-dashboard',
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
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        ReactiveFormsModule,
        BasePageComponent,
    ],
    templateUrl: './meal-plan-dashboard.component.html',
    styleUrls: ['./meal-plan-dashboard.component.scss']
})
export class MealPlanDashboardComponent implements OnInit {
    mealPlans: MealPlanResponseModel[] = [];
    filteredPlans: MealPlanResponseModel[] = [];
    isLoading = true;
    error: string | null = null;
    searchControl = new FormControl('');
    viewMode: 'week' | 'month' = 'week';
    selectedDate = new Date();

    pageConfig: BasePageConfig = {
        title: 'Meal Plans',
        subtitle: 'Plan and organize your meals for the week or month',
        showRefreshButton: true,
        refreshButtonText: 'Refresh',
        maxWidth: '1200px',
    };

    constructor(
        private router: Router,
        private mealPlanService: MealPlanService,
        private snackBar: MatSnackBar,
        private dialog: MatDialog
    ) { }

    ngOnInit(): void {
        this.loadMealPlans();
        this.setupSearchFilter();
    }

    setupSearchFilter(): void {
        this.searchControl.valueChanges.pipe(
            debounceTime(300),
            distinctUntilChanged()
        ).subscribe(searchTerm => {
            this.filterPlans(searchTerm || '');
        });
    }

    loadMealPlans(): void {
        this.isLoading = true;
        this.error = null;

        this.mealPlanService.getMealPlans().subscribe({
            next: (mealPlans) => {
                this.mealPlans = mealPlans;
                this.filteredPlans = [...this.mealPlans];
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading meal plans:', error);
                this.error = 'Failed to load meal plans';
                this.isLoading = false;
            }
        });
    }

    filterPlans(searchTerm: string): void {
        if (!searchTerm.trim()) {
            this.filteredPlans = [...this.mealPlans];
        } else {
            const term = searchTerm.toLowerCase();
            this.filteredPlans = this.mealPlans.filter(plan =>
                plan.recipeName?.toLowerCase().includes(term) ||
                plan.description?.toLowerCase().includes(term)
            );
        }
    }

    onCreatePlan(): void {
        this.router.navigate(['/meal-plan/create']);
    }

    onViewPlan(planId: number): void {
        this.router.navigate(['/meal-plan', planId]);
    }

    onEditPlan(planId: number): void {
        this.router.navigate(['/meal-plan', planId, 'edit']);
    }

    onDeletePlan(planId: number): void {
        const dialogRef = this.dialog.open(ConfirmDialogComponent, {
            width: '400px',
            data: {
                title: 'Delete Meal Plan',
                message: 'Are you sure you want to delete this meal plan? This action cannot be undone.',
                confirmText: 'Delete',
                cancelText: 'Cancel',
                confirmColor: 'warn'
            }
        });

        dialogRef.afterClosed().subscribe(result => {
            if (result) {
                this.mealPlanService.deleteMealPlan(planId).subscribe({
                    next: () => {
                        this.snackBar.open('Meal plan deleted successfully', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                        this.loadMealPlans();
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

    onViewRules(): void {
        this.router.navigate(['/meal-plan/rules']);
    }

    onRefresh(): void {
        this.loadMealPlans();
    }

    onRetry(): void {
        this.loadMealPlans();
    }

    onViewModeChange(mode: 'week' | 'month'): void {
        this.viewMode = mode;
    }

    onDateChange(date: Date): void {
        this.selectedDate = date;
    }

    getPreviousWeekDate(): Date {
        const date = new Date(this.selectedDate);
        date.setDate(date.getDate() - 7);
        return date;
    }

    getNextWeekDate(): Date {
        const date = new Date(this.selectedDate);
        date.setDate(date.getDate() + 7);
        return date;
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

    getWeekDays(): Date[] {
        const days: Date[] = [];
        const startOfWeek = new Date(this.selectedDate);
        startOfWeek.setDate(startOfWeek.getDate() - startOfWeek.getDay());

        for (let i = 0; i < 7; i++) {
            const day = new Date(startOfWeek);
            day.setDate(day.getDate() + i);
            days.push(day);
        }

        return days;
    }

    getPlansForDate(date: Date): MealPlanResponseModel[] {
        const dateString = date.toISOString().split('T')[0];
        return this.filteredPlans.filter(plan =>
            plan.date === dateString
        );
    }

    isToday(date: Date): boolean {
        const today = new Date();
        return date.toDateString() === today.toDateString();
    }

    isSelectedDate(date: Date): boolean {
        return date.toDateString() === this.selectedDate.toDateString();
    }
} 