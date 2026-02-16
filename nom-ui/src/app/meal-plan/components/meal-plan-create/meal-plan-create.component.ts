import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwButtonComponent, AmwDatepickerComponent, AmwProgressBarComponent, AmwIconComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanCreateRequestModel } from '../../models/meal-plan-create-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-meal-plan-create',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwProgressBarComponent,
    AmwInputComponent,
    AmwSelectComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwDatepickerComponent,
    AmwIconComponent,
    AmwValidationTooltipDirective
  ],
  templateUrl: './meal-plan-create.component.html',
  styleUrls: ['./meal-plan-create.component.scss']
})
export class MealPlanCreateComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private mealPlanService = inject(MealPlanService);
  private router = inject(Router);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  mealPlanForm: FormGroup = this.nonNullableFb.group({
    recipeName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    mealType: ['dinner', [Validators.required]],
    date: [new Date(), [Validators.required]],
    description: ['', [Validators.maxLength(500)]],
    householdId: [1]
  });

  isLoading = signal(false);
  validationContext!: ValidationContext;

  mealTypes = [
    { value: 'breakfast', label: 'Breakfast' },
    { value: 'lunch', label: 'Lunch' },
    { value: 'dinner', label: 'Dinner' },
    { value: 'snack', label: 'Snack' }
  ];

  private mealTypeIdMap: Record<string, number> = {
    breakfast: 1100,
    lunch: 1101,
    dinner: 1102,
    snack: 1103
  };

  ngOnInit(): void {
    // No need to set AuthorId - it will be handled by the backend

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Recipe name validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'recipeName-required',
      message: 'Recipe name is required',
      severity: 'error',
      field: 'recipeName',
      control: this.mealPlanForm.get('recipeName') ?? undefined,
      validator: () => !this.mealPlanForm.get('recipeName')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'recipeName-minlength',
      message: 'Recipe name must be at least 2 characters',
      severity: 'error',
      field: 'recipeName',
      control: this.mealPlanForm.get('recipeName') ?? undefined,
      validator: () => !this.mealPlanForm.get('recipeName')?.hasError('minlength')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'recipeName-maxlength',
      message: 'Recipe name cannot exceed 100 characters',
      severity: 'error',
      field: 'recipeName',
      control: this.mealPlanForm.get('recipeName') ?? undefined,
      validator: () => !this.mealPlanForm.get('recipeName')?.hasError('maxlength')
    });

    // Meal type validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'mealType-required',
      message: 'Meal type is required',
      severity: 'error',
      field: 'mealType',
      control: this.mealPlanForm.get('mealType') ?? undefined,
      validator: () => !this.mealPlanForm.get('mealType')?.hasError('required')
    });

    // Date validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'date-required',
      message: 'Date is required',
      severity: 'error',
      field: 'date',
      control: this.mealPlanForm.get('date') ?? undefined,
      validator: () => !this.mealPlanForm.get('date')?.hasError('required')
    });

    // Description validation (optional field)
    this.validationService.addViolation(this.validationContext.id, {
      id: 'description-maxlength',
      message: 'Description cannot exceed 500 characters',
      severity: 'error',
      field: 'description',
      control: this.mealPlanForm.get('description') ?? undefined,
      validator: () => !this.mealPlanForm.get('description')?.hasError('maxlength')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  onSubmit(): void {
    if (this.mealPlanForm.valid) {
      this.isLoading.set(true);

      const formValue = this.mealPlanForm.value;
      const createRequest = new MealPlanCreateRequestModel({
        title: formValue.recipeName,
        mealTypeId: this.mealTypeIdMap[formValue.mealType] || 3,
        date: formValue.date,
        notes: formValue.description,
        householdId: formValue.householdId || 1
      });

      this.mealPlanService.createMealPlan(createRequest)
        .pipe(loading('Creating meal plan...'))
        .subscribe({
          next: (response) => {
            this.isLoading.set(false);
            this.notificationService.success('Meal plan created successfully!');
            this.router.navigate(['/meal-plan', response.id]);
          },
          error: (error) => {
            this.isLoading.set(false);
            console.error('Error creating meal plan:', error);
            this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.SAVE_FAILED);
          }
        });
    }
  }

  onCancel(): void {
    this.router.navigate(['/meal-plan']);
  }
} 