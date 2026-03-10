import { Component, inject, signal, computed, viewChild, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PersonService } from '../../core/services/person.service';
import { LoadingService } from '../../core/services/loading.service';
import { RestrictionRequest } from '../../core/models/restriction-request.model';
import { Profile, ProfileFormData } from '../../profile/profile.component';
import { Restrictions } from '../../restrictions/restrictions.component';

export interface AddMemberDialogData {
  householdId: number;
  personId?: number;
  personName?: string;
  personEmail?: string | null;
  initialStep?: number;
}

@Component({
  selector: 'nom-add-member-dialog',
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    Profile,
    Restrictions,
  ],
  templateUrl: './add-member-dialog.component.html',
  styleUrl: './add-member-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddMemberDialog implements OnInit {
  private dialogRef = inject(MatDialogRef<AddMemberDialog>);
  data: AddMemberDialogData = inject(MAT_DIALOG_DATA);
  private personService = inject(PersonService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  loading = signal(false);
  errorMessage = signal('');
  currentStep = signal(0);
  createdPersonId = signal<number | null>(null);
  profileInitialData = signal<ProfileFormData | null>(null);
  restrictionsInitialData = signal<RestrictionRequest[]>([]);

  profileRef = viewChild(Profile);
  restrictionsRef = viewChild(Restrictions);

  isEditMode = computed(() => !!this.data.personId);

  ngOnInit(): void {
    if (this.data.personId) {
      this.createdPersonId.set(this.data.personId);
      this.loadExistingData(this.data.personId);
    }
  }

  onProfileComplete(profileData: ProfileFormData): void {
    this.loading.set(true);
    this.errorMessage.set('');

    const personId = this.data.personId ?? 0;

    this.personService.saveProfile(personId, {
      name: profileData.personDetails.name,
      attributes: profileData.attributes,
      email: profileData.email,
      householdId: this.data.householdId,
    }).pipe(
      this.loadingService.loading(this.isEditMode() ? 'Saving profile...' : 'Creating member profile...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (person) => {
        this.createdPersonId.set(person.id);
        this.loading.set(false);
        this.currentStep.set(1);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to save member profile. Please try again.');
      },
    });
  }

  onRestrictionsComplete(restrictions: RestrictionRequest[]): void {
    const personId = this.createdPersonId();
    if (!personId) {
      this.close();
      return;
    }

    this.loading.set(true);
    this.personService.saveRestrictions(personId, restrictions).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.loading.set(false);
        this.close();
      },
      error: () => {
        this.loading.set(false);
        this.close(); // Still close — person exists
      },
    });
  }

  skipRestrictions(): void {
    this.close();
  }

  cancel(): void {
    this.dialogRef.close(undefined);
  }

  private close(): void {
    this.dialogRef.close(this.createdPersonId());
  }

  private loadExistingData(personId: number): void {
    this.personService.getOnboardingState(personId).pipe(
      this.loadingService.loading('Loading member data...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (state) => {
        this.profileInitialData.set({
          personDetails: state.personDetails,
          attributes: state.attributes ?? [],
          email: this.data.personEmail ?? undefined,
        });
        this.restrictionsInitialData.set(state.restrictions ?? []);
        if (this.data.initialStep != null) {
          this.currentStep.set(this.data.initialStep);
        }
      },
      error: () => {
        this.errorMessage.set('Unable to load member data.');
      },
    });
  }
}
