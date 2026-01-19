import { Component, OnInit, inject, signal } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { MatChipsModule } from '@angular/material/chips';

import { AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwProgressSpinnerComponent, AmwMenuComponent, AmwMenuItemComponent, AmwMenuTriggerForDirective, DialogService } from 'angular-material-wrap';

import { HouseholdService } from '../../services/household.service';
import { HouseholdResponseModel } from '../../models/household-response.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'nom-household-detail',
    standalone: true,
    imports: [
        MatChipsModule,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwProgressSpinnerComponent,
        AmwMenuComponent,
        AmwMenuItemComponent,
        AmwMenuTriggerForDirective
    ],
    templateUrl: './household-detail.component.html',
    styleUrls: ['./household-detail.component.scss']
})
export class HouseholdDetailComponent implements OnInit {
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private householdService = inject(HouseholdService);
    private notificationService = inject(NotificationService);
    private dialogService = inject(DialogService);

    household = signal<HouseholdResponseModel | null>(null);
    isLoading = signal(true);
    error = signal<string | null>(null);
    householdId = signal(0);

    pageTitle = 'Household Details';
    pageSubtitle = 'View and manage household information';



    ngOnInit(): void {
        this.route.params.subscribe(params => {
            this.householdId.set(+params['id']);
            this.loadHousehold();
        });
    }

    loadHousehold(): void {
        this.isLoading.set(true);
        this.error.set(null);

        this.householdService.getHousehold(this.householdId()).subscribe({
            next: (household) => {
                this.household.set(household);
                this.isLoading.set(false);
            },
            error: (error) => {
                console.error('Error loading household:', error);
                this.error.set('Failed to load household details');
                this.isLoading.set(false);
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
        this.router.navigate(['/household', this.householdId(), 'edit']);
    }

    onInviteMembers(): void {
        this.router.navigate(['/household', this.householdId(), 'invite']);
    }

    onViewMealPlans(): void {
        this.router.navigate(['/plan'], { queryParams: { householdId: this.householdId() } });
    }

    onViewShoppingLists(): void {
        this.router.navigate(['/shopping'], { queryParams: { householdId: this.householdId() } });
    }

    onViewRecipes(): void {
        this.router.navigate(['/recipe'], { queryParams: { householdId: this.householdId() } });
    }

    onDeleteHousehold(): void {
        if (!this.household()) return;

        this.dialogService.confirm(
            `Are you sure you want to delete "${this.household()!.name}"? This action cannot be undone and will remove all associated data.`,
            'Delete Household'
        ).subscribe(confirmed => {
            if (confirmed) {
                this.householdService.deleteHousehold(this.householdId()).subscribe({
                    next: () => {
                        this.notificationService.success('Household deleted successfully');
                        this.router.navigate(['/household']);
                    },
                    error: (error) => {
                        console.error('Error deleting household:', error);
                        this.notificationService.error('Failed to delete household');
                    }
                });
            }
        });
    }

    onRemoveMember(memberId: number): void {
        this.dialogService.confirm(
            'Are you sure you want to remove this member from the household?',
            'Remove Member'
        ).subscribe(confirmed => {
            if (confirmed) {
                this.householdService.removeMember(this.householdId(), memberId).subscribe({
                    next: () => {
                        this.notificationService.success('Member removed successfully');
                        this.loadHousehold();
                    },
                    error: (error) => {
                        console.error('Error removing member:', error);
                        this.notificationService.error('Failed to remove member');
                    }
                });
            }
        });
    }

    onCopyInviteLink(): void {
        if (this.household()?.inviteToken) {
            const inviteLink = `${window.location.origin}/household/join/${this.household()!.inviteToken}`;
            navigator.clipboard.writeText(inviteLink).then(() => {
                this.notificationService.success('Invite link copied to clipboard');
            }).catch(() => {
                this.notificationService.error('Failed to copy invite link');
            });
        }
    }
} 