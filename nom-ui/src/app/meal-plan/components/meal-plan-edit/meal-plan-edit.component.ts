import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwButtonComponent, AmwDatepickerComponent, AmwCardComponent, AmwProgressBarComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanResponseModel } from '../../models/meal-plan-response.model';
import { MealPlanUpdateRequestModel } from '../../models/meal-plan-update-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-meal-plan-edit',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwProgressBarComponent,
    AmwInputComponent,
    AmwSelectComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwDatepickerComponent,
    AmwCardComponent,
    AmwValidationTooltipDirective
  ],
  templateUrl: './meal-plan-edit.component.html',
  styleUrls: ['./meal-plan-edit.component.scss']
})
export class MealPlanEditComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private mealPlanService = inject(MealPlanService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  mealPlanForm: FormGroup = this.nonNullableFb.group({
    recipeName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    mealType: ['dinner', [Validators.required]],
    date: [new Date(), [Validators.required]],
    description: ['', [Validators.maxLength(500)]]
  });

  isLoading = signal(false);
  mealPlanId = signal(0);
  mealPlan = signal<MealPlanResponseModel | null>(null);
  validationContext!: ValidationContext;

  mealTypes = [
    { value: 'breakfast', label: 'Breakfast' },
    { value: 'lunch', label: 'Lunch' },
    { value: 'dinner', label: 'Dinner' },
    { value: 'snack', label: 'Snack' }
  ];

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.mealPlanId.set(+params['id']);
      this.loadMealPlan();
    });

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
        this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
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

      this.mealPlanService.updateMealPlan(this.mealPlanId(), updateRequest)
        .pipe(loading('Updating meal plan...'))
        .subscribe({
          next: () => {
            this.isLoading.set(false);
            this.notificationService.success('Meal plan updated successfully!');
            this.router.navigate(['/meal-plan', this.mealPlanId()]);
          },
          error: (error) => {
            this.isLoading.set(false);
            console.error('Error updating meal plan:', error);
            this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.SAVE_FAILED);
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