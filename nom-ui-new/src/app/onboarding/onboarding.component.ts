import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { PersonService } from '../core/services/person.service';
import { LoadingService } from '../core/services/loading.service';
import { OnboardingCompleteRequest, RestrictionRequest } from '../core/models/person.model';
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
  private personService = inject(PersonService);
  private loadingService = inject(LoadingService);

  currentStep = signal(0);
  completedSteps = signal<Set<number>>(new Set());
  errorMessage = signal('');

  profileData = signal<ProfileFormData | null>(null);
  restrictionsData = signal<RestrictionRequest[]>([]);
  householdData = signal<HouseholdFormData | null>(null);
  planData = signal<PlanFormData | null>(null);

  readonly stepLabels = ['Profile', 'Dietary', 'Household', 'Plan'];
  readonly totalSteps = 4;

  isLastStep = computed(() => this.currentStep() === this.totalSteps - 1);
  isFirstStep = computed(() => this.currentStep() === 0);

  ngOnInit(): void {
    this.personService.getOnboardingState().pipe(
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
    this.markComplete(0);
    this.next();
  }

  onRestrictionsComplete(restrictions: RestrictionRequest[]): void {
    this.restrictionsData.set(restrictions);
    this.markComplete(1);
    this.next();
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
    const profile = this.profileData();
    const restrictions = this.restrictionsData();
    const plan = this.planData();

    const request: OnboardingCompleteRequest = {
      personId: null,
      personDetails: profile?.personDetails ?? { id: 0, name: '', attributes: [] },
      attributes: profile?.attributes ?? [],
      restrictions: restrictions,
      planInvitationCode: plan?.invitationCode ?? null,
      hasAdditionalParticipants: false,
      numberOfAdditionalParticipants: 0,
      additionalParticipantDetails: [],
      applyIndividualPreferencesToEachPerson: false,
    };

    this.personService.completeOnboarding(request).pipe(
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
