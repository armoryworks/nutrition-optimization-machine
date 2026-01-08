import { Component, OnInit, input, output } from '@angular/core';

import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'nom-onboarding-invitation-code',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule
],
  templateUrl: './onboarding-invitation-code.component.html',
  styleUrls: ['./onboarding-invitation-code.component.scss'],
})
export class OnboardingInvitationCodeComponent implements OnInit {
  currentInvitationCode = input<string | null>(null);
  isLoading = input(false);
  errorMessage = input<string | null>(null);

  codeSubmitted = output<string>();
  noCodeSelected = output<void>();

  invitationCodeFormControl = new FormControl<string | null>(null);

  ngOnInit(): void {
    // Initialize form control with existing data if available (e.g., from session storage)
    if (this.currentInvitationCode()) {
      this.invitationCodeFormControl.setValue(this.currentInvitationCode());
    }
  }

  onSubmit(): void {
    if (this.invitationCodeFormControl.value) {
      this.codeSubmitted.emit(this.invitationCodeFormControl.value);
    } else {
      // Potentially show a local error message or rely on parent component's error handling
      console.warn('Invitation code is empty.');
    }
  }

  onSkip(): void {
    this.noCodeSelected.emit();
  }
}
