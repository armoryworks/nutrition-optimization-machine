import { Component, OnInit, CUSTOM_ELEMENTS_SCHEMA, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { MealPlanUpdateRequestModel } from '../../models/meal-plan-update-request.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'nom-meal-plan-edit',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatDatepickerModule,
    MatNativeDateModule,
    BaseFormComponent
],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './meal-plan-edit.component.html',
  styleUrls: ['./meal-plan-edit.component.scss']
})
export class MealPlanEditComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private mealPlanService = inject(MealPlanService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);

  mealPlanForm: FormGroup = this.nonNullableFb.group({
    recipeName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    mealType: ['dinner', [Validators.required]],
    date: [new Date(), [Validators.required]],
    description: ['', [Validators.maxLength(500)]]
  });

  isLoading = signal(false);
  mealPlanId = signal(0);
  mealPlan = signal<MealPlanResponseModel | null>(null);

  mealTypes = [
    { value: 'breakfast', label: 'Breakfast' },
    { value: 'lunch', label: 'Lunch' },
    { value: 'dinner', label: 'Dinner' },
    { value: 'snack', label: 'Snack' }
  ];

  formConfig: BaseFormConfig = {
    title: 'Edit Meal Plan',
    subtitle: 'Update your meal plan information',
    submitText: 'Update Meal Plan',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px',
  };

  constructor() {
    // Form is now initialized at declaration
  }

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.mealPlanId.set(+params['id']);
      this.loadMealPlan();
    });
  }

  loadMealPlan(): void {
    this.isLoading.set(true);

    this.mealPlanService.getMealPlan(this.mealPlanId()).subscribe({
      next: (mealPlan) => {
        this.mealPlan.set(mealPlan);
        this.mealPlanForm.patchValue({
          recipeName: mealPlan.recipeName,
          mealType: mealPlan.mealType,
          date: new Date(mealPlan.date),
          description: mealPlan.description || ''
        });
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading meal plan:', error);
        this.snackBar.open('Failed to load meal plan details', 'Close', {
          duration: 5000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
        this.router.navigate(['/meal-plan']);
      }
    });
  }

  onSubmit(): void {
    if (this.mealPlanForm.valid && this.mealPlan()) {
      this.isLoading.set(true);

      const updateRequest = new MealPlanUpdateRequestModel({
        recipeName: this.mealPlanForm.value.recipeName,
        mealTypeId: this.getMealTypeId(this.mealPlanForm.value.mealType),
        date: this.mealPlanForm.value.date,
        description: this.mealPlanForm.value.description
      });

      this.mealPlanService.updateMealPlan(this.mealPlanId(), updateRequest).subscribe({
        next: () => {
          this.isLoading.set(false);
          this.snackBar.open('Meal plan updated successfully!', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
          this.router.navigate(['/meal-plan', this.mealPlanId()]);
        },
        error: (error) => {
          this.isLoading.set(false);
          console.error('Error updating meal plan:', error);
          this.snackBar.open('Failed to update meal plan. Please try again.', 'Close', {
            duration: 5000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/meal-plan', this.mealPlanId()]);
  }

  private getMealTypeId(mealType: string): number {
    switch (mealType) {
      case 'breakfast': return 1;
      case 'lunch': return 2;
      case 'dinner': return 3;
      case 'snack': return 4;
      default: return 3; // Default to dinner
    }
  }
} 