import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { HouseholdService } from '../../services/household.service';
// Using inline interface instead of missing model
interface HouseholdInviteRequestModel {
    householdId: number;
    expiresAt: Date;
}
import { UserInfoService } from '../../../utilities/services/user-info.service';

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

    ],
    templateUrl: './household-invite.component.html',
    styleUrls: ['./household-invite.component.scss']
})
export class HouseholdInviteComponent implements OnInit {
    inviteForm: FormGroup;
    isLoading = false;
    householdId: number = 0;
    error: string | null = null;
    inviteToken: string | null = null;
    inviteLink: string | null = null;

    // Removed formConfig since BaseFormConfig is not imported

    constructor(
        private nonNullableFb: NonNullableFormBuilder,
        private householdService: HouseholdService,
        private router: Router,
        private route: ActivatedRoute,
        private snackBar: MatSnackBar,
        private userInfoService: UserInfoService
    ) {
        this.inviteForm = this.nonNullableFb.group({
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

            const request: HouseholdInviteRequestModel = {
                householdId: this.householdId,
                expiresAt: new Date(Date.now() + this.inviteForm.value.expiresInDays * 24 * 60 * 60 * 1000)
            };

            this.householdService.createInviteToken(request).subscribe({
                next: (response) => {
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

    onSubmit(): void {
        this.generateInviteToken();
    }

    copyLink(): void {
        this.copyInviteLink();
    }

    shareLink(): void {
        if (this.inviteLink && navigator.share) {
            navigator.share({
                title: 'Household Invitation',
                text: 'Join our household',
                url: this.inviteLink
            }).catch((error) => {
                console.log('Error sharing:', error);
                // Fallback to copy
                this.copyInviteLink();
            });
        } else {
            // Fallback to copy
            this.copyInviteLink();
        }
    }
} 