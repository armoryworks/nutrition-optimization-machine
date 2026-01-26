import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwProgressSpinnerComponent,
  AmwInputComponent,
  AmwTextareaComponent,
  AmwCheckboxComponent,
} from 'angular-material-wrap';

import { HouseholdService } from '../../services/household.service';
import { HouseholdResponseModel } from '../../models/household-response.model';
import { HouseholdUpdateRequestModel } from '../../models/household-update-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-household-settings',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    AmwCardComponent,
    AmwButtonComponent,
    AmwProgressSpinnerComponent,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwCheckboxComponent,
  ],
  templateUrl: './household-settings.component.html',
  styleUrl: './household-settings.component.scss',
})
export class HouseholdSettingsComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private householdService = inject(HouseholdService);
  private notificationService = inject(NotificationService);

  // Signals
  householdId = signal<number>(0);
  household = signal<HouseholdResponseModel | null>(null);
  isLoading = signal(true);
  isSaving = signal(false);
  error = signal<string | null>(null);

  // Forms
  preferencesForm!: FormGroup;
  notificationForm!: FormGroup;
  privacyForm!: FormGroup;

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.householdId.set(+id);
        this.initializeForms();
        this.loadHousehold();
      } else {
        this.error.set('No household ID provided');
        this.isLoading.set(false);
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private initializeForms(): void {
    // Preferences form
    this.preferencesForm = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(100)]],
      description: ['', Validators.maxLength(500)],
    });

    // Notification settings form (placeholder - no backend support yet)
    this.notificationForm = this.fb.group({
      emailNotifications: [true],
      newMemberNotifications: [true],
      mealPlanReminders: [true],
      shoppingListUpdates: [true],
    });

    // Privacy settings form (placeholder - no backend support yet)
    this.privacyForm = this.fb.group({
      allowMemberInvites: [true],
      shareRecipes: [true],
      publicProfile: [false],
    });
  }

  private loadHousehold(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.householdService
      .getHousehold(this.householdId())
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (household: HouseholdResponseModel) => {
          this.household.set(household);
          this.populateForms(household);
        },
        error: (err: unknown) => {
          this.error.set(ERROR_MESSAGES.HOUSEHOLD.LOAD_FAILED);
          console.error('Error loading household:', err);
        },
      });
  }

  private populateForms(household: HouseholdResponseModel): void {
    // Populate preferences
    this.preferencesForm.patchValue({
      name: household.name,
      description: household.description || '',
    });

    // Note: Notification and privacy settings would be populated from backend
    // when those features are implemented
  }

  onSavePreferences(): void {
    if (this.preferencesForm.invalid) {
      this.notificationService.error('Please fix form errors before saving');
      return;
    }

    this.isSaving.set(true);

    const request: HouseholdUpdateRequestModel = {
      name: this.preferencesForm.value.name,
      description: this.preferencesForm.value.description || undefined,
    };

    this.householdService
      .updateHousehold(this.householdId(), request)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isSaving.set(false))
      )
      .subscribe({
        next: (household: HouseholdResponseModel) => {
          this.household.set(household);
          this.notificationService.success('Preferences saved successfully');
        },
        error: (err: unknown) => {
          console.error('Error saving preferences:', err);
          this.notificationService.error(ERROR_MESSAGES.HOUSEHOLD.SAVE_FAILED);
        },
      });
  }

  onSaveNotifications(): void {
    // Placeholder - would call backend API when implemented
    this.notificationService.info('Notification settings saved (placeholder - no backend support yet)');
  }

  onSavePrivacy(): void {
    // Placeholder - would call backend API when implemented
    this.notificationService.info('Privacy settings saved (placeholder - no backend support yet)');
  }

  onBack(): void {
    this.router.navigate(['/household', this.householdId()]);
  }

  onRetry(): void {
    this.loadHousehold();
  }
}
