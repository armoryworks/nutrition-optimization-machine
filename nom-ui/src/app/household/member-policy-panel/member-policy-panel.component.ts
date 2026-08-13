import { Component, inject, input, signal, computed, effect, untracked, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HttpErrorResponse } from '@angular/common/http';
import { PolicyService } from '../../core/services/policy.service';
import { PersonService } from '../../core/services/person.service';
import { HouseholdMemberResponseModel } from '../../core/models/household-member-response.model';
import { FrequencyCap, FeatureGateKey } from '../../core/models/member-policy.model';
import { RestrictionRequest } from '../../core/models/restriction-request.model';

interface GateRow {
  key: FeatureGateKey;
  label: string;
  description: string;
}

const GATE_ROWS: GateRow[] = [
  { key: 'shuffle', label: 'Shuffle', description: 'Fill meal-plan slots with random recipes' },
  { key: 'recipe_import', label: 'Recipe import', description: 'Import recipes from a URL' },
  { key: 'recipe_create', label: 'Recipe create', description: 'Create new recipes' },
  { key: 'recipe_edit', label: 'Recipe edit', description: 'Edit existing recipes' },
];

/**
 * Steward panel for viewing and editing a member's household policy
 * (feature gates, curated-only, frequency caps) and for locking/unlocking
 * the member's dietary restrictions.
 */
@Component({
  selector: 'nom-member-policy-panel',
  imports: [
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatSlideToggleModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
  ],
  templateUrl: './member-policy-panel.component.html',
  styleUrl: './member-policy-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MemberPolicyPanel {
  householdId = input.required<number>();
  members = input<HouseholdMemberResponseModel[]>([]);

  private policyService = inject(PolicyService);
  private personService = inject(PersonService);
  private destroyRef = inject(DestroyRef);

  gateRows = GATE_ROWS;

  selectedPersonId = signal<number | null>(null);
  loading = signal(false);
  saving = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  // Working copy of the selected member's policy
  featureGates = signal<{ [key: string]: boolean }>({});
  frequencyCaps = signal<FrequencyCap[]>([]);
  curatedOnly = signal(false);

  // The selected member's restrictions (with lock state)
  restrictions = signal<RestrictionRequest[]>([]);
  restrictionBusyId = signal<number | null>(null);

  selectedMember = computed(() =>
    this.members().find(m => m.personId === this.selectedPersonId()) ?? null);

  /** Reset the panel when the household changes. */
  private householdChanged = effect(() => {
    this.householdId();
    untracked(() => {
      this.selectedPersonId.set(null);
      this.restrictions.set([]);
      this.errorMessage.set('');
      this.successMessage.set('');
    });
  });

  onMemberChange(personId: number): void {
    this.selectedPersonId.set(personId);
    this.errorMessage.set('');
    this.successMessage.set('');
    this.loadPolicy(personId);
    this.loadRestrictions(personId);
  }

  /** A gate is allowed unless explicitly set to false. */
  isAllowed(key: FeatureGateKey): boolean {
    return this.featureGates()[key] !== false;
  }

  onGateToggle(key: FeatureGateKey, allowed: boolean): void {
    this.featureGates.update(gates => {
      const next = { ...gates };
      if (allowed) {
        // Absent key means allowed — drop the explicit entry entirely.
        delete next[key];
      } else {
        next[key] = false;
      }
      return next;
    });
  }

  addCap(): void {
    this.frequencyCaps.update(caps => [...caps, { tag: '', maxPerWeek: 1 }]);
  }

  removeCap(index: number): void {
    this.frequencyCaps.update(caps => caps.filter((_, i) => i !== index));
  }

  savePolicy(): void {
    const personId = this.selectedPersonId();
    const householdId = this.householdId();
    if (personId == null || this.saving()) return;

    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    const caps = this.frequencyCaps()
      .map(c => ({ tag: c.tag.trim(), maxPerWeek: Math.max(0, Math.floor(c.maxPerWeek ?? 0)) }))
      .filter(c => c.tag.length > 0);

    this.policyService.saveMemberPolicy(householdId, personId, {
      householdId,
      personId,
      featureGates: this.featureGates(),
      frequencyCaps: caps,
      curatedOnly: this.curatedOnly(),
      updatedBy: null,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.featureGates.set(saved.featureGates ?? {});
        this.frequencyCaps.set(saved.frequencyCaps ?? []);
        this.curatedOnly.set(saved.curatedOnly);
        this.successMessage.set('Member policy saved.');
      },
      error: (err) => {
        this.saving.set(false);
        this.errorMessage.set(this.friendlyError(err, 'Unable to save the member policy. Please try again.'));
      },
    });
  }

  // ── Restriction locks ──

  /** True when the lock was placed by an external manager (not a person). */
  isProviderLocked(restriction: RestrictionRequest): boolean {
    const lockedBy = restriction.lockedBy ?? '';
    return !!restriction.locked && !!lockedBy && !lockedBy.startsWith('person:');
  }

  toggleLock(restriction: RestrictionRequest): void {
    const restrictionId = restriction.id;
    const personId = this.selectedPersonId();
    if (restrictionId == null || personId == null || this.restrictionBusyId() != null) return;

    this.restrictionBusyId.set(restrictionId);
    this.errorMessage.set('');

    const call = restriction.locked
      ? this.policyService.unlockRestriction(this.householdId(), restrictionId)
      : this.policyService.lockRestriction(this.householdId(), restrictionId);

    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.restrictionBusyId.set(null);
        this.loadRestrictions(personId);
      },
      error: (err) => {
        this.restrictionBusyId.set(null);
        this.errorMessage.set(this.friendlyError(err, 'Unable to change the restriction lock. Please try again.'));
      },
    });
  }

  // ── Private helpers ──

  private loadPolicy(personId: number): void {
    this.loading.set(true);
    this.policyService.getMemberPolicy(this.householdId(), personId).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (policy) => {
        this.loading.set(false);
        this.featureGates.set(policy.featureGates ?? {});
        this.frequencyCaps.set(policy.frequencyCaps ?? []);
        this.curatedOnly.set(policy.curatedOnly);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(this.friendlyError(err, "Unable to load the member's policy."));
      },
    });
  }

  private loadRestrictions(personId: number): void {
    this.personService.getOnboardingState(personId).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (state) => this.restrictions.set(state.restrictions ?? []),
      error: () => this.restrictions.set([]),
    });
  }

  private friendlyError(err: unknown, fallback: string): string {
    if (err instanceof HttpErrorResponse) {
      if (err.error?.reason === 'steward_required') {
        return 'Only household stewards can change member policies and restriction locks.';
      }
      if (typeof err.error?.message === 'string' && err.error.message) {
        return err.error.message;
      }
    }
    return fallback;
  }
}
