import { Component, inject, input, output, signal, computed, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { HouseholdService } from '../core/services/household.service';
import { AuthService } from '../core/services/auth.service';
import { LoadingService } from '../core/services/loading.service';
import { HouseholdResponseModel } from '../core/models/household-response.model';
import { HouseholdCreateResponseModel } from '../core/models/household-create-response.model';
import { HouseholdMemberResponseModel } from '../core/models/household-member-response.model';
import { AddMemberDialog, AddMemberDialogData } from './add-member-dialog/add-member-dialog.component';

export interface HouseholdFormData {
  household: HouseholdCreateResponseModel | null;
  joinToken: string | null;
  members: HouseholdMemberResponseModel[];
  dietaryScope: 'household' | 'individual';
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
    MatSlideToggleModule,
    MatDialogModule,
  ],
  templateUrl: './household.component.html',
  styleUrl: './household.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Household implements OnInit {
  mode = input<'standalone' | 'wizard'>('standalone');

  stepComplete = output<HouseholdFormData>();
  skipped = output<void>();
  saved = output<HouseholdFormData>();

  private fb = inject(FormBuilder);
  private dialog = inject(MatDialog);
  private householdService = inject(HouseholdService);
  private authService = inject(AuthService);
  private loadingService = inject(LoadingService);

  households = signal<HouseholdResponseModel[]>([]);
  members = signal<HouseholdMemberResponseModel[]>([]);
  activeHouseholdId = signal<number | null>(null);
  dietaryScope = signal<'household' | 'individual'>('household');
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');
  activeTab = signal<'create' | 'join'>('create');

  isStandalone = computed(() => this.mode() !== 'wizard');
  hasHousehold = computed(() => this.households().length > 0);
  primaryUserName = computed(() => this.authService.username() || 'You');
  currentPersonId = computed(() => this.authService.personId());

  // Non-user members (exclude the primary user from the grid's "other members" section)
  nonUserMembers = computed(() => {
    const personId = this.currentPersonId();
    return this.members().filter(m => m.personId !== personId);
  });

  createForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    description: ['', Validators.maxLength(2047)],
  });

  joinForm = this.fb.group({
    token: ['', Validators.required],
  });

  ngOnInit(): void {
    this.loadHouseholds();
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
        this.loadHouseholds();
        if (this.isStandalone()) {
          this.createForm.reset();
          this.saved.emit(this.buildFormData(household, null));
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
        this.loadHouseholds();
        if (this.isStandalone()) {
          this.joinForm.reset();
          this.saved.emit(this.buildFormData(null, token));
        }
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Invalid or expired invite code. Please check and try again.');
      },
    });
  }

  onContinue(): void {
    this.stepComplete.emit(this.buildFormData(null, null));
  }

  onSkip(): void {
    this.skipped.emit();
  }

  onDietaryScopeChange(checked: boolean): void {
    this.dietaryScope.set(checked ? 'individual' : 'household');
  }

  openAddMemberDialog(): void {
    const householdId = this.activeHouseholdId();
    if (!householdId) return;

    const dialogRef = this.dialog.open(AddMemberDialog, {
      width: '600px',
      disableClose: true,
      data: { householdId } as AddMemberDialogData,
    });

    dialogRef.afterClosed().subscribe((result: number | undefined) => {
      if (result) {
        this.loadMembersForHousehold(householdId);
      }
    });
  }

  openEditMemberDialog(member: HouseholdMemberResponseModel, initialStep?: number): void {
    const householdId = this.activeHouseholdId();
    if (!householdId) return;

    const dialogRef = this.dialog.open(AddMemberDialog, {
      width: '600px',
      disableClose: true,
      data: {
        householdId,
        personId: member.personId,
        personName: member.personName,
        personEmail: member.personEmail,
        initialStep,
      } as AddMemberDialogData,
    });

    dialogRef.afterClosed().subscribe((result: number | undefined) => {
      if (result) {
        this.loadMembersForHousehold(householdId);
      }
    });
  }

  removeMember(member: HouseholdMemberResponseModel): void {
    const householdId = this.activeHouseholdId();
    if (!householdId) return;

    this.householdService.removeMember(householdId, member.id).subscribe({
      next: () => this.loadMembersForHousehold(householdId),
      error: () => this.errorMessage.set('Unable to remove member. Please try again.'),
    });
  }

  private loadHouseholds(): void {
    this.householdService.getHouseholds().pipe(
      this.loadingService.loading('Loading households...')
    ).subscribe({
      next: (list) => {
        this.households.set(list);
        if (list.length > 0) {
          this.loadMembersForHousehold(list[0].id);
        }
      },
      error: () => this.errorMessage.set('Unable to load households.'),
    });
  }

  private loadMembersForHousehold(householdId: number): void {
    this.activeHouseholdId.set(householdId);
    this.householdService.getHousehold(householdId).subscribe({
      next: (household) => this.members.set(household.members ?? []),
      error: () => {},
    });
  }

  private buildFormData(household: HouseholdCreateResponseModel | null, joinToken: string | null): HouseholdFormData {
    return {
      household,
      joinToken,
      members: this.members(),
      dietaryScope: this.dietaryScope(),
    };
  }
}
