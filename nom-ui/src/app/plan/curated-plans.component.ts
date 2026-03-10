import { Component, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { PlanService } from '../core/services/plan.service';
import { LoadingService } from '../core/services/loading.service';
import { PlanModel } from '../core/models/plan.model';

@Component({
  selector: 'nom-curated-plans',
  imports: [RouterLink, MatIconModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './curated-plans.component.html',
  styleUrl: './curated-plans.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CuratedPlans implements OnInit {
  private planService = inject(PlanService);
  private loadingService = inject(LoadingService);
  private router = inject(Router);
  private dialog = inject(MatDialog);

  plans = signal<PlanModel[]>([]);
  loading = signal(true);
  error = signal('');

  ngOnInit(): void {
    this.planService.getCuratedPlans().pipe(
      this.loadingService.loading('Loading curated plans...'),
    ).subscribe({
      next: (plans) => {
        this.plans.set(plans);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load curated plans.');
        this.loading.set(false);
      },
    });
  }

  onClone(plan: PlanModel): void {
    const dialogRef = this.dialog.open(ClonePlanDialog, {
      width: '400px',
      data: { planName: plan.name },
    });

    dialogRef.afterClosed().subscribe((newName: string | undefined) => {
      if (!newName) return;
      this.planService.clonePlan({
        sourcePlanId: plan.id,
        newPlanName: newName,
      }).pipe(
        this.loadingService.loading('Cloning plan...'),
      ).subscribe({
        next: () => this.router.navigate(['/plan']),
        error: () => this.error.set('Failed to clone plan.'),
      });
    });
  }
}

@Component({
  selector: 'nom-clone-plan-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule],
  templateUrl: './clone-plan-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClonePlanDialog {
  data = inject<{ planName: string }>(MAT_DIALOG_DATA);
  newName = new FormControl(this.data.planName + ' (Copy)');
}
