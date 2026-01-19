// File: nom-ui/src/app/user/privacy-settings/privacy-settings.component.ts

import { Component, OnInit, ViewEncapsulation, inject, signal } from '@angular/core';

import { ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AmwButtonComponent, AmwCardComponent, AmwToggleComponent, AmwIconComponent, AmwProgressSpinnerComponent, DialogService } from 'angular-material-wrap';

import { ConsentModel } from '../../../privacy/models/consent.model';
import { PrivacyService } from '../../../privacy/services/privacy.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { UpdateConsentRequest } from '../../../privacy/models/update-consent.request';

@Component({
  selector: 'nom-privacy-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatProgressBarModule,
    AmwButtonComponent,
    AmwCardComponent,
    AmwToggleComponent,
    AmwIconComponent,
    AmwProgressSpinnerComponent,
  ],
  templateUrl: './privacy-settings.component.html',
  styleUrls: ['./privacy-settings.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PrivacySettingsComponent implements OnInit {
  private fb = inject(FormBuilder);
  private privacyService = inject(PrivacyService);
  private notificationService = inject(NotificationService);
  private dialogService = inject(DialogService);

  consentForm: FormGroup;
  privacyForm: FormGroup;
  isLoading = signal(false);
  isSubmitting = signal(false);

  // This would be fetched from a reference data service in a real app
  availableConsents: ConsentModel[] = [
    {
      consentTypeRefId: 8000,
      isConsented: false,
      name: 'Analytics',
      description:
        'Allow us to use your data for internal analytics to improve our service.',
    },
    {
      consentTypeRefId: 8001,
      isConsented: false,
      name: 'Marketing',
      description:
        'Receive marketing communications, newsletters, and special offers.',
    },
    {
      consentTypeRefId: 8002,
      isConsented: false,
      name: 'Personalization',
      description:
        'Allow us to personalize your content and meal recommendations.',
    },
  ];



  constructor() {
    this.consentForm = this.fb.group({});
    this.privacyForm = this.fb.group({});
  }

  ngOnInit(): void {
    // In a real app, you would first fetch the user's current consent settings
    // and then build the form. For this example, we'll build it with defaults.
    this.availableConsents.forEach((consent) => {
      this.privacyForm.addControl(
        consent.name!,
        this.fb.control(consent.isConsented)
      );
    });
  }

  onSubmit(): void {
    if (this.privacyForm.invalid) {
      return;
    }
    this.isSubmitting.set(true);
  }

  onConsentSubmit(): void {
    if (this.consentForm.invalid) {
      return;
    }
    this.isLoading.set(true);

    const formValues = this.consentForm.value;
    const consentRequests: ConsentModel[] = this.availableConsents.map(
      (consent) => ({
        consentTypeRefId: consent.consentTypeRefId,
        isConsented: formValues[consent.name!],
      })
    );

    const request: UpdateConsentRequest = { consents: consentRequests };

    this.privacyService.updateConsent(request).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.notificationService.success(
          'Your privacy settings have been updated.'
        );
      },
      error: (err) => {
        this.isLoading.set(false);
        this.notificationService.error(
          err.message || 'Failed to update settings.'
        );
      },
    });
  }

  onExportData(): void {
    this.isLoading.set(true);
    this.privacyService.requestDataExport({ format: 'json' }).subscribe({
      next: (res) => {
        this.isLoading.set(false);
        this.notificationService.info(
          `Data export requested. You will be notified when it's ready. Request ID: ${res.requestId}`
        );
      },
      error: (err) => {
        this.isLoading.set(false);
        this.notificationService.error(
          err.message || 'Failed to request data export.'
        );
      },
    });
  }

  onDeleteAccount(): void {
    this.dialogService.confirm(
      'Are you sure you want to permanently delete your account and all associated data? This action cannot be undone.',
      'Confirm Account Deletion'
    ).subscribe((confirmed) => {
      if (confirmed) {
        // User confirmed the action
        this.isLoading.set(true);
        this.privacyService.requestDataDeletion({ confirm: true }).subscribe({
          next: (res) => {
            this.isLoading.set(false);
            this.notificationService.warning(
              `Account deletion process initiated. You will be logged out shortly. Request ID: ${res.requestId}`
            );
            // Here you would typically log the user out.
          },
          error: (err) => {
            this.isLoading.set(false);
            this.notificationService.error(
              err.message || 'Failed to initiate account deletion.'
            );
          },
        });
      }
    });
  }
}
