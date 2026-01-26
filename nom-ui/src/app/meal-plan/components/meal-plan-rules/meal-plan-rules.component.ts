import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AmwInputComponent, AmwSelectComponent, AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwDialogService, AmwProgressBarComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';
import { MealPlanService } from '../../services/meal-plan.service';
import { MealPlanRuleCreateRequestModel } from '../../models/meal-plan-rule-create-request.model';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { MealPlanRuleResponseModel } from '../../models/meal-plan-rule-response.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-meal-plan-rules',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwProgressBarComponent,
    AmwInputComponent,
    AmwSelectComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwValidationTooltipDirective,
  ],
  templateUrl: './meal-plan-rules.component.html',
  styleUrls: ['./meal-plan-rules.component.scss']
})
export class MealPlanRulesComponent implements OnInit, OnDestroy {
  private mealPlanService = inject(MealPlanService);
  private referenceDataService = inject(ReferenceDataService);
  private router = inject(Router);
  private nonNullableFb = inject(NonNullableFormBuilder);
  private notificationService = inject(NotificationService);
  private dialogService = inject(AmwDialogService);
  private userInfoService = inject(UserInfoService);
  private validationService = inject(AmwValidationService);

  rules = signal<MealPlanRuleResponseModel[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);
  ruleForm: FormGroup;
  isAddingRule = signal(false);
  validationContext!: ValidationContext;

  mealTypes = signal<ReferenceItemModel[]>([]);
  daysOfWeek = signal<ReferenceItemModel[]>([]);

  getMealTypeOptions(): { value: number; label: string }[] {
    return this.mealTypes().map(type => ({ value: type.id, label: type.name }));
  }

  getDayOfWeekOptions(): { value: number; label: string }[] {
    return this.daysOfWeek().map(day => ({ value: day.id, label: day.name }));
  }

  constructor() {
    this.ruleForm = this.nonNullableFb.group({
      dayOfWeekId: ['', [Validators.required]],
      mealTypeId: ['', [Validators.required]],
      queryFilterString: ['']
    });
  }

  ngOnInit(): void {
    this.loadRules();
    this.loadReferenceData();

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Day of week validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'dayOfWeekId-required',
      message: 'Day of week is required',
      severity: 'error',
      field: 'dayOfWeekId',
      control: this.ruleForm.get('dayOfWeekId') ?? undefined,
      validator: () => !this.ruleForm.get('dayOfWeekId')?.hasError('required')
    });

    // Meal type validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'mealTypeId-required',
      message: 'Meal type is required',
      severity: 'error',
      field: 'mealTypeId',
      control: this.ruleForm.get('mealTypeId') ?? undefined,
      validator: () => !this.ruleForm.get('mealTypeId')?.hasError('required')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  private loadReferenceData(): void {
    // Load meal types from backend
    this.referenceDataService.getMealTypes().subscribe({
      next: (mealTypes) => {
        this.mealTypes.set(mealTypes);
      },
      error: (error) => {
        console.error('Error loading meal types:', error);
        this.mealTypes.set([]);
      }
    });

    // Load days of week
    this.referenceDataService.getDaysOfWeek().subscribe({
      next: (days) => {
        this.daysOfWeek.set(days);
      },
      error: (error) => {
        console.error('Error loading days of week:', error);
        this.daysOfWeek.set([]);
      }
    });
  }

  loadRules(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.mealPlanService.getRules().subscribe({
      next: (rules: MealPlanRuleResponseModel[]) => {
        this.rules.set(rules);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        console.error('Error loading rules:', error);
        this.error.set(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
        this.isLoading.set(false);
        this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.LOAD_FAILED);
      }
    });
  }

  onSubmit(): void {
    if (this.ruleForm.invalid) {
      this.notificationService.warning('Please fill in all required fields');
      return;
    }

    this.isAddingRule.set(true);
    const formValue = this.ruleForm.value;

    const request: MealPlanRuleCreateRequestModel = {
      id: 0,
      householdId: this.getCurrentHouseholdId(),
      name: formValue.name || 'Meal Plan Rule',
      dayOfWeekId: formValue.dayOfWeekId,
      mealTypeId: formValue.mealTypeId,
      queryFilterString: formValue.queryFilterString || undefined,
      isActive: true
    };

    this.mealPlanService.createRule(request)
      .pipe(loading('Creating rule...'))
      .subscribe({
        next: () => {
          this.notificationService.success('Rule created successfully!');
          this.ruleForm.reset();
          this.isAddingRule.set(false);
          this.loadRules();
        },
        error: (error) => {
          console.error('Error creating rule:', error);
          this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.SAVE_FAILED);
          this.isAddingRule.set(false);
        }
      });
  }

  editRule(rule: MealPlanRuleResponseModel): void {
    // Navigate to edit rule page
    this.router.navigate(['/meal-plan/rules', rule.id, 'edit']);
  }

  deleteRule(rule: MealPlanRuleResponseModel): void {
    this.dialogService.confirm(
      'Are you sure you want to delete this meal plan rule?',
      'Delete Rule'
    ).subscribe(result => {
      if (result) {
        this.mealPlanService.deleteRule(rule.id)
          .pipe(loading('Deleting rule...'))
          .subscribe({
            next: () => {
              this.notificationService.success('Rule deleted successfully');
              this.loadRules();
            },
            error: (error) => {
              console.error('Error deleting rule:', error);
              this.notificationService.error(ERROR_MESSAGES.MEAL_PLAN.DELETE_FAILED);
            }
          });
      }
    });
  }

  getMealTypeName(mealTypeId: number): string {
    const mealType = this.mealTypes().find(type => type.id === mealTypeId);
    return mealType?.name || 'Unknown';
  }

  getDayOfWeekName(dayOfWeekId: number): string {
    const day = this.daysOfWeek().find(d => d.id === dayOfWeekId);
    return day?.name || 'Unknown';
  }

  onBack(): void {
    this.router.navigate(['/meal-plan']);
  }

  private getCurrentHouseholdId(): number {
    return this.userInfoService.getHouseholdId();
  }

  private getCurrentUserId(): number {
    return this.userInfoService.getPersonId() || 1;
  }
} 