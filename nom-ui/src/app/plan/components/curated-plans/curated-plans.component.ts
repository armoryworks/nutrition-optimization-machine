import { Component, OnInit, inject, signal, effect, Injector } from '@angular/core';

import { AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwChipComponent, AmwProgressSpinnerComponent, DialogService } from 'angular-material-wrap';

import { PlanService } from '../../services/plan.service';
import { PlanModel } from '../../models/plan.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { PlanNameComponent } from '../plan-name/plan-name.component';

@Component({
    selector: 'nom-curated-plans',
    standalone: true,
    imports: [
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwChipComponent,
        AmwProgressSpinnerComponent,
    ],
    templateUrl: './curated-plans.component.html',
    styleUrls: ['./curated-plans.component.scss']
})
export class CuratedPlansComponent implements OnInit {
    private planService = inject(PlanService);
    private notificationService = inject(NotificationService);
    private dialogService = inject(DialogService);
    private injector = inject(Injector);

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

        const dialogRef = this.dialogService.open('Clone Plan', PlanNameComponent, {
            width: '400px'
        });

        // Set data via instance signals
        dialogRef.instance.title.set('Clone Plan');
        dialogRef.instance.message.set('Enter a name for your cloned plan:');
        dialogRef.instance.defaultValue.set(`${plan.name} (Copy)`);

        // Signal-based communication with the dialog component
        effect(() => {
            const confirmedName = dialogRef.instance.confirmed();
            if (confirmedName) {
                dialogRef.close(confirmedName);
            }
        }, { injector: this.injector });

        effect(() => {
            if (dialogRef.instance.cancelled()) {
                dialogRef.close();
            }
        }, { injector: this.injector });

        dialogRef.afterClosed().subscribe(result => {
            if (result) {
                this.cloningPlanId.set(planId);
                this.planService.clonePlan(planId, result).subscribe({
                    next: () => {
                        this.notificationService.success('Plan cloned successfully!');
                        this.cloningPlanId.set(null);
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

    getStatusColor(status: string): 'primary' | 'accent' | 'warn' {
        switch (status) {
            case 'Curated': return 'primary';
            case 'PendingCuration': return 'warn';
            case 'RequiresRevision': return 'accent';
            default: return 'primary';
        }
    }
}
