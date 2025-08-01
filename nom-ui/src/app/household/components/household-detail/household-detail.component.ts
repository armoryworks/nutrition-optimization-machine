import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
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
import { HouseholdResponseModel } from '../../models/household.model';

@Component({
    selector: 'app-household-detail',
    standalone: true,
    imports: [
        CommonModule,
        MatCardModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
        MatChipsModule,
        MatDividerModule,
        MatDialogModule,
        MatListModule,
        MatMenuModule,
    ],
    templateUrl: './household-detail.component.html',
    styleUrls: ['./household-detail.component.scss']
})
export class HouseholdDetailComponent implements OnInit {
    household: HouseholdResponseModel | null = null;
    isLoading = true;
    error: string | null = null;
    householdId: number = 0;

    constructor(
        private route: ActivatedRoute,
        private router: Router,
        private householdService: HouseholdService,
        private snackBar: MatSnackBar,
        private dialog: MatDialog
    ) { }

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

    onEditHousehold(): void {
        this.router.navigate(['/household', this.householdId, 'edit']);
    }

    onInviteMembers(): void {
        this.router.navigate(['/household', this.householdId, 'invite']);
    }

    onViewMealPlans(): void {
        this.router.navigate(['/meal-plan'], { queryParams: { householdId: this.householdId } });
    }

    onViewShoppingLists(): void {
        this.router.navigate(['/shopping'], { queryParams: { householdId: this.householdId } });
    }

    onViewRecipes(): void {
        this.router.navigate(['/recipe'], { queryParams: { householdId: this.householdId } });
    }

    onBackToList(): void {
        this.router.navigate(['/household']);
    }

    onRefresh(): void {
        this.loadHousehold();
    }

    onDeleteHousehold(): void {
        // TODO: Implement delete confirmation dialog
        this.snackBar.open('Delete functionality not yet implemented', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
        });
    }

    onRemoveMember(memberId: number): void {
        // TODO: Implement member removal
        this.snackBar.open('Member removal not yet implemented', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
        });
    }

    onCopyInviteLink(): void {
        // TODO: Implement invite link copying
        this.snackBar.open('Invite link copying not yet implemented', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
        });
    }
} 