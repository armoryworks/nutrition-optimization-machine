import { Component, inject, input, output, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ReferenceService } from '../core/services/reference.service';
import { LoadingService } from '../core/services/loading.service';
import { AuthService } from '../core/services/auth.service';
import { PersonService } from '../core/services/person.service';
import { ReferenceItem } from '../core/models/reference-item.model';
import { ReferenceDiscriminator } from '../core/models/reference-discriminator.model';
import { RestrictionRequest } from '../core/models/restriction-request.model';

interface RestrictionSection {
  groupId: number;
  label: string;
  icon: string;
  allItems: ReferenceItem[];
  searchControl: FormControl<string>;
}

const SECTION_CONFIG: { groupId: number; icon: string }[] = [
  { groupId: ReferenceDiscriminator.PersonDietaryRestrictionType, icon: 'restaurant' },
  { groupId: ReferenceDiscriminator.AllergyType, icon: 'warning' },
  { groupId: ReferenceDiscriminator.MedicalConditionType, icon: 'medical_services' },
  { groupId: ReferenceDiscriminator.SocietalRestrictionType, icon: 'groups' },
  { groupId: ReferenceDiscriminator.PersonalPreferenceType, icon: 'tune' },
];

@Component({
  selector: 'nom-restrictions',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatAutocompleteModule,
    MatChipsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
  ],
  templateUrl: './restrictions.component.html',
  styleUrl: './restrictions.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Restrictions implements OnInit {
  mode = input<'standalone' | 'wizard'>('standalone');
  initialRestrictions = input<RestrictionRequest[]>([]);
  hideActions = input(false);

  stepComplete = output<RestrictionRequest[]>();
  saved = output<RestrictionRequest[]>();

  private referenceService = inject(ReferenceService);
  private loadingService = inject(LoadingService);
  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private destroyRef = inject(DestroyRef);

  sections = signal<RestrictionSection[]>([]);
  selectedIds = signal<Set<number>>(new Set());
  itemLookup = signal<Map<number, ReferenceItem>>(new Map());
  /** Restrictions locked by a steward or provider: read-only, never resubmitted. */
  lockedRestrictions = signal<RestrictionRequest[]>([]);
  errorMessage = signal('');
  successMessage = signal('');
  saving = signal(false);

  isStandalone = computed(() => this.mode() !== 'wizard');

  /** Restriction type ids covered by locked restrictions (not re-addable). */
  private lockedTypeIds = computed(
    () => new Set(this.lockedRestrictions().map(r => r.restrictionTypeId)));

  ngOnInit(): void {
    this.loadRestrictionGroups();

    this.applyRestrictions(this.initialRestrictions() ?? []);
    if (this.isStandalone()) {
      this.loadExistingRestrictions();
    }
  }

  /** Text for a locked restriction's badge, based on who locked it. */
  lockedByLabel(restriction: RestrictionRequest): string {
    const lockedBy = restriction.lockedBy ?? '';
    return lockedBy && !lockedBy.startsWith('person:')
      ? 'Locked by your provider'
      : 'Locked by your household steward';
  }

  /** Split incoming restrictions into locked (read-only) and editable selection. */
  private applyRestrictions(restrictions: RestrictionRequest[]): void {
    const locked = restrictions.filter(r => r.locked);
    const editable = restrictions.filter(r => !r.locked);
    this.lockedRestrictions.set(locked);
    if (restrictions.length > 0) {
      this.selectedIds.set(new Set(editable.map(r => r.restrictionTypeId)));
    }
  }

  private loadExistingRestrictions(): void {
    const personId = this.authService.personId();
    if (!personId) return;

    this.personService.getOnboardingState(personId).pipe(
      this.loadingService.loading('Loading your restrictions...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (state) => this.applyRestrictions(state.restrictions ?? []),
      error: () => {},
    });
  }

  filteredItems(section: RestrictionSection): ReferenceItem[] {
    const search = section.searchControl.value?.toLowerCase() ?? '';
    if (!search) return [];
    const selected = this.selectedIds();
    const locked = this.lockedTypeIds();
    return section.allItems.filter(
      item => !selected.has(item.referenceId) &&
        !locked.has(item.referenceId) &&
        (item.referenceName.toLowerCase().includes(search) ||
         (item.referenceDescription?.toLowerCase().includes(search) ?? false))
    );
  }

  sectionSelectedItems(groupId: number): ReferenceItem[] {
    const section = this.sections().find(s => s.groupId === groupId);
    if (!section) return [];
    const selected = this.selectedIds();
    return section.allItems.filter(item => selected.has(item.referenceId));
  }

  addFromAutocomplete(event: MatAutocompleteSelectedEvent, section: RestrictionSection): void {
    const item = event.option.value as ReferenceItem;
    this.selectedIds.update(set => {
      const next = new Set(set);
      next.add(item.referenceId);
      return next;
    });
    section.searchControl.setValue('');
  }

  removeRestriction(id: number): void {
    this.selectedIds.update(set => {
      const next = new Set(set);
      next.delete(id);
      return next;
    });
  }

  displayFn(): string {
    return '';
  }

  /** Public entry point for parent components to trigger submission. */
  submit(): void {
    this.onSubmit();
  }

  onSubmit(): void {
    // Locked restrictions are never resubmitted — the selection only ever
    // contains editable restrictions, and the server preserves locked ones.
    const restrictions = this.buildRestrictions();
    if (this.isStandalone()) {
      this.saveRestrictions(restrictions);
    } else {
      this.stepComplete.emit(restrictions);
    }
  }

  private saveRestrictions(restrictions: RestrictionRequest[]): void {
    const personId = this.authService.personId();
    if (!personId) {
      this.errorMessage.set('Unable to identify your account. Please try logging in again.');
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    this.personService.saveRestrictions(personId, restrictions).pipe(
      this.loadingService.loading('Saving dietary preferences...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        this.successMessage.set('Dietary preferences saved.');
        this.saved.emit(restrictions);
      },
      error: () => {
        this.saving.set(false);
        this.errorMessage.set('Unable to save your dietary preferences. Please try again.');
      },
    });
  }

  private loadRestrictionGroups(): void {
    this.referenceService.getRestrictionGroups().pipe(
      this.loadingService.loading('Loading dietary options...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (data) => {
        const lookup = new Map<number, ReferenceItem>();
        const builtSections: RestrictionSection[] = [];

        for (const config of SECTION_CONFIG) {
          const items = data[config.groupId] ?? [];
          for (const item of items) {
            lookup.set(item.referenceId, item);
          }
          if (items.length > 0) {
            builtSections.push({
              groupId: config.groupId,
              label: items[0]?.groupName ?? `Group ${config.groupId}`,
              icon: config.icon,
              allItems: items.sort((a, b) => a.referenceName.localeCompare(b.referenceName)),
              searchControl: new FormControl('', { nonNullable: true }),
            });
          }
        }

        this.itemLookup.set(lookup);
        this.sections.set(builtSections);
      },
      error: () => this.errorMessage.set('Unable to load dietary options.'),
    });
  }

  private buildRestrictions(): RestrictionRequest[] {
    const selected = this.selectedIds();
    const lookup = this.itemLookup();
    return [...selected]
      .map(id => lookup.get(id))
      .filter((item): item is ReferenceItem => !!item)
      .map(item => ({
        name: item.referenceName,
        description: item.referenceDescription,
        restrictionTypeId: item.referenceId,
        appliesToEntirePlan: true,
        affectedPersonIds: null,
      }));
  }
}
