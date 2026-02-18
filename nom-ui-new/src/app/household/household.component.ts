import { Component, inject, input, output, signal, computed, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { HouseholdService } from '../core/services/household.service';
import { LoadingService } from '../core/services/loading.service';
import { HouseholdResponseModel, HouseholdCreateResponseModel } from '../core/models/household.model';

export interface HouseholdFormData {
  household: HouseholdCreateResponseModel | null;
  joinToken: string | null;
}

@Component({
  selector: 'nom-household',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './household.component.html',
  styleUrl: './household.component.scss',
})
export class Household implements OnInit {
  mode = input<'standalone' | 'wizard'>('standalone');

  stepComplete = output<HouseholdFormData>();
  skipped = output<void>();
  saved = output<HouseholdFormData>();

  private fb = inject(FormBuilder);
  private householdService = inject(HouseholdService);
  private loadingService = inject(LoadingService);

  households = signal<HouseholdResponseModel[]>([]);
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');
  activeTab = signal<'create' | 'join'>('create');

  isStandalone = computed(() => this.mode() !== 'wizard');

  createForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    description: ['', Validators.maxLength(2047)],
  });

  joinForm = this.fb.group({
    token: ['', Validators.required],
  });

  ngOnInit(): void {
    if (this.isStandalone()) {
      this.loadHouseholds();
    }
  }

  onCreateSubmit(): void {
    if (this.createForm.invalid) return;
    const form = this.createForm.getRawValue();

    this.loading.set(true);
    this.errorMessage.set('');

    this.householdService.createHousehold({
      name: form.name!,
      description: form.description || null,
      householdGroupId: 1,
    }).pipe(
      this.loadingService.loading('Creating household...')
    ).subscribe({
      next: (household) => {
        this.loading.set(false);
        this.successMessage.set(`Household "${household.name}" created.`);
        const data: HouseholdFormData = { household, joinToken: null };
        if (this.isStandalone()) {
          this.loadHouseholds();
          this.createForm.reset();
          this.saved.emit(data);
        } else {
          this.stepComplete.emit(data);
        }
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to create household. Please try again.');
      },
    });
  }

  onJoinSubmit(): void {
    if (this.joinForm.invalid) return;
    const token = this.joinForm.getRawValue().token!;

    this.loading.set(true);
    this.errorMessage.set('');

    this.householdService.joinHousehold(token).pipe(
      this.loadingService.loading('Joining household...')
    ).subscribe({
      next: () => {
        this.loading.set(false);
        this.successMessage.set('Successfully joined household.');
        const data: HouseholdFormData = { household: null, joinToken: token };
        if (this.isStandalone()) {
          this.loadHouseholds();
          this.joinForm.reset();
          this.saved.emit(data);
        } else {
          this.stepComplete.emit(data);
        }
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Invalid or expired invite code. Please check and try again.');
      },
    });
  }

  onSkip(): void {
    this.skipped.emit();
  }

  private loadHouseholds(): void {
    this.householdService.getHouseholds().pipe(
      this.loadingService.loading('Loading households...')
    ).subscribe({
      next: (list) => this.households.set(list),
      error: () => this.errorMessage.set('Unable to load households.'),
    });
  }
}
