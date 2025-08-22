import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, Validators, FormArray } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { MealPlanReferenceService } from '../../services/meal-plan-reference.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

@Component({
    selector: 'app-meal-plan-form',
    template: `
    <div class="meal-plan-form">
      <div class="meal-plan-form__header">
        <h2>{{ isEditMode ? 'Edit Meal Plan' : 'Create New Meal Plan' }}</h2>
        <p class="subtitle">Plan your meals for the week ahead</p>
      </div>

      <form [formGroup]="mealPlanForm" (ngSubmit)="onSubmit()" class="meal-plan-form__content">
        <!-- Plan Information -->
        <div class="form-section">
          <h3>Plan Information</h3>
          
          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Plan Name</mat-label>
              <input matInput formControlName="name" placeholder="Enter meal plan name">
              <mat-error *ngIf="mealPlanForm.get('name')?.hasError('required')">
                Plan name is required
              </mat-error>
            </mat-form-field>
          </div>

          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Description</mat-label>
              <textarea matInput formControlName="description" rows="3" placeholder="Describe your meal plan"></textarea>
            </mat-form-field>
          </div>

          <div class="form-row">
            <mat-form-field class="form-field">
              <mat-label>Start Date</mat-label>
              <input matInput [matDatepicker]="startPicker" formControlName="startDate">
              <mat-datepicker-toggle matSuffix [for]="startPicker"></mat-datepicker-toggle>
              <mat-datepicker #startPicker></mat-datepicker>
              <mat-error *ngIf="mealPlanForm.get('startDate')?.hasError('required')">
                Start date is required
              </mat-error>
            </mat-form-field>

            <mat-form-field class="form-field">
              <mat-label>End Date</mat-label>
              <input matInput [matDatepicker]="endPicker" formControlName="endDate">
              <mat-datepicker-toggle matSuffix [for]="endPicker"></mat-datepicker-toggle>
              <mat-datepicker #endPicker></mat-datepicker>
              <mat-error *ngIf="mealPlanForm.get('endDate')?.hasError('required')">
                End date is required
              </mat-error>
            </mat-form-field>
          </div>
        </div>

        <!-- Weekly Schedule -->
        <div class="form-section">
          <h3>Weekly Schedule</h3>
          <p class="section-hint">Select which days of the week to include in your meal plan</p>
          
          <div class="days-grid">
            <div *ngFor="let day of daysOfWeek; let i = index" class="day-selector">
              <mat-checkbox [formControlName]="'day' + i" [checked]="isDaySelected(i)">
                {{ day.referenceName }}
              </mat-checkbox>
            </div>
          </div>
        </div>

        <!-- Meal Assignments -->
        <div class="form-section">
          <h3>Meal Assignments</h3>
          <p class="section-hint">Assign meals to specific days and meal types</p>
          
          <div formArrayName="mealAssignments" class="meal-assignments">
            <div *ngFor="let mealAssignment of mealAssignmentsArray.controls; let i = index" 
                 [formGroupName]="i" 
                 class="meal-assignment">
              
              <div class="meal-assignment__header">
                <h4>Meal {{ i + 1 }}</h4>
                <button mat-icon-button type="button" (click)="removeMealAssignment(i)" 
                        [disabled]="mealAssignmentsArray.length <= 1">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>

              <div class="meal-assignment__content">
                <div class="form-row">
                  <mat-form-field class="form-field">
                    <mat-label>Day of Week</mat-label>
                    <mat-select formControlName="dayOfWeekId" [showDescription]="true">
                      <mat-option *ngFor="let day of daysOfWeek" [value]="day.referenceId">
                        {{ day.referenceName }}
                      </mat-option>
                    </mat-select>
                    <mat-error *ngIf="mealAssignment.get('dayOfWeekId')?.hasError('required')">
                      Day of week is required
                    </mat-error>
                  </mat-form-field>

                  <mat-form-field class="form-field">
                    <mat-label>Meal Type</mat-label>
                    <mat-select formControlName="mealTypeId" [showDescription]="true">
                      <mat-option *ngFor="let mealType in mealTypes" [value]="mealType.referenceId">
                        {{ mealType.referenceName }}
                      </mat-option>
                    </mat-select>
                    <mat-error *ngIf="mealAssignment.get('mealTypeId')?.hasError('required')">
                      Meal type is required
                    </mat-error>
                  </mat-form-field>
                </div>

                <div class="form-row">
                  <mat-form-field class="form-field">
                    <mat-label>Recipe Name</mat-label>
                    <input matInput formControlName="recipeName" placeholder="Enter recipe name or description">
                    <mat-error *ngIf="mealAssignment.get('recipeName')?.hasError('required')">
                      Recipe name is required
                    </mat-error>
                  </mat-form-field>

                  <mat-form-field class="form-field">
                    <mat-label>Notes</mat-label>
                    <textarea matInput formControlName="notes" rows="2" placeholder="Optional notes or modifications"></textarea>
                  </mat-form-field>
                </div>
              </div>
            </div>
          </div>

          <div class="add-meal-section">
            <button mat-stroked-button type="button" (click)="addMealAssignment()">
              <mat-icon>add</mat-icon>
              Add Another Meal
            </button>
          </div>
        </div>

        <!-- Shopping List Integration -->
        <div class="form-section">
          <h3>Shopping List Integration</h3>
          
          <div class="form-row">
            <mat-checkbox formControlName="generateShoppingList">
              Generate shopping list from this meal plan
            </mat-checkbox>
          </div>

          <div class="form-row" *ngIf="mealPlanForm.get('generateShoppingList')?.value">
            <mat-form-field class="form-field">
              <mat-label>Shopping List Name</mat-label>
              <input matInput formControlName="shoppingListName" placeholder="Enter shopping list name">
            </mat-form-field>
          </div>
        </div>

        <!-- Form Actions -->
        <div class="form-actions">
          <button mat-button type="button" (click)="onCancel()">Cancel</button>
          <button mat-raised-button color="primary" type="submit" [disabled]="mealPlanForm.invalid || isSubmitting">
            <mat-icon *ngIf="isSubmitting" class="spinning">refresh</mat-icon>
            {{ isSubmitting ? 'Saving...' : (isEditMode ? 'Update Meal Plan' : 'Create Meal Plan') }}
          </button>
        </div>
      </form>

      <!-- Plan Summary -->
      <div class="plan-summary" *ngIf="hasMealAssignments()">
        <h4>Meal Plan Summary</h4>
        <div class="summary-content">
          <div class="summary-item">
            <strong>Total Meals:</strong> {{ mealAssignmentsArray.length }}
          </div>
          <div class="summary-item">
            <strong>Days Covered:</strong> {{ getUniqueDaysCount() }}
          </div>
          <div class="summary-item">
            <strong>Meal Types:</strong> {{ getUniqueMealTypesCount() }}
          </div>
        </div>
      </div>
    </div>
  `,
    styleUrls: ['./meal-plan-form.component.scss']
})
export class MealPlanFormComponent implements OnInit, OnDestroy {
    mealPlanForm!: FormGroup;
    isEditMode = false;
    isSubmitting = false;

