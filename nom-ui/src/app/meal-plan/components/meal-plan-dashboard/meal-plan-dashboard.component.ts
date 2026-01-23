import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { ReactiveFormsModule, FormsModule, FormControl } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

import { AmwInputComponent, AmwButtonComponent, AmwIconComponent, AmwChipComponent, AmwButtonToggleGroupComponent, AmwButtonToggleComponent, AmwDialogService } from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'nom-meal-plan-dashboard',
    standalone: true,
    imports: [
        DatePipe,
        ReactiveFormsModule,
        FormsModule,
        AmwInputComponent,
        AmwButtonComponent,
        AmwIconComponent,
        AmwChipComponent,
        AmwButtonToggleGroupComponent,
        AmwButtonToggleComponent
    ],
    templateUrl: './meal-plan-dashboard.component.html',
    styleUrls: ['./meal-plan-dashboard.component.scss']
})
export class MealPlanDashboardComponent implements OnInit {
    private router = inject(Router);
    private mealPlanService = inject(MealPlanService);
    private notificationService = inject(NotificationService);
    private dialogService = inject(AmwDialogService);

    mealPlans = signal<MealPlanResponseModel[]>([]);
    filteredPlans = signal<MealPlanResponseModel[]>([]);
    isLoading = signal(true);
    error = signal<string | null>(null);
    searchControl = new FormControl('');
    searchTerm = signal('');
    viewMode = signal<'week' | 'month'>('week');
    selectedDate = signal(new Date());





    ngOnInit(): void {
        this.loadMealPlans();
        this.setupSearchFilter();
    }

    setupSearchFilter(): void {
        this.searchControl.valueChanges.pipe(
            debounceTime(300),
            distinctUntilChanged()
        ).subscribe(searchTerm => {
            this.searchTerm.set(searchTerm || '');
            this.filterPlans(this.searchTerm());
        });
    }

    onSearch(): void {
        this.filterPlans(this.searchTerm());
    }

    clearSearch(): void {
        this.searchTerm.set('');
        this.filterPlans('');
    }

    loadMealPlans(): void {
        this.isLoading.set(true);
        this.error.set(null);

        this.mealPlanService.getMealPlans().subscribe({
            next: (mealPlans) => {
                this.mealPlans.set(mealPlans);
                this.filteredPlans.set([...this.mealPlans()]);
                this.isLoading.set(false);
            },
            error: (error) => {
                console.error('Error loading meal plans:', error);
                this.error.set('Failed to load meal plans');
                this.isLoading.set(false);
            }
        });
    }

    filterPlans(searchTerm: string): void {
        if (!searchTerm.trim()) {
            this.filteredPlans.set([...this.mealPlans()]);
        } else {
            const term = searchTerm.toLowerCase();
            this.filteredPlans.set(this.mealPlans().filter(plan =>
                plan.recipeName?.toLowerCase().includes(term) ||
                plan.title?.toLowerCase().includes(term) ||
                plan.description?.toLowerCase().includes(term)
            ));
        }
    }

    onCreatePlan(): void {
        this.router.navigate(['/meal-plan/create']);
    }

    createMealPlan(): void {
        this.onCreatePlan();
    }

    onViewPlan(planId: number): void {
        this.router.navigate(['/meal-plan', planId]);
    }

    onEditPlan(planId: number): void {
        this.router.navigate(['/meal-plan', planId, 'edit']);
    }

    onDeletePlan(planId: number): void {
        this.dialogService.confirm(
            'Are you sure you want to delete this meal plan? This action cannot be undone.',
            'Delete Meal Plan'
        ).subscribe(result => {
            if (result) {
                this.mealPlanService.deleteMealPlan(planId).subscribe({
                    next: () => {
                        this.notificationService.success('Meal plan deleted successfully');
                        this.loadMealPlans();
                    },
                    error: (error) => {
                        console.error('Error deleting meal plan:', error);
                        this.notificationService.error('Failed to delete meal plan');
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

    onViewModeChange(event?: any): void {
        // viewMode is already bound via ngModel, so just need to handle the event
        if (event?.value) {
            this.viewMode.set(event.value);
        }
    }

    onDateChange(date: Date): void {
        this.selectedDate.set(date);
    }

    getPreviousWeekDate(): Date {
        const date = new Date(this.selectedDate());
        date.setDate(date.getDate() - 7);
        return date;
    }

    getNextWeekDate(): Date {
        const date = new Date(this.selectedDate());
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

    getWeekDays(): { date: Date; name: string }[] {
        const days: { date: Date; name: string }[] = [];
        const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
        const startOfWeek = new Date(this.selectedDate());
        startOfWeek.setDate(startOfWeek.getDate() - startOfWeek.getDay());

        for (let i = 0; i < 7; i++) {
            const day = new Date(startOfWeek);
            day.setDate(day.getDate() + i);
            days.push({
                date: day,
                name: dayNames[day.getDay()]
            });
        }

        return days;
    }

    getPlansForDay(date: Date): MealPlanResponseModel[] {
        return this.getPlansForDate(date);
    }

    editMealPlan(plan: MealPlanResponseModel): void {
        this.onEditPlan(plan.id);
    }

    deleteMealPlan(plan: MealPlanResponseModel): void {
        this.onDeletePlan(plan.id);
    }

    getCurrentMonth(): string {
        const months = ['January', 'February', 'March', 'April', 'May', 'June',
                       'July', 'August', 'September', 'October', 'November', 'December'];
        return `${months[this.selectedDate().getMonth()]} ${this.selectedDate().getFullYear()}`;
    }

    getPlansForDate(date: Date): MealPlanResponseModel[] {
        const dateString = date.toISOString().split('T')[0];
        return this.filteredPlans().filter(plan => {
            const planDate = plan.date instanceof Date ? plan.date.toISOString().split('T')[0] : plan.date;
            return planDate === dateString;
        });
    }

    isToday(date: Date): boolean {
        const today = new Date();
        return date.toDateString() === today.toDateString();
    }

    isSelectedDate(date: Date): boolean {
        return date.toDateString() === this.selectedDate().toDateString();
    }

    getTotalMeals(): number {
        return this.filteredPlans().length;
    }

    getUpcomingMeals(): number {
        const today = new Date();
        const nextWeek = new Date(today);
        nextWeek.setDate(today.getDate() + 7);

        return this.filteredPlans().filter(plan => {
            const planDate = plan.date instanceof Date ? plan.date : new Date(plan.date);
            return planDate >= today && planDate <= nextWeek;
        }).length;
    }
} 