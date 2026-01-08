import { Component, OnInit, inject, signal } from '@angular/core';

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
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule
],
    templateUrl: './household-invite.component.html',
    styleUrls: ['./household-invite.component.scss']
})
export class HouseholdInviteComponent implements OnInit {
    private nonNullableFb = inject(NonNullableFormBuilder);
    private householdService = inject(HouseholdService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    private snackBar = inject(MatSnackBar);
    private userInfoService = inject(UserInfoService);

    inviteForm: FormGroup;
    isLoading = signal(false);
    householdId = signal(0);
    error = signal<string | null>(null);
    inviteToken = signal<string | null>(null);
    inviteLink = signal<string | null>(null);

    constructor() {
        this.inviteForm = this.nonNullableFb.group({
            expiresInDays: [7, [Validators.required, Validators.min(1), Validators.max(30)]]
        });
    }

    ngOnInit(): void {
        this.route.params.subscribe(params => {
            this.householdId.set(+params['id']);
        });
    }

    generateInviteToken(): void {
        if (this.inviteForm.valid) {
            this.isLoading.set(true);
            this.error.set(null);

            const request: HouseholdInviteRequestModel = {
                householdId: this.householdId(),
                expiresAt: new Date(Date.now() + this.inviteForm.value.expiresInDays * 24 * 60 * 60 * 1000)
            };

            this.householdService.createInviteToken(request).subscribe({
                next: (response) => {
                    this.inviteToken.set(response.token);
                    this.inviteLink.set(`${window.location.origin}/household/join?token=${response.token}`);
                    this.isLoading.set(false);
                    this.snackBar.open('Invite token generated successfully', 'Close', {
                        duration: 3000,
                        horizontalPosition: 'center',
                        verticalPosition: 'top'
                    });
                },
                error: (error) => {
                    console.error('Error generating invite token:', error);
                    this.error.set('Failed to generate invite token');
                    this.isLoading.set(false);
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
        if (this.inviteLink()) {
            navigator.clipboard.writeText(this.inviteLink()!).then(() => {
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
        if (this.inviteToken()) {
            navigator.clipboard.writeText(this.inviteToken()!).then(() => {
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
        this.router.navigate(['/household', this.householdId()]);
    }

    onSubmit(): void {
        this.generateInviteToken();
    }

    copyLink(): void {
        this.copyInviteLink();
    }

    shareLink(): void {
        if (this.inviteLink() && navigator.share) {
            navigator.share({
                title: 'Household Invitation',
                text: 'Join our household',
                url: this.inviteLink()!
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