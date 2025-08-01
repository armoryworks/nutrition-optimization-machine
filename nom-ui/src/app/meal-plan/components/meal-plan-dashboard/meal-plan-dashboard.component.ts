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

        // TODO: Implement get all meal plans service method
        // For now, using mock data
        setTimeout(() => {
            this.mealPlans = [
                {
                    id: 1,
                    householdId: 1,
                    authorId: 1,
                    date: new Date(),
                    mealType: 'Breakfast',
                    recipeId: 1,
                    recipeName: 'Oatmeal with Berries',
                    notes: 'Add honey for sweetness'
                },
                {
                    id: 2,
                    householdId: 1,
                    authorId: 1,
                    date: new Date(),
                    mealType: 'Lunch',
                    recipeId: 2,
                    recipeName: 'Grilled Chicken Salad',
                    notes: 'Use mixed greens'
                },
                {
                    id: 3,
                    householdId: 1,
                    authorId: 1,
                    date: new Date(),
                    mealType: 'Dinner',
                    recipeId: 3,
                    recipeName: 'Pasta Carbonara',
                    notes: 'Add extra cheese'
                }
            ];
            this.filteredPlans = [...this.mealPlans];
            this.isLoading = false;
        }, 1000);
    }

    filterPlans(searchTerm: string): void {
        if (!searchTerm.trim()) {
            this.filteredPlans = [...this.mealPlans];
        } else {
            const term = searchTerm.toLowerCase();
            this.filteredPlans = this.mealPlans.filter(plan =>
                plan.recipeName?.toLowerCase().includes(term) ||
                plan.mealType.toLowerCase().includes(term) ||
                plan.notes?.toLowerCase().includes(term)
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
        // TODO: Implement delete confirmation dialog
        this.snackBar.open('Delete functionality not yet implemented', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
        });
    }

    onViewRules(): void {
        this.router.navigate(['/meal-plan/rules']);
    }

    onRefresh(): void {
        this.loadMealPlans();
    }

    onViewModeChange(mode: 'week' | 'month'): void {
        this.viewMode = mode;
        // TODO: Implement view mode change logic
    }

    onDateChange(date: Date): void {
        this.selectedDate = date;
        // TODO: Implement date change logic
    }

    getPreviousWeekDate(): Date {
        return new Date(this.selectedDate.getTime() - 7 * 24 * 60 * 60 * 1000);
    }

    getNextWeekDate(): Date {
        return new Date(this.selectedDate.getTime() + 7 * 24 * 60 * 60 * 1000);
    }

    getMealTypeIcon(mealType: string): string {
        switch (mealType.toLowerCase()) {
            case 'breakfast':
                return 'wb_sunny';
            case 'lunch':
                return 'restaurant';
            case 'dinner':
                return 'local_dining';
            case 'snack':
                return 'local_cafe';
            default:
                return 'restaurant';
        }
    }

    getMealTypeColor(mealType: string): string {
        switch (mealType.toLowerCase()) {
            case 'breakfast':
                return 'primary';
            case 'lunch':
                return 'accent';
            case 'dinner':
                return 'warn';
            case 'snack':
                return 'primary';
            default:
                return 'primary';
        }
    }

    getWeekDays(): Date[] {
        const days: Date[] = [];
        const startOfWeek = new Date(this.selectedDate);
        startOfWeek.setDate(startOfWeek.getDate() - startOfWeek.getDay());

        for (let i = 0; i < 7; i++) {
            const day = new Date(startOfWeek);
            day.setDate(startOfWeek.getDate() + i);
            days.push(day);
        }

        return days;
    }

    getPlansForDate(date: Date): MealPlanResponseModel[] {
        return this.filteredPlans.filter(plan =>
            plan.date.toDateString() === date.toDateString()
        );
    }

    isToday(date: Date): boolean {
        return date.toDateString() === new Date().toDateString();
    }

    isSelectedDate(date: Date): boolean {
        return date.toDateString() === this.selectedDate.toDateString();
    }
} 