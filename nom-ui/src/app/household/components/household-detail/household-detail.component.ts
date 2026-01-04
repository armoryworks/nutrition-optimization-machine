import { Component, OnInit, inject } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';

import { HouseholdService } from '../../services/household.service';
import { HouseholdResponseModel } from '../../models/household-response.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';
import { BaseDetailComponent, BaseDetailConfig } from '../../../common/components/base-detail/base-detail.component';

@Component({
    selector: 'nom-household-detail',
    standalone: true,
    imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatDialogModule,
    MatListModule,
    MatMenuModule,
    BaseDetailComponent
],
    templateUrl: './household-detail.component.html',
    styleUrls: ['./household-detail.component.scss']
})
export class HouseholdDetailComponent implements OnInit {
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private householdService = inject(HouseholdService);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);

    household: HouseholdResponseModel | null = null;
    isLoading = true;
    error: string | null = null;
    householdId = 0;

    detailConfig: BaseDetailConfig = {
        title: 'Household Details',
        subtitle: 'View and manage household information',
        showBackButton: true,
        maxWidth: '800px',
    };



    ngOnInit(): void {
        this.route.params.subscribe(params => {
            this.householdId = +params['id'];
            this.loadHousehold();
        });
    }

    loadHousehold(): void {
        this.isLoading = true;
        this.error = null;

        this.householdService.getHousehold(this.householdId).subscribe({
            next: (household) => {
                this.household = household;
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading household:', error);
                this.error = 'Failed to load household details';
                this.isLoading = false;
            }
        });
    }

    onBack(): void {
        this.router.navigate(['/household']);
    }

    onRetry(): void {
        this.loadHousehold();
    }

    onEditHousehold(): void {
        this.router.navigate(['/household', this.householdId, 'edit']);
    }

    onInviteMembers(): void {
        this.router.navigate(['/household', this.householdId, 'invite']);
    }

    onViewMealPlans(): void {
        this.router.navigate(['/plan'], { queryParams: { householdId: this.householdId } });
    }

    onViewShoppingLists(): void {
        this.router.navigate(['/shopping'], { queryParams: { householdId: this.householdId } });
    }

    onViewRecipes(): void {
        this.router.navigate(['/recipe'], { queryParams: { householdId: this.householdId } });
    }

    onDeleteHousehold(): void {
        if (!this.household) return;

        const dialogRef = this.dialog.open(ConfirmDialogComponent, {
            width: '400px',
            data: {
                title: 'Delete Household',
                message: `Are you sure you want to delete "${this.household.name}"? This action cannot be undone and will remove all associated data.`,
                confirmText: 'Delete',
                cancelText: 'Cancel',
                confirmColor: 'warn'
            }
        });

        dialogRef.afterClosed().subscribe(result => {
            if (result) {
                this.householdService.deleteHousehold(this.householdId).subscribe({
                    next: () => {
                        this.snackBar.open('Household deleted successfully', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                        this.router.navigate(['/household']);
                    },
                    error: (error) => {
                        console.error('Error deleting household:', error);
                        this.snackBar.open('Failed to delete household', 'Close', {
                            duration: 5000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                    }
                });
            }
        });
    }

    onRemoveMember(memberId: number): void {
        const dialogRef = this.dialog.open(ConfirmDialogComponent, {
            width: '400px',
            data: {
                title: 'Remove Member',
                message: 'Are you sure you want to remove this member from the household?',
                confirmText: 'Remove',
                cancelText: 'Cancel',
                confirmColor: 'warn'
            }
        });

        dialogRef.afterClosed().subscribe(result => {
            if (result) {
                this.householdService.removeMember(this.householdId, memberId).subscribe({
                    next: () => {
                        this.snackBar.open('Member removed successfully', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                        this.loadHousehold();
                    },
                    error: (error) => {
                        console.error('Error removing member:', error);
                        this.snackBar.open('Failed to remove member', 'Close', {
                            duration: 5000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                    }
                });
            }
        });
    }

    onCopyInviteLink(): void {
        if (this.household?.inviteToken) {
            const inviteLink = `${window.location.origin}/household/join/${this.household.inviteToken}`;
            navigator.clipboard.writeText(inviteLink).then(() => {
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
} 