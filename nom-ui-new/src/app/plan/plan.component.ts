import { Component, inject, input, output, signal, computed, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PlanService } from '../core/services/plan.service';
import { LoadingService } from '../core/services/loading.service';
import { PlanModel } from '../core/models/plan.model';
import { RestrictionRequest } from '../core/models/person.model';

export interface PlanFormData {
  planName: string | null;
  planDescription: string | null;
  startDate: string | null;
  endDate: string | null;
  applyRestrictions: boolean;
  invitationCode: string | null;
}

@Component({
  selector: 'nom-plan',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './plan.component.html',
  styleUrl: './plan.component.scss',
})
export class Plan implements OnInit {
  mode = input<'standalone' | 'wizard'>('standalone');
  initialRestrictions = input<RestrictionRequest[]>([]);

  stepComplete = output<PlanFormData>();
  skipped = output<void>();
  saved = output<PlanFormData>();

  private fb = inject(FormBuilder);
  private planService = inject(PlanService);
  private loadingService = inject(LoadingService);

  plans = signal<PlanModel[]>([]);
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');
  activeTab = signal<'create' | 'join'>('create');

  isStandalone = computed(() => this.mode() !== 'wizard');
  hasRestrictions = computed(() => this.initialRestrictions().length > 0);

  createForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    description: [''],
    startDate: [new Date()],
    endDate: [null as Date | null],
    applyRestrictions: [true],
  });

  joinForm = this.fb.group({
    invitationCode: ['', Validators.required],
  });

  ngOnInit(): void {
    if (this.isStandalone()) {
      this.loadPlans();
    }
  }

  onCreateSubmit(): void {
    if (this.createForm.invalid) return;
    const form = this.createForm.getRawValue();

    const data: PlanFormData = {
      planName: form.name,
      planDescription: form.description || null,
      startDate: form.startDate ? this.toDateString(form.startDate) : null,
      endDate: form.endDate ? this.toDateString(form.endDate) : null,
      applyRestrictions: form.applyRestrictions ?? false,
      invitationCode: null,
    };

    if (this.isStandalone()) {
      this.createPlan(data);
    } else {
      this.stepComplete.emit(data);
    }
  }

  onJoinSubmit(): void {
    if (this.joinForm.invalid) return;
    const code = this.joinForm.getRawValue().invitationCode!;

    const data: PlanFormData = {
      planName: null,
      planDescription: null,
      startDate: null,
      endDate: null,
      applyRestrictions: false,
      invitationCode: code,
    };

    if (this.isStandalone()) {
      this.successMessage.set('Plan invitation code saved.');
      this.saved.emit(data);
    } else {
      this.stepComplete.emit(data);
    }
  }

  onSkip(): void {
    this.skipped.emit();
  }

  private createPlan(data: PlanFormData): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.planService.createPlan({
      name: data.planName!,
      description: data.planDescription,
      startDate: data.startDate!,
      endDate: data.endDate,
      goals: [],
      meals: [],
      restrictions: data.applyRestrictions
        ? this.initialRestrictions().map(r => ({
            id: 0,
            name: r.name,
            description: r.description,
            restrictionType: null,
            ingredientName: null,
            nutrientName: null,
          }))
        : [],
    }).pipe(
      this.loadingService.loading('Creating plan...')
    ).subscribe({
      next: () => {
        this.loading.set(false);
        this.successMessage.set('Plan created successfully.');
        this.loadPlans();
        this.createForm.reset({ startDate: new Date(), applyRestrictions: true });
        this.saved.emit(data);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to create plan. Please try again.');
      },
    });
  }

  private loadPlans(): void {
    this.planService.getMyPlans().pipe(
      this.loadingService.loading('Loading your plans...')
    ).subscribe({
      next: (list) => this.plans.set(list),
      error: () => {}, // silently fail — user may not have a person yet
    });
  }

  private toDateString(date: Date): string {
    return date.toISOString().split('T')[0];
  }
}
