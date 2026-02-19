import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../core/services/auth.service';
import { PersonService } from '../core/services/person.service';
import { LoadingService } from '../core/services/loading.service';
import { OnboardingCompleteRequest, RestrictionRequest, SaveProfileRequest } from '../core/models/person.model';
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
})
export class Onboarding implements OnInit {
  private router = inject(Router);
  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private loadingService = inject(LoadingService);

  currentStep = signal(0);
  completedSteps = signal<Set<number>>(new Set());
  errorMessage = signal('');

  profileData = signal<ProfileFormData | null>(null);
  profileSaved = signal(false);
  restrictionsData = signal<RestrictionRequest[]>([]);
  householdData = signal<HouseholdFormData | null>(null);
  planData = signal<PlanFormData | null>(null);

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
      this.loadingService.loading('Checking your progress...')
    ).subscribe({
      next: (state) => {
        if (state.isComplete) {
          this.router.navigate(['/home']);
          return;
        }
        if (state.currentStep > 0 && state.currentStep < this.totalSteps) {
          this.currentStep.set(state.currentStep);
        }
        // Pre-populate from existing state
        if (state.personDetails?.name) {
          this.profileData.set({
            personDetails: state.personDetails,
            attributes: state.attributes ?? [],
          });
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
      this.loadingService.loading('Saving profile...')
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
      this.loadingService.loading('Saving dietary preferences...')
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
    const plan = this.planData();

    const request: OnboardingCompleteRequest = {
      personId: personId,
      personDetails: profile?.personDetails ?? { id: 0, name: '', attributes: [] },
      attributes: this.profileSaved() ? [] : (profile?.attributes ?? []),
      restrictions: restrictions,
      planInvitationCode: plan?.invitationCode ?? null,
      hasAdditionalParticipants: false,
      numberOfAdditionalParticipants: 0,
      additionalParticipantDetails: [],
      applyIndividualPreferencesToEachPerson: false,
    };

    this.personService.completeOnboarding(personId, request).pipe(
      this.loadingService.loading('Setting up your account...')
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
