import { Component, OnInit, OnDestroy, ViewEncapsulation, inject } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from '@angular/forms';

import { AmwInputComponent, AmwButtonComponent, AmwToggleComponent, AmwCheckboxComponent, AmwCardComponent, AmwIconComponent, AmwListComponent, AmwListItemComponent, AmwDividerComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { UpdateTwoFactorResponse } from '../models/update-two-factor-response';
import { UpdateTwoFactor } from '../models/update-two-factor'; // Import the UpdateTwoFactor model
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-update-two-factor',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwButtonComponent,
    AmwToggleComponent,
    AmwCheckboxComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwListComponent,
    AmwListItemComponent,
    AmwDividerComponent,
    AmwValidationTooltipDirective,
  ],
  templateUrl: './update-two-factor.component.html',
  styleUrls: ['./update-two-factor.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class UpdateTwoFactorComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  twoFactorForm!: FormGroup;
  validationContext!: ValidationContext;

  // We cannot fetch initial status from backend without getTwoFactorStatus
  // So, initialize with a default "disabled" status
  current2faStatus: UpdateTwoFactorResponse = {
    sharedKey: '',
    recoveryCodesLeft: 0,
    recoverCodes: [],
    isTwoFactorEnabled: false, // Default to disabled
    isMachineRemembered: false,
  };

  sharedKeyToDisplay: string | null = null; // For display and QR code
  recoveryCodesToDisplay: string[] = [];

  // For displaying new recovery codes

  ngOnInit(): void {
    this.twoFactorForm = this.nonNullableFb.group(
      {
        enable2fa: [this.current2faStatus.isTwoFactorEnabled], // Initialize with default disabled status
        password: ['', Validators.required],
        twoFactorCode: [''], // Required when disabling
        rememberMachine: [this.current2faStatus.isMachineRemembered], // This checkbox maps to forgetMachine
      },
      { validators: this.twoFactorConditionalValidator }
    );

    // Watch for changes in enable2fa toggle to adjust validation
    this.twoFactorForm.get('enable2fa')?.valueChanges.subscribe(() => {
      this.twoFactorForm.get('password')?.updateValueAndValidity();
      this.twoFactorForm.get('twoFactorCode')?.updateValueAndValidity();
    });

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Password validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'password-required',
      message: 'Current password is required',
      severity: 'error',
      field: 'password',
      control: this.twoFactorForm.get('password') ?? undefined,
      validator: () => !this.twoFactorForm.get('password')?.hasError('required')
    });

    // Two-factor code validation (conditionally required when disabling)
    this.validationService.addViolation(this.validationContext.id, {
      id: 'twoFactorCode-required',
      message: 'Authenticator code is required to disable 2FA',
      severity: 'error',
      field: 'twoFactorCode',
      control: this.twoFactorForm.get('twoFactorCode') ?? undefined,
      validator: () => !this.twoFactorForm.get('twoFactorCode')?.hasError('required')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  // Form-level validator for 2FA logic
  twoFactorConditionalValidator(control: AbstractControl) {
    const enable2fa = control.get('enable2fa')?.value;
    const passwordControl = control.get('password');
    const twoFactorCodeControl = control.get('twoFactorCode');

    // Ensure password is required for any update operation
    if (!passwordControl?.value) {
      passwordControl?.setErrors({ required: true });
    } else {
      passwordControl?.setErrors(null);
    }

    // Two-factor code required when disabling 2FA
    // The code field is only shown if enable2fa is false in HTML,
    // so this validator will align with that.
    if (passwordControl?.valid && !enable2fa) {
      if (!twoFactorCodeControl?.value) {
        twoFactorCodeControl?.setErrors({ required: true });
      } else {
        twoFactorCodeControl?.setErrors(null);
      }
    } else {
      // If enabling or password not valid, clear code errors
      twoFactorCodeControl?.setErrors(null);
    }

    // If password field is valid and was marked required, clear its error
    if (passwordControl?.valid && passwordControl?.hasError('required')) {
      passwordControl.setErrors(null);
    }

    return null; // No form-level errors for now, specific control errors are set
  }

  /**
   * Generates the otpauth URI for QR code display.
   * @param sharedKey The base32 encoded shared key from the backend.
   * @param email The user's email address.
   * @param issuer The issuer name (your app's name).
   * @returns The otpauth URI string.
   */
  generateOtpAuthUri(
    sharedKey: string,
    email: string,
    issuer = 'NutritionOptimizationMachine'
  ): string {
    const encodedIssuer = encodeURIComponent(issuer);
    const encodedEmail = encodeURIComponent(email);
    return `otpauth://totp/${encodedIssuer}:${encodedEmail}?secret=${sharedKey}&issuer=${encodedIssuer}`;
  }

  /**
   * Handles the form submission for 2FA changes.
   */
  onSubmit(): void {
    this.twoFactorForm.markAllAsTouched();
    this.twoFactorForm.updateValueAndValidity();

    if (this.twoFactorForm.invalid) {
      this.notificationService.warning(
        'Please correct the highlighted errors in the form.'
      );
      return;
    }

    const formData = this.twoFactorForm.getRawValue();
    const updateData: UpdateTwoFactor = {
      enable: formData.enable2fa,
      twoFactorCode: formData.twoFactorCode,
      resetSharedKey: false,
      resetRecoverCodes: false,
      forgetMachine: !formData.rememberMachine,
    };

    this.authService.updateTwoFactorAuth(updateData)
      .pipe(loading('Updating 2FA settings...'))
      .subscribe({
        next: (response: UpdateTwoFactorResponse) => {
          this.current2faStatus = response;
          this.twoFactorForm.get('password')?.reset();
          this.twoFactorForm.get('twoFactorCode')?.reset();

          if (response.isTwoFactorEnabled) {
            this.sharedKeyToDisplay = response.sharedKey;
            this.recoveryCodesToDisplay = response.recoverCodes;
            this.notificationService.success(
              'Two-Factor Authentication enabled successfully! Please save your recovery codes.'
            );
          } else {
            this.sharedKeyToDisplay = null;
            this.recoveryCodesToDisplay = [];
            this.notificationService.success(
              'Two-Factor Authentication disabled successfully!'
            );
          }

          this.twoFactorForm.patchValue(
            {
              enable2fa: response.isTwoFactorEnabled,
              rememberMachine: response.isMachineRemembered,
            },
            { emitEvent: false }
          );
          this.twoFactorForm.setErrors(null);
        },
        error: (error) => {
          console.error('2FA update error:', error);
          this.notificationService.error(
            error.message || 'Failed to update 2FA settings.'
          );
        },
      });
  }

  // Helper to copy recovery codes to clipboard
  copyRecoveryCodes(): void {
    const codesText = this.recoveryCodesToDisplay.join('\n');
    navigator.clipboard
      .writeText(codesText)
      .then(() => {
        this.notificationService.info('Recovery codes copied to clipboard!');
      })
      .catch((err) => {
        console.error('Could not copy text: ', err);
        this.notificationService.error(
          'Failed to copy codes. Please copy manually.'
        );
      });
  }

  // Helper to copy shared key to clipboard
  copySharedKey(): void {
    if (this.sharedKeyToDisplay) {
      navigator.clipboard
        .writeText(this.sharedKeyToDisplay)
        .then(() => {
          this.notificationService.info('Shared key copied to clipboard!');
        })
        .catch((err) => {
          console.error('Could not copy text: ', err);
          this.notificationService.error(
            'Failed to copy shared key. Please copy manually.'
          );
        });
    }
  }
}
