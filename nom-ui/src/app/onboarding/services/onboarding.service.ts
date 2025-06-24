import { Injectable } from '@angular/core';
import { OnboardingCompleteRequestModel } from '../models/onboarding-complete-request.model';
import { PersonModel } from '../../person/models/person.model';
import { BehaviorSubject, Observable } from 'rxjs';
import { RestrictionTypeEnum } from '../../restriction/enums/restriction-type.enum';

@Injectable({
  providedIn: 'root',
})
export class OnboardingService {
  private readonly SESSION_STORAGE_KEY = 'onboardingData';
  private _onboardingData: OnboardingCompleteRequestModel;

  // This BehaviorSubject will allow components to subscribe to data changes
  private _onboardingDataSubject: BehaviorSubject<OnboardingCompleteRequestModel>;
  public onboardingData$: Observable<OnboardingCompleteRequestModel>;

  // Define the workflow steps here as it's part of the global workflow state
  public workflowSteps = [
    {
      id: 'invitationCode',
      title: 'Invitation Code',
      component: 'ui-only-invitation-code',
      required: false,
      dataProperty: 'planInvitationCode',
    },
    {
      id: 'personDetails',
      title: 'Your Details',
      component: 'app-person-edit',
      required: true,
      dataProperty: 'personDetails',
    },
    {
      id: 'additionalParticipants',
      title: 'Additional Participants',
      component: 'app-onboarding-additional-participants',
      required: true,
      dataProperty: null,
    },
    {
      id: 'healthAttributes',
      title: 'Health Attributes',
      component: 'app-person-health-edit',
      required: false,
      dataProperty: 'attributes',
    },
    // Consolidated restriction scope steps into one
    {
      id: 'restrictionScope',
      title: 'Restriction Scope',
      component: 'app-onboarding-restriction-scope',
      required: true,
      dataProperty: null,
    }, // Data handled internally then emitted
    {
      id: 'societalRestrictions',
      title: 'Dietary Practices',
      component: 'app-restriction-edit',
      required: false,
      restrictionType: RestrictionTypeEnum.SocietalReligiousEthical,
    },
    {
      id: 'medicalRestrictions',
      title: 'Medical Restrictions',
      component: 'app-restriction-edit',
      required: false,
      restrictionType: RestrictionTypeEnum.AllergyMedical,
    },
    {
      id: 'personalPreferences',
      title: 'Personal Preferences',
      component: 'app-restriction-edit',
      required: false,
      restrictionType: RestrictionTypeEnum.PersonalPreference,
    },
    {
      id: 'applyIndividualPreferences',
      title: 'Individual Preferences',
      component: 'ui-only-yes-no',
      required: true,
      dataProperty: 'applyIndividualPreferencesToEachPerson',
      condition: (data: OnboardingCompleteRequestModel) =>
        data.hasAdditionalParticipants === true &&
        data.numberOfAdditionalParticipants > 0,
    },
    {
      id: 'summary',
      title: 'Review & Submit',
      component: 'ui-only-summary',
      required: true,
      dataProperty: null,
    },
  ];

  constructor() {
    this._onboardingData = this.loadOnboardingData();
    this._onboardingDataSubject =
      new BehaviorSubject<OnboardingCompleteRequestModel>(this._onboardingData);
    this.onboardingData$ = this._onboardingDataSubject.asObservable();
  }

  /**
   * Loads onboarding data from sessionStorage.
   * If no data is found, initializes a new OnboardingCompleteRequestModel.
   * Also reconstructs PersonModel and RestrictionModel instances from plain objects.
   */
  private loadOnboardingData(): OnboardingCompleteRequestModel {
    const storedData = sessionStorage.getItem(this.SESSION_STORAGE_KEY);
    if (storedData) {
      try {
        const parsedData: OnboardingCompleteRequestModel =
          JSON.parse(storedData);

        // Reconstruct PersonModel instances for deep objects
        if (parsedData.personDetails) {
          parsedData.personDetails = new PersonModel(parsedData.personDetails);
          // Recursively reconstruct attributes if they exist
          if (parsedData.personDetails.attributes) {
            parsedData.personDetails.attributes =
              parsedData.personDetails.attributes.map((attr) => ({ ...attr })); // Attributes are simple, so shallow copy is fine
          }
        }
        if (
          parsedData.additionalParticipantDetails &&
          parsedData.additionalParticipantDetails.length > 0
        ) {
          parsedData.additionalParticipantDetails =
            parsedData.additionalParticipantDetails.map((p) => {
              const person = new PersonModel(p);
              if (person.attributes) {
                person.attributes = person.attributes.map((attr) => ({
                  ...attr,
                }));
              }
              return person;
            });
        }

        // Reconstruct RestrictionModel instances
        if (parsedData.restrictions && parsedData.restrictions.length > 0) {
          parsedData.restrictions = parsedData.restrictions.map((r) => ({
            ...r,
          })); // Restrictions are also mostly flat
        }

        // Ensure these properties are always initialized if they might be null/undefined
        parsedData.additionalParticipantDetails =
          parsedData.additionalParticipantDetails || [];
        parsedData.restrictions = parsedData.restrictions || [];

        return new OnboardingCompleteRequestModel(parsedData); // Create a new instance to ensure methods/defaults
      } catch (e) {
        console.error('Error parsing onboarding data from sessionStorage:', e);
        // Clear corrupted data
        sessionStorage.removeItem(this.SESSION_STORAGE_KEY);
      }
    }
    // Default initial state
    const newOnboardingData = new OnboardingCompleteRequestModel();
    // Initialize primary person with a temporary ID if it doesn't exist
    if (!newOnboardingData.personDetails) {
      newOnboardingData.personDetails = new PersonModel({ id: -1, name: '' });
    }
    return newOnboardingData;
  }

  /**
   * Saves the current onboarding data to sessionStorage.
   * Components should call this whenever they modify the onboarding data.
   */
  public saveOnboardingData(): void {
    sessionStorage.setItem(
      this.SESSION_STORAGE_KEY,
      JSON.stringify(this._onboardingData)
    );
    this._onboardingDataSubject.next(this._onboardingData); // Notify subscribers of the change
  }

  /**
   * Gets the current onboarding data.
   * Components should use this to get the latest data before making changes.
   */
  public getOnboardingData(): OnboardingCompleteRequestModel {
    return this._onboardingData;
  }

  /**
   * Clears all onboarding data from sessionStorage.
   * Should be called when onboarding is complete or cancelled.
   */
  public clearOnboardingData(): void {
    sessionStorage.removeItem(this.SESSION_STORAGE_KEY);
    this._onboardingData = new OnboardingCompleteRequestModel(); // Reset internal state
    this._onboardingDataSubject.next(this._onboardingData); // Notify subscribers
  }

  /**
   * Updates a specific property in the onboarding data.
   * @param propertyName The name of the property to update.
   * @param value The new value for the property.
   */
  public updateOnboardingProperty<
    K extends keyof OnboardingCompleteRequestModel
  >(propertyName: K, value: OnboardingCompleteRequestModel[K]): void {
    (this._onboardingData as any)[propertyName] = value;
    this.saveOnboardingData(); // Save immediately after update
  }
}
