import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-onboarding-invitation-code',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './onboarding-invitation-code.component.html',
  styleUrls: ['./onboarding-invitation-code.component.scss'],
})
export class OnboardingInvitationCodeComponent implements OnInit {
  @Input() currentInvitationCode: string | null = null;
  @Input() isLoading: boolean = false;
  @Input() errorMessage: string | null = null;

  @Output() codeSubmitted = new EventEmitter<string>();
  @Output() noCodeSelected = new EventEmitter<void>();

  invitationCodeFormControl = new FormControl<string | null>(null);

  constructor() {}

  ngOnInit(): void {
    // Initialize form control with existing data if available (e.g., from session storage)
    if (this.currentInvitationCode) {
      this.invitationCodeFormControl.setValue(this.currentInvitationCode);
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
