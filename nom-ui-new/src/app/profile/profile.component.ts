import { Component, inject, input, output, signal, computed, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ReferenceService } from '../core/services/reference.service';
import { PersonService } from '../core/services/person.service';
import { LoadingService } from '../core/services/loading.service';
import { ReferenceItem, ReferenceDiscriminator } from '../core/models/reference.model';
import { PersonAttributeRequest, PersonDetailsRequest } from '../core/models/person.model';

export interface ProfileFormData {
  personDetails: PersonDetailsRequest;
  attributes: PersonAttributeRequest[];
}

@Component({
  selector: 'nom-profile',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
})
export class Profile implements OnInit {
  mode = input<'standalone' | 'wizard'>('standalone');
  initialData = input<ProfileFormData | null>(null);

  stepComplete = output<ProfileFormData>();
  saved = output<ProfileFormData>();

  private fb = inject(FormBuilder);
  private referenceService = inject(ReferenceService);
  private personService = inject(PersonService);
  private loadingService = inject(LoadingService);

  activityLevels = signal<ReferenceItem[]>([]);
  healthGoals = signal<ReferenceItem[]>([]);
  attributeTypes = signal<ReferenceItem[]>([]);
  unitSystem = signal<'imperial' | 'metric'>('imperial');
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  profileForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    dateOfBirth: [null as Date | null],
    gender: [''],
    heightFeet: [null as number | null, [Validators.min(1), Validators.max(8)]],
    heightInches: [null as number | null, [Validators.min(0), Validators.max(11)]],
    heightCm: [null as number | null, [Validators.min(30), Validators.max(250)]],
    weightLbs: [null as number | null, [Validators.min(1), Validators.max(1000)]],
    weightKg: [null as number | null, [Validators.min(1), Validators.max(500)]],
    activityLevel: [null as number | null],
    healthGoal: [null as number | null],
  });

  genderOptions = ['Male', 'Female', 'Other', 'Prefer not to say'];

  isStandalone = computed(() => this.mode() !== 'wizard');

  ngOnInit(): void {
    this.loadReferenceData();
    const data = this.initialData();
    if (data) {
      this.populateFromData(data);
    } else if (this.isStandalone()) {
      this.loadExistingProfile();
    }
  }

  onSubmit(): void {
    if (this.profileForm.invalid) return;

    const formData = this.buildFormData();
    if (this.isStandalone()) {
      this.saveProfile(formData);
    } else {
      this.stepComplete.emit(formData);
    }
  }

  onUnitSystemChange(value: 'imperial' | 'metric'): void {
    this.unitSystem.set(value);
  }

  private loadReferenceData(): void {
    this.referenceService.getReferencesBulk([
      ReferenceDiscriminator.PersonActivityLevelType,
      ReferenceDiscriminator.PersonHealthGoalType,
      ReferenceDiscriminator.PersonAttributeType,
    ]).pipe(
      this.loadingService.loading('Loading profile options...')
    ).subscribe({
      next: (data) => {
        this.activityLevels.set(data[ReferenceDiscriminator.PersonActivityLevelType] ?? []);
        this.healthGoals.set(data[ReferenceDiscriminator.PersonHealthGoalType] ?? []);
        this.attributeTypes.set(data[ReferenceDiscriminator.PersonAttributeType] ?? []);
      },
    });
  }

  private loadExistingProfile(): void {
    this.personService.getOnboardingState().pipe(
      this.loadingService.loading('Loading your profile...')
    ).subscribe({
      next: (state) => {
        if (state.hasExistingPerson && state.personDetails) {
          this.populateFromData({
            personDetails: state.personDetails,
            attributes: state.attributes ?? [],
          });
        }
      },
    });
  }

  private populateFromData(data: ProfileFormData): void {
    this.profileForm.patchValue({ name: data.personDetails.name });

    for (const attr of data.attributes) {
      const typeName = this.getAttributeTypeName(attr.attributeTypeRefId);
      switch (typeName) {
        case 'Date of Birth':
          this.profileForm.patchValue({ dateOfBirth: new Date(attr.value) });
          break;
        case 'Gender':
          this.profileForm.patchValue({ gender: attr.value });
          break;
        case 'Height': {
          const cm = parseFloat(attr.value);
          if (this.unitSystem() === 'imperial') {
            const totalInches = cm / 2.54;
            this.profileForm.patchValue({
              heightFeet: Math.floor(totalInches / 12),
              heightInches: Math.round(totalInches % 12),
            });
          } else {
            this.profileForm.patchValue({ heightCm: Math.round(cm) });
          }
          break;
        }
        case 'Weight': {
          const kg = parseFloat(attr.value);
          if (this.unitSystem() === 'imperial') {
            this.profileForm.patchValue({ weightLbs: Math.round(kg * 2.20462) });
          } else {
            this.profileForm.patchValue({ weightKg: Math.round(kg * 10) / 10 });
          }
          break;
        }
      }
    }

    // Activity level and health goal are stored as attribute refs
    for (const attr of data.personDetails.attributes ?? []) {
      const typeName = this.getAttributeTypeName(attr.attributeTypeRefId);
      if (typeName === 'Activity Level') {
        // Value is the referenceId of the activity level
        this.profileForm.patchValue({ activityLevel: parseInt(attr.value, 10) });
      } else if (typeName === 'Health Goal') {
        this.profileForm.patchValue({ healthGoal: parseInt(attr.value, 10) });
      }
    }
  }

  private getAttributeTypeName(refId: number): string {
    return this.attributeTypes().find(t => t.referenceId === refId)?.referenceName ?? '';
  }

  private getAttributeTypeId(name: string): number | null {
    return this.attributeTypes().find(t => t.referenceName === name)?.referenceId ?? null;
  }

  private buildFormData(): ProfileFormData {
    const form = this.profileForm.getRawValue();
    const attributes: PersonAttributeRequest[] = [];

    // Height → cm
    const heightId = this.getAttributeTypeId('Height');
    if (heightId) {
      let cm: number | null = null;
      if (this.unitSystem() === 'imperial' && form.heightFeet != null) {
        cm = (form.heightFeet * 12 + (form.heightInches ?? 0)) * 2.54;
      } else if (this.unitSystem() === 'metric' && form.heightCm != null) {
        cm = form.heightCm;
      }
      if (cm != null) {
        attributes.push({ attributeTypeRefId: heightId, value: cm.toFixed(1) });
      }
    }

    // Weight → kg
    const weightId = this.getAttributeTypeId('Weight');
    if (weightId) {
      let kg: number | null = null;
      if (this.unitSystem() === 'imperial' && form.weightLbs != null) {
        kg = form.weightLbs / 2.20462;
      } else if (this.unitSystem() === 'metric' && form.weightKg != null) {
        kg = form.weightKg;
      }
      if (kg != null) {
        attributes.push({ attributeTypeRefId: weightId, value: kg.toFixed(1) });
      }
    }

    // Gender
    const genderId = this.getAttributeTypeId('Gender');
    if (genderId && form.gender) {
      attributes.push({ attributeTypeRefId: genderId, value: form.gender });
    }

    // Date of Birth
    const dobId = this.getAttributeTypeId('Date of Birth');
    if (dobId && form.dateOfBirth) {
      attributes.push({ attributeTypeRefId: dobId, value: form.dateOfBirth.toISOString().split('T')[0] });
    }

    return {
      personDetails: {
        id: 0,
        name: form.name ?? '',
        attributes: [
          ...attributes,
          ...(form.activityLevel ? [{ attributeTypeRefId: form.activityLevel, value: String(form.activityLevel) }] : []),
          ...(form.healthGoal ? [{ attributeTypeRefId: form.healthGoal, value: String(form.healthGoal) }] : []),
        ],
      },
      attributes,
    };
  }

  private saveProfile(formData: ProfileFormData): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    this.personService.upsertPerson({ personName: formData.personDetails.name }).pipe(
      this.loadingService.loading('Saving profile...')
    ).subscribe({
      next: () => {
        this.loading.set(false);
        this.successMessage.set('Profile saved successfully.');
        this.saved.emit(formData);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to save your profile. Please try again.');
      },
    });
  }
}
