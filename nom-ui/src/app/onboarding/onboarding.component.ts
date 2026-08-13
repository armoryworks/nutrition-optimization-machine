import { Component, DestroyRef, inject, signal, computed, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../core/services/auth.service';
import { PersonService } from '../core/services/person.service';
import { LoadingService } from '../core/services/loading.service';
import { OnboardingCompleteRequest } from '../core/models/onboarding-complete-request.model';
import { RestrictionRequest } from '../core/models/restriction-request.model';
import { SaveProfileRequest } from '../core/models/save-profile-request.model';
import { Profile, ProfileFormData } from '../profile/profile.component';
import { Restrictions } from '../restrictions/restrictions.component';
import { Household, HouseholdFormData } from '../household/household.component';
import { Plan, PlanFormData } from '../plan/plan.component';

@Component({
  selector: 'nom-onboarding',
  imports: [
    MatButtonModule,
    MatIconModule,
    Profile,
    Restrictions,
    Household,
    Plan,
  ],
  templateUrl: './onboarding.component.html',
  styleUrl: './onboarding.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Onboarding implements OnInit {
  private router = inject(Router);
  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  currentStep = signal(0);
  completedSteps = signal<Set<number>>(new Set());
  errorMessage = signal('');

  profileData = signal<ProfileFormData | null>(null);
  profileSaved = signal(false);
  restrictionsData = signal<RestrictionRequest[]>([]);
  householdData = signal<HouseholdFormData | null>(null);
  planData = signal<PlanFormData | null>(null);

  /** The solo/household fork: 'solo' auto-creates a personal kitchen on completion. */
  householdMode = signal<'undecided' | 'solo' | 'household'>('undecided');

  readonly stepLabels = ['Profile', 'Dietary', 'Household', 'Plan'];
  readonly totalSteps = 4;

  isLastStep = computed(() => this.currentStep() === this.totalSteps - 1);
  isFirstStep = computed(() => this.currentStep() === 0);

  ngOnInit(): void {
    const personId = this.authService.personId();
    if (!personId) {
      this.router.navigate(['/home']);
      return;
    }

    this.personService.getOnboardingState(personId).pipe(
      this.loadingService.loading('Checking your progress...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (state) => {
        if (state.isComplete) {
          this.router.navigate(['/home']);
          return;
        }
        // Resume at the correct step and mark prior steps complete
        if (state.currentStep > 0 && state.currentStep < this.totalSteps) {
          this.currentStep.set(state.currentStep);
          const completed = new Set<number>();
          for (let i = 0; i < state.currentStep; i++) {
            completed.add(i);
          }
          this.completedSteps.set(completed);
        }
        // Pre-populate from existing state
        if (state.personDetails?.name) {
          this.profileData.set({
            personDetails: state.personDetails,
            attributes: state.attributes ?? [],
          });
          this.profileSaved.set(state.attributes?.length > 0);
        }
        if (state.restrictions?.length) {
          this.restrictionsData.set(state.restrictions);
        }
      },
    });
  }

  onProfileComplete(data: ProfileFormData): void {
    this.profileData.set(data);

    const personId = this.authService.personId();
    if (!personId) {
      this.markComplete(0);
      this.next();
      return;
    }

    const request: SaveProfileRequest = {
      name: data.personDetails.name,
      attributes: data.attributes,
    };

    this.personService.saveProfile(personId, request).pipe(
      this.loadingService.loading('Saving profile...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.profileSaved.set(true);
        this.markComplete(0);
        this.next();
      },
      error: () => {
        this.errorMessage.set('Unable to save profile. Please try again.');
      },
    });
  }

  onRestrictionsComplete(restrictions: RestrictionRequest[]): void {
    this.restrictionsData.set(restrictions);

    const personId = this.authService.personId();
    if (!personId) {
      this.markComplete(1);
      this.next();
      return;
    }

    this.personService.saveRestrictions(personId, restrictions).pipe(
      this.loadingService.loading('Saving dietary preferences...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.markComplete(1);
        this.next();
      },
      error: () => {
        this.errorMessage.set('Unable to save dietary preferences. Please try again.');
      },
    });
  }

  onHouseholdComplete(data: HouseholdFormData): void {
    this.householdData.set(data);
    this.markComplete(2);
    this.next();
  }

  onHouseholdSkipped(): void {
    this.next();
  }

  /** "Just me": skip household naming/invites entirely; a personal kitchen is auto-created on completion. */
  chooseSolo(): void {
    this.householdMode.set('solo');
    this.householdData.set(null);
    this.markComplete(2);
    this.next();
  }

  chooseHousehold(): void {
    this.householdMode.set('household');
  }

  backToHouseholdChoice(): void {
    this.householdMode.set('undecided');
  }

  onPlanComplete(data: PlanFormData): void {
    this.planData.set(data);
    this.markComplete(3);
    this.completeOnboarding();
  }

  next(): void {
    if (this.currentStep() < this.totalSteps - 1) {
      this.currentStep.update(s => s + 1);
    }
  }

  previous(): void {
    if (this.currentStep() > 0) {
      this.currentStep.update(s => s - 1);
    }
  }

  skipStep(): void {
    if (this.isLastStep()) {
      this.completeOnboarding();
    } else {
      this.next();
    }
  }

  goToStep(index: number): void {
    if (index >= 0 && index < this.totalSteps) {
      this.currentStep.set(index);
    }
  }

  completeOnboarding(): void {
    const personId = this.authService.personId();
    if (!personId) {
      this.errorMessage.set('Unable to identify your account. Please try logging in again.');
      return;
    }

    const profile = this.profileData();
    const restrictions = this.restrictionsData();
    const household = this.householdData();
    const plan = this.planData();
    const members = household?.members ?? [];

    const request: OnboardingCompleteRequest = {
      personId: personId,
      personDetails: profile?.personDetails ?? { id: 0, name: '', attributes: [] },
      attributes: this.profileSaved() ? [] : (profile?.attributes ?? []),
      restrictions: restrictions,
      planInvitationCode: plan?.invitationCode ?? null,
      hasAdditionalParticipants: members.length > 0,
      numberOfAdditionalParticipants: members.length,
      additionalParticipantDetails: members.map(m => ({
        id: m.personId,
        name: m.personName,
        attributes: [],
      })),
      applyIndividualPreferencesToEachPerson: household?.dietaryScope === 'individual',
      createSoloHousehold: this.householdMode() === 'solo',
    };

    this.personService.completeOnboarding(personId, request).pipe(
      this.loadingService.loading('Setting up your account...'),
      switchMap(() => this.authService.refreshClaims()),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.router.navigate(['/home']);
      },
      error: () => {
        this.errorMessage.set('Something went wrong. Please try again.');
      },
    });
  }

  private markComplete(step: number): void {
    this.completedSteps.update(set => {
      const next = new Set(set);
      next.add(step);
      return next;
    });
  }
}
