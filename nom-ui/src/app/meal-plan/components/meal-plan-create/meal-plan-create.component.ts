import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
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
import { MealPlanCreateRequestModel } from '../../models/meal-plan-create-request.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'nom-meal-plan-create',
  standalone: true,
  imports: [
    CommonModule,
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
    BaseFormComponent,
  ],
  templateUrl: './meal-plan-create.component.html',
  styleUrls: ['./meal-plan-create.component.scss']
})
export class MealPlanCreateComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private mealPlanService = inject(MealPlanService);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);
  private userInfoService = inject(UserInfoService);

  mealPlanForm: FormGroup = this.nonNullableFb.group({
    recipeName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    mealType: ['dinner', [Validators.required]],
    date: [new Date(), [Validators.required]],
    description: ['', [Validators.maxLength(500)]],
    householdId: [1]
  });

  isLoading = false;

  mealTypes = [
    { value: 'breakfast', label: 'Breakfast' },
    { value: 'lunch', label: 'Lunch' },
    { value: 'dinner', label: 'Dinner' },
    { value: 'snack', label: 'Snack' }
  ];

  formConfig: BaseFormConfig = {
    title: 'Create Meal Plan',
    subtitle: 'Create a new meal plan to organize your meals',
    submitText: 'Create Meal Plan',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px',
  };

  constructor() {
    // Form is now initialized at declaration
  }

  ngOnInit(): void {
    // No need to set AuthorId - it will be handled by the backend
  }

  onSubmit(): void {
    if (this.mealPlanForm.valid) {
      this.isLoading = true;

      const createRequest = new MealPlanCreateRequestModel({
        recipeName: this.mealPlanForm.value.recipeName,
        mealType: this.mealPlanForm.value.mealType,
        date: this.mealPlanForm.value.date,
        description: this.mealPlanForm.value.description
      });

      this.mealPlanService.createMealPlan(createRequest).subscribe({
        next: (response) => {
          this.isLoading = false;
          this.snackBar.open('Meal plan created successfully!', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
          this.router.navigate(['/meal-plan', response.id]);
        },
        error: (error) => {
          this.isLoading = false;
          console.error('Error creating meal plan:', error);
          this.snackBar.open('Failed to create meal plan. Please try again.', 'Close', {
            duration: 5000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/meal-plan']);
  }
} 