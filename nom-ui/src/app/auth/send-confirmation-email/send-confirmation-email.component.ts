import { Component, ViewEncapsulation, inject, OnInit, OnDestroy } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';

import { RouterLink } from '@angular/router';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { SendConfirmationEmail } from '../models/send-confirmation-email';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-send-confirmation-email',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwValidationTooltipDirective,
  ],
  templateUrl: './send-confirmation-email.component.html',
  styleUrls: ['./send-confirmation-email.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class SendConfirmationEmailComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  sendConfirmationEmailForm: FormGroup;
  validationContext!: ValidationContext;

  constructor() {
    this.sendConfirmationEmailForm = this.nonNullableFb.group({
      email: ['', [Validators.required, Validators.email]],
    });
  }

  ngOnInit(): void {
    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Email validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-required',
      message: 'Email is required',
      severity: 'error',
      field: 'email',
      control: this.sendConfirmationEmailForm.get('email') ?? undefined,
      validator: () => !this.sendConfirmationEmailForm.get('email')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-format',
      message: 'Please enter a valid email address',
      severity: 'error',
      field: 'email',
      control: this.sendConfirmationEmailForm.get('email') ?? undefined,
      validator: () => !this.sendConfirmationEmailForm.get('email')?.hasError('email')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }



  /**
   * Handles the form submission to send a confirmation email.
   */
  onSubmit(): void {
    this.sendConfirmationEmailForm.markAllAsTouched();

    if (this.sendConfirmationEmailForm.invalid) {
      this.notificationService.warning('Please enter a valid email address.');
      return;
    }

    const data: SendConfirmationEmail =
      this.sendConfirmationEmailForm.getRawValue();

    this.authService.sendConfirmationEmail(data)
      .pipe(loading('Sending confirmation email...'))
      .subscribe({
        next: () => {
          this.notificationService.success(
            'Confirmation email sent. Please check your inbox!'
          );
          this.sendConfirmationEmailForm.reset();
        },
        error: (error) => {
          console.error('Send confirmation email error:', error);
          this.notificationService.error(
            error.message || 'An unexpected error occurred. Please try again.'
          );
        },
      });
  }
}
