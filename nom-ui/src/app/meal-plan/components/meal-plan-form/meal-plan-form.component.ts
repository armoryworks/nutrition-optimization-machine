import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, Validators, FormArray, ReactiveFormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import {
  AmwInputComponent,
  AmwSelectComponent,
  AmwTextareaComponent,
  AmwButtonComponent,
  AmwCheckboxComponent,
  AmwDatepickerComponent,
  AmwCardComponent,
  AmwIconComponent,
  AmwDialogService,
  AmwProgressBarComponent,
  AmwValidationTooltipDirective,
  AmwValidationService,
  ValidationContext
} from 'angular-material-wrap';
import { MealPlanReferenceService } from '../../services/meal-plan-reference.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
  selector: 'app-meal-plan-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwProgressBarComponent,
    AmwInputComponent,
    AmwSelectComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCheckboxComponent,
    AmwDatepickerComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwValidationTooltipDirective,
  ],
  templateUrl: './meal-plan-form.component.html',
  styleUrls: ['./meal-plan-form.component.scss']
})
export class MealPlanFormComponent implements OnInit, OnDestroy {
  private fb = inject(FormBuilder);
  private mealPlanReferenceService = inject(MealPlanReferenceService);
  private notificationService = inject(NotificationService);
  private dialogService = inject(AmwDialogService);
  private validationService = inject(AmwValidationService);

  mealPlanForm!: FormGroup;
  isEditMode = false;
  isSubmitting = signal(false);
  validationContext!: ValidationContext;

  // Reference data
  daysOfWeek: any[] = [];
  mealTypes: any[] = [];

  // Make constants available in template
  readonly REFERENCE_IDS = REFERENCE_IDS;

  private destroy$ = new Subject<void>();

  constructor() {
    this.initializeForm();
  }

  ngOnInit(): void {
    this.loadReferenceData();
    this.setupFormListeners();

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Name validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-required',
      message: 'Plan name is required',
      severity: 'error',
      field: 'name',
      control: this.mealPlanForm.get('name') ?? undefined,
      validator: () => !this.mealPlanForm.get('name')?.hasError('required')
    });

    // Start date validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'startDate-required',
      message: 'Start date is required',
      severity: 'error',
      field: 'startDate',
      control: this.mealPlanForm.get('startDate') ?? undefined,
      validator: () => !this.mealPlanForm.get('startDate')?.hasError('required')
    });

    // End date validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'endDate-required',
      message: 'End date is required',
      severity: 'error',
      field: 'endDate',
      control: this.mealPlanForm.get('endDate') ?? undefined,
      validator: () => !this.mealPlanForm.get('endDate')?.hasError('required')
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();

    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  get mealAssignmentsArray(): FormArray {
    return this.mealPlanForm.get('mealAssignments') as FormArray;
  }

  getDaysOfWeekOptions(): { value: number; label: string }[] {
    return this.daysOfWeek.map(day => ({ value: day.referenceId, label: day.referenceName }));
  }

  getMealTypeOptions(): { value: number; label: string }[] {
    return this.mealTypes.map(type => ({ value: type.referenceId, label: type.referenceName }));
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
      this.isSubmitting.set(true);

      const formValue = this.mealPlanForm.value;
      console.log('Meal plan form submitted:', formValue);

      // Simulate API call
      setTimeout(() => {
        this.isSubmitting.set(false);
        this.notificationService.success(`Meal plan ${this.isEditMode ? 'updated' : 'created'} successfully!`);
        this.resetForm();
      }, 2000);
    }
  }

  onCancel(): void {
    if (this.mealPlanForm.dirty) {
      this.dialogService.confirm(
        'Are you sure you want to cancel? All changes will be lost.',
        'Cancel Changes'
      ).subscribe(result => {
        if (result) {
          this.resetForm();
        }
      });
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
