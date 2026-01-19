import { Component, OnInit, inject, signal } from '@angular/core';

import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { FormsModule } from '@angular/forms';

import { AmwButtonComponent, AmwCardComponent, AmwInputComponent, AmwIconComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

import { PlanService } from '../../services/plan.service';
import { PlanModel } from '../../models/plan.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'nom-curated-plans',
    standalone: true,
    imports: [
        MatChipsModule,
        MatDialogModule,
        FormsModule,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwProgressSpinnerComponent,
    ],
    templateUrl: './curated-plans.component.html',
    styleUrls: ['./curated-plans.component.scss']
})
export class CuratedPlansComponent implements OnInit {
    private planService = inject(PlanService);
    private notificationService = inject(NotificationService);
    private dialog = inject(MatDialog);

    curatedPlans = signal<PlanModel[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    cloningPlanId = signal<number | null>(null);

    ngOnInit(): void {
        this.loadCuratedPlans();
    }

    loadCuratedPlans(): void {
        this.isLoading.set(true);
        this.error.set(null);

        this.planService.getCuratedPlans().subscribe({
            next: (plans) => {
                this.curatedPlans.set(plans);
                this.isLoading.set(false);
            },
            error: (error) => {
                console.error('Error loading curated plans:', error);
                this.error.set('Failed to load curated plans');
                this.isLoading.set(false);
            }
        });
    }

    clonePlan(planId: number): void {
        const plan = this.curatedPlans().find(p => p.id === planId);
        if (!plan) return;

        const dialogRef = this.dialog.open(PlanNameDialogComponent, {
            width: '400px',
            data: {
                title: 'Clone Plan',
                message: 'Enter a name for your cloned plan:',
                defaultValue: `${plan.name} (Copy)`
            }
        });

        dialogRef.afterClosed().subscribe(result => {
            if (result) {
                this.cloningPlanId.set(planId);
                this.planService.clonePlan(planId, result).subscribe({
                    next: () => {
                        this.notificationService.success('Plan cloned successfully!');
                        this.cloningPlanId.set(null);
                        // Optionally navigate to the cloned plan
                    },
                    error: (error) => {
                        console.error('Error cloning plan:', error);
                        this.notificationService.error('Failed to clone plan');
                        this.cloningPlanId.set(null);
                    }
                });
            }
        });
    }

    getPlanDuration(plan: PlanModel): string {
        if (!plan.startDate || !plan.endDate) return 'Duration not specified';

        const startDate = new Date(plan.startDate);
        const endDate = new Date(plan.endDate);

        const diffTime = Math.abs(endDate.getTime() - startDate.getTime());
        const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

        if (diffDays === 1) return '1 day';
        if (diffDays < 7) return `${diffDays} days`;
        if (diffDays < 30) return `${Math.ceil(diffDays / 7)} weeks`;
        return `${Math.ceil(diffDays / 30)} months`;
    }

    getPlanStats(plan: PlanModel): { goals: number; meals: number; recipes: number } {
        const totalRecipes = plan.meals.reduce((sum, meal) => sum + meal.recipes.length, 0);
        return {
            goals: plan.goals.length,
            meals: plan.meals.length,
            recipes: totalRecipes
        };
    }

    getStatusColor(status: string): string {
        switch (status) {
            case 'Curated': return 'primary';
            case 'PendingCuration': return 'warn';
            case 'RequiresRevision': return 'accent';
            default: return 'default';
        }
    }
}

// Simple dialog component for plan name input
@Component({
    selector: 'nom-plan-name-dialog',
    standalone: true,
    imports: [MatDialogModule, FormsModule, AmwInputComponent, AmwButtonComponent],
    template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
      <amw-input
        [(ngModel)]="planName"
        label="Plan Name"
        placeholder="Enter plan name"
        style="width: 100%;">
      </amw-input>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <amw-button variant="text" mat-dialog-close>Cancel</amw-button>
      <amw-button variant="filled" color="primary" [mat-dialog-close]="planName" [disabled]="!planName.trim()">
        Clone
      </amw-button>
    </mat-dialog-actions>
  `
})
export class PlanNameDialogComponent {
    dialogRef = inject(MatDialog);
    data = inject(MAT_DIALOG_DATA);

    planName = '';

    constructor() {
        const data = this.data;

        this.planName = data.defaultValue || '';
    }
} 