import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormsModule } from '@angular/forms';
import { BehaviorSubject } from 'rxjs';
import { PlanService } from '../../services/plan.service';
import { PlanModel } from '../../models/plan.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'nom-curated-plans',
    standalone: true,
    imports: [
        CommonModule,
        MatCardModule,
        MatButtonModule,
        MatIconModule,
        MatChipsModule,
        MatProgressSpinnerModule,
        MatDialogModule,
        MatFormFieldModule,
        MatInputModule,
        FormsModule
    ],
    templateUrl: './curated-plans.component.html',
    styleUrls: ['./curated-plans.component.scss']
})
export class CuratedPlansComponent implements OnInit {
    private planService = inject(PlanService);
    private notificationService = inject(NotificationService);
    private dialog = inject(MatDialog);

    curatedPlans$ = new BehaviorSubject<PlanModel[]>([]);
    isLoading = false;
    error: string | null = null;
    cloningPlanId: number | null = null;

    ngOnInit(): void {
        this.loadCuratedPlans();
    }

    loadCuratedPlans(): void {
        this.isLoading = true;
        this.error = null;

        this.planService.getCuratedPlans().subscribe({
            next: (plans) => {
                this.curatedPlans$.next(plans);
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading curated plans:', error);
                this.error = 'Failed to load curated plans';
                this.isLoading = false;
            }
        });
    }

    clonePlan(planId: number): void {
        const plan = this.curatedPlans$.value.find(p => p.id === planId);
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
                this.cloningPlanId = planId;
                this.planService.clonePlan(planId, result).subscribe({
                    next: () => {
                        this.notificationService.success('Plan cloned successfully!');
                        this.cloningPlanId = null;
                        // Optionally navigate to the cloned plan
                    },
                    error: (error) => {
                        console.error('Error cloning plan:', error);
                        this.notificationService.error('Failed to clone plan');
                        this.cloningPlanId = null;
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
    imports: [CommonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, FormsModule],
    template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
      <mat-form-field appearance="outline" style="width: 100%;">
        <mat-label>Plan Name</mat-label>
        <input matInput [(ngModel)]="planName" placeholder="Enter plan name">
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-raised-button color="primary" [mat-dialog-close]="planName" [disabled]="!planName.trim()">
        Clone
      </button>
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