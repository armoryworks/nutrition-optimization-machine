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
import { ReferenceService } from '../core/services/reference.service';
import { LoadingService } from '../core/services/loading.service';
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
  private destroyRef = inject(DestroyRef);

  sections = signal<RestrictionSection[]>([]);
  selectedIds = signal<Set<number>>(new Set());
  itemLookup = signal<Map<number, ReferenceItem>>(new Map());
  errorMessage = signal('');
  successMessage = signal('');

  isStandalone = computed(() => this.mode() !== 'wizard');

  ngOnInit(): void {
    this.loadRestrictionGroups();

    const initial = this.initialRestrictions() ?? [];
    if (initial.length > 0) {
      this.selectedIds.set(new Set(initial.map(r => r.restrictionTypeId)));
    }
  }

  filteredItems(section: RestrictionSection): ReferenceItem[] {
    const search = section.searchControl.value?.toLowerCase() ?? '';
    if (!search) return [];
    const selected = this.selectedIds();
    return section.allItems.filter(
      item => !selected.has(item.referenceId) &&
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
    const restrictions = this.buildRestrictions();
    if (this.isStandalone()) {
      this.successMessage.set('Dietary preferences saved.');
      this.saved.emit(restrictions);
    } else {
      this.stepComplete.emit(restrictions);
    }
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
