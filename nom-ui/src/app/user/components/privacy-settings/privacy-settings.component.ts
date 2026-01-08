// File: nom-ui/src/app/user/privacy-settings/privacy-settings.component.ts

import { Component, OnInit, ViewEncapsulation, inject, signal } from '@angular/core';

import { ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog'; // Import MatDialog and MatDialogModule
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ConsentModel } from '../../../privacy/models/consent.model';
import { PrivacyService } from '../../../privacy/services/privacy.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { UpdateConsentRequest } from '../../../privacy/models/update-consent.request';
import { ConfirmationDialogComponent } from '../../../common/components/confirmation-dialog/confirmation-dialog.component';

@Component({
  selector: 'nom-privacy-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatSlideToggleModule,
    MatButtonModule,
    MatDividerModule,
    MatProgressBarModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule
],
  templateUrl: './privacy-settings.component.html',
  styleUrls: ['./privacy-settings.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PrivacySettingsComponent implements OnInit {
  private fb = inject(FormBuilder);
  private privacyService = inject(PrivacyService);
  private notificationService = inject(NotificationService);
  private dialog = inject(MatDialog);

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
    const dialogRef = this.dialog.open(ConfirmationDialogComponent, {
      width: '400px',
      data: {
        title: 'Confirm Account Deletion',
        message:
          'Are you sure you want to permanently delete your account and all associated data? This action cannot be undone.',
        confirmButtonText: 'Delete My Account',
      },
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
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
