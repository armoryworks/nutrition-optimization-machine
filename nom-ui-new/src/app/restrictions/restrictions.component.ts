import { Component, inject, input, output, signal, computed, OnInit } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ReferenceService } from '../core/services/reference.service';
import { LoadingService } from '../core/services/loading.service';
import { ReferenceItem, ReferenceDiscriminator } from '../core/models/reference.model';
import { RestrictionRequest } from '../core/models/person.model';

interface RestrictionCategory {
  label: string;
  items: ReferenceItem[];
}

// Client-side categorization of restriction type IDs (discriminator 2000)
const ALLERGY_IDS = new Set([2012, 2013, 2014, 2015, 2016, 2017, 2018]);
const RELIGIOUS_IDS = new Set([2010, 2011]);
const SENSITIVITY_IDS = new Set([2002, 2019]);

@Component({
  selector: 'nom-restrictions',
  imports: [
    MatChipsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './restrictions.component.html',
  styleUrl: './restrictions.component.scss',
})
export class Restrictions implements OnInit {
  mode = input<'standalone' | 'wizard'>('standalone');
  initialRestrictions = input<RestrictionRequest[]>([]);

  stepComplete = output<RestrictionRequest[]>();
  saved = output<RestrictionRequest[]>();

  private referenceService = inject(ReferenceService);
  private loadingService = inject(LoadingService);

  allRestrictions = signal<ReferenceItem[]>([]);
  selectedIds = signal<Set<number>>(new Set());
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  isStandalone = computed(() => this.mode() === 'standalone');

  categories = computed<RestrictionCategory[]>(() => {
    const items = this.allRestrictions();
    if (items.length === 0) return [];

    const dietary: ReferenceItem[] = [];
    const allergies: ReferenceItem[] = [];
    const religious: ReferenceItem[] = [];
    const other: ReferenceItem[] = [];

    for (const item of items) {
      if (ALLERGY_IDS.has(item.referenceId)) {
        allergies.push(item);
      } else if (RELIGIOUS_IDS.has(item.referenceId)) {
        religious.push(item);
      } else if (SENSITIVITY_IDS.has(item.referenceId)) {
        other.push(item);
      } else {
        dietary.push(item);
      }
    }

    const result: RestrictionCategory[] = [];
    if (dietary.length) result.push({ label: 'Dietary Preferences', items: dietary });
    if (allergies.length) result.push({ label: 'Allergies', items: allergies });
    if (religious.length) result.push({ label: 'Religious & Cultural', items: religious });
    if (other.length) result.push({ label: 'Sensitivities & Other', items: other });
    return result;
  });

  ngOnInit(): void {
    this.loadRestrictionTypes();

    const initial = this.initialRestrictions();
    if (initial.length > 0) {
      this.selectedIds.set(new Set(initial.map(r => r.restrictionTypeId)));
    }
  }

  toggleRestriction(id: number): void {
    this.selectedIds.update(set => {
      const next = new Set(set);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  isSelected(id: number): boolean {
    return this.selectedIds().has(id);
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

  private loadRestrictionTypes(): void {
    this.referenceService.getRestrictionTypes().pipe(
      this.loadingService.loading('Loading dietary options...')
    ).subscribe({
      next: (items) => this.allRestrictions.set(items),
      error: () => this.errorMessage.set('Unable to load dietary options.'),
    });
  }

  private buildRestrictions(): RestrictionRequest[] {
    const selected = this.selectedIds();
    return this.allRestrictions()
      .filter(item => selected.has(item.referenceId))
      .map(item => ({
        name: item.referenceName,
        description: item.referenceDescription,
        restrictionTypeId: item.referenceId,
        appliesToEntirePlan: true,
        affectedPersonIds: null,
      }));
  }
}
