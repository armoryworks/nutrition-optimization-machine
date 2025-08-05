import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';

import { HouseholdService } from '../../services/household.service';
import { HouseholdInviteTokenCreateRequestModel } from '../../models/household-invite-token-create-request.model';
import { HouseholdInviteTokenResponseModel } from '../../models/household-invite-token-response.model';

@Component({
  selector: 'nom-household-invite',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
  ],
  templateUrl: './household-invite.component.html',
  styleUrls: ['./household-invite.component.scss']
})
export class HouseholdInviteComponent implements OnInit {
  onSubmit(): void {
    if (this.inviteForm.valid) {
      this.generateInviteToken();
    } else {
      this.snackBar.open('Please fill in all required fields', 'Close', {
        duration: 3000,
        horizontalPosition: 'center',
        verticalPosition: 'top'
      });
    }
  }

  copyLink(): void {
    if (this.inviteLink) {
      navigator.clipboard.writeText(this.inviteLink).then(() => {
        this.snackBar.open('Invite link copied to clipboard', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      }).catch(() => {
        this.snackBar.open('Failed to copy invite link', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      });
    } else {
      this.snackBar.open('No invite link available. Please generate one first.', 'Close', {
        duration: 3000,
        horizontalPosition: 'center',
        verticalPosition: 'top'
      });
    }
  }

  shareLink(): void {
    if (this.inviteLink && navigator.share) {
      navigator.share({
        title: 'Join my household',
        text: 'You\'ve been invited to join my household on NOM',
        url: this.inviteLink
      }).then(() => {
        this.snackBar.open('Invite shared successfully', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      }).catch(() => {
        this.snackBar.open('Failed to share invite link', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      });
    } else if (this.inviteLink) {
      // Fallback to copy if Web Share API is not available
      this.copyLink();
    } else {
      this.snackBar.open('No invite link available. Please generate one first.', 'Close', {
        duration: 3000,
        horizontalPosition: 'center',
        verticalPosition: 'top'
      });
    }
  }
  inviteForm: FormGroup;
  householdId: number = 0;
  isLoading = false;
  inviteToken: string | null = null;
  inviteLink: string | null = null;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private householdService: HouseholdService,
    private fb: FormBuilder,
    private snackBar: MatSnackBar
  ) {
    this.inviteForm = this.fb.group({
      expiresInDays: [7, [Validators.required, Validators.min(1), Validators.max(30)]]
    });
  }

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.householdId = +params['id'];
    });
  }

  generateInviteToken(): void {
    if (this.inviteForm.valid) {
      this.isLoading = true;
      this.error = null;

      const request: HouseholdInviteTokenCreateRequestModel = {
        householdId: this.householdId,
        expiresAt: new Date(Date.now() + this.inviteForm.value.expiresInDays * 24 * 60 * 60 * 1000)
      };

      this.householdService.createInviteToken(request).subscribe({
        next: (response: HouseholdInviteTokenResponseModel) => {
          this.inviteToken = response.token;
          this.inviteLink = `${window.location.origin}/household/join?token=${response.token}`;
          this.isLoading = false;
          this.snackBar.open('Invite token generated successfully', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
        },
        error: (error) => {
          console.error('Error generating invite token:', error);
          this.error = 'Failed to generate invite token';
          this.isLoading = false;
          this.snackBar.open('Failed to generate invite token', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
        }
      });
    }
  }

  copyInviteLink(): void {
    if (this.inviteLink) {
      navigator.clipboard.writeText(this.inviteLink).then(() => {
        this.snackBar.open('Invite link copied to clipboard', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      }).catch(() => {
        this.snackBar.open('Failed to copy invite link', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      });
    }
  }

  copyToken(): void {
    if (this.inviteToken) {
      navigator.clipboard.writeText(this.inviteToken).then(() => {
        this.snackBar.open('Invite token copied to clipboard', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      }).catch(() => {
        this.snackBar.open('Failed to copy invite token', 'Close', {
          duration: 3000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      });
    }
  }

  onBack(): void {
    this.router.navigate(['/household', this.householdId]);
  }
} 