    // Reference data
    daysOfWeek: any[] = [];
    mealTypes: any[] = [];

    // Make constants available in template
    readonly REFERENCE_IDS = REFERENCE_IDS;

    private destroy$ = new Subject<void>();

    constructor(
        private fb: FormBuilder,
        private mealPlanReferenceService: MealPlanReferenceService
    ) {
        this.initializeForm();
    }

    ngOnInit(): void {
        this.loadReferenceData();
        this.setupFormListeners();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    get mealAssignmentsArray(): FormArray {
        return this.mealPlanForm.get('mealAssignments') as FormArray;
    }

    private initializeForm(): void {
        this.mealPlanForm = this.fb.group({
            name: ['', Validators.required],
            description: [''],
            startDate: [null, Validators.required],
            endDate: [null, Validators.required],
            day0: [false], // Monday
            day1: [false], // Tuesday
            day2: [false], // Wednesday
            day3: [false], // Thursday
            day4: [false], // Friday
            day5: [false], // Saturday
            day6: [false], // Sunday
            mealAssignments: this.fb.array([
                this.createMealAssignmentForm()
            ]),
            generateShoppingList: [false],
            shoppingListName: ['']
        });
    }

    private createMealAssignmentForm(): FormGroup {
        return this.fb.group({
            dayOfWeekId: [null, Validators.required],
            mealTypeId: [null, Validators.required],
            recipeName: ['', Validators.required],
            notes: ['']
        });
    }

    private loadReferenceData(): void {
        // Load meal types and days of week in bulk for performance
        this.mealPlanReferenceService.getMealPlanReferencesBulk()
            .pipe(takeUntil(this.destroy$))
            .subscribe(({ mealTypes, daysOfWeek }) => {
                this.mealTypes = mealTypes;
                this.daysOfWeek = daysOfWeek;
            });
    }

    private setupFormListeners(): void {
        // Listen for form changes
        this.mealPlanForm.valueChanges
            .pipe(takeUntil(this.destroy$))
            .subscribe(() => {
                // Form validation and UI updates can go here
            });
    }

    isDaySelected(dayIndex: number): boolean {
        return this.mealPlanForm.get('day' + dayIndex)?.value || false;
    }

    addMealAssignment(): void {
        this.mealAssignmentsArray.push(this.createMealAssignmentForm());
    }

    removeMealAssignment(index: number): void {
        if (this.mealAssignmentsArray.length > 1) {
            this.mealAssignmentsArray.removeAt(index);
        }
    }

    hasMealAssignments(): boolean {
        return this.mealAssignmentsArray.length > 0;
    }

    getUniqueDaysCount(): number {
        const days = this.mealAssignmentsArray.controls
            .map(control => control.get('dayOfWeekId')?.value)
            .filter(dayId => dayId !== null);
        return new Set(days).size;
    }

    getUniqueMealTypesCount(): number {
        const mealTypes = this.mealAssignmentsArray.controls
            .map(control => control.get('mealTypeId')?.value)
            .filter(mealTypeId => mealTypeId !== null);
        return new Set(mealTypes).size;
    }

    onSubmit(): void {
        if (this.mealPlanForm.valid) {
            this.isSubmitting = true;

            const formValue = this.mealPlanForm.value;
            console.log('Meal plan form submitted:', formValue);

            // Simulate API call
            setTimeout(() => {
                this.isSubmitting = false;
                alert(`Meal plan ${this.isEditMode ? 'updated' : 'created'} successfully!`);
                this.resetForm();
            }, 2000);
        }
    }

    onCancel(): void {
        if (this.mealPlanForm.dirty) {
            if (confirm('Are you sure you want to cancel? All changes will be lost.')) {
                this.resetForm();
            }
        } else {
            this.resetForm();
        }
    }

    private resetForm(): void {
        this.mealPlanForm.reset({
            day0: false,
            day1: false,
            day2: false,
            day3: false,
            day4: false,
            day5: false,
            day6: false,
            generateShoppingList: false
        });

        // Reset meal assignments array
        while (this.mealAssignmentsArray.length !== 0) {
            this.mealAssignmentsArray.removeAt(0);
        }
        this.mealAssignmentsArray.push(this.createMealAssignmentForm());
    }
}
