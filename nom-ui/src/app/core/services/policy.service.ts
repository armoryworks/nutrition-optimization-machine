import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { HouseholdStore } from './household-store';
import { MemberPolicyModel, FeatureGateKey } from '../models/member-policy.model';

/**
 * Caching layer for the caller's own member policy per household, plus the
 * steward mutation surface (member policies and restriction locks).
 *
 * Feature-gate semantics: an absent gate key means allowed; an explicit
 * `false` means gated. While a policy has not loaded yet (or fails to load)
 * nothing is gated — gating is a policy enforcement aid, enforced
 * server-side; the UI only mirrors it.
 */
@Injectable({ providedIn: 'root' })
export class PolicyService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private householdStore = inject(HouseholdStore);
  private readonly apiUrl = `${environment.apiUrl}/Household`;

  /** Own policies keyed by householdId. */
  private ownPolicies = signal<Map<number, MemberPolicyModel>>(new Map());
  private inFlight = new Set<number>();

  /** Household the "primary" gate helpers refer to (the user's first household). */
  private primaryHouseholdId = signal<number | null>(null);
  private primaryRequested = false;

  constructor() {
    // A different user must never see a cached policy.
    effect(() => {
      this.authService.isLoggedIn();
      this.ownPolicies.set(new Map());
      this.inFlight.clear();
      this.primaryHouseholdId.set(null);
      this.primaryRequested = false;
    });
  }

  // ── Own policy (cached) ──

  /** Fetch-and-cache the caller's own policy for a household (idempotent). */
  loadOwnPolicy(householdId: number): void {
    const personId = this.authService.personId();
    if (!personId || this.ownPolicies().has(householdId) || this.inFlight.has(householdId)) {
      return;
    }
    this.inFlight.add(householdId);
    this.getMemberPolicy(householdId, personId).subscribe({
      next: (policy) => {
        this.inFlight.delete(householdId);
        this.ownPolicies.update((map) => new Map(map).set(householdId, policy));
      },
      error: () => {
        this.inFlight.delete(householdId);
      },
    });
  }

  /**
   * Fetch-and-cache the caller's policy for their first household — used by
   * surfaces without their own household context (recipe pages, sidebar).
   */
  loadOwnPolicyForPrimaryHousehold(): void {
    if (this.primaryRequested) return;
    this.primaryRequested = true;
    this.householdStore.getHouseholds().subscribe({
      next: (list) => {
        if (list.length > 0) {
          this.primaryHouseholdId.set(list[0].id);
          this.loadOwnPolicy(list[0].id);
        }
      },
      error: () => {
        this.primaryRequested = false;
      },
    });
  }

  /** True when the caller's policy for this household gates the feature. */
  isGated(householdId: number | null, key: FeatureGateKey): boolean {
    if (householdId == null) return false;
    const policy = this.ownPolicies().get(householdId);
    return policy?.featureGates?.[key] === false;
  }

  /** `isGated` against the caller's first household. */
  isGatedPrimary(key: FeatureGateKey): boolean {
    return this.isGated(this.primaryHouseholdId(), key);
  }

  /** The caller's own policy for a household, if loaded. */
  ownPolicy = (householdId: number | null): MemberPolicyModel | null =>
    householdId == null ? null : this.ownPolicies().get(householdId) ?? null;

  /** Signal-friendly view of the caller's curated-only flag for a household. */
  curatedOnly = computed(() => {
    const id = this.primaryHouseholdId();
    return id != null && (this.ownPolicies().get(id)?.curatedOnly ?? false);
  });

  // ── Raw policy endpoints (steward editing reads/writes any member) ──

  getMemberPolicy(householdId: number, personId: number): Observable<MemberPolicyModel> {
    return this.http.get<MemberPolicyModel>(
      `${this.apiUrl}/${householdId}/members/${personId}/policy`,
    );
  }

  saveMemberPolicy(
    householdId: number,
    personId: number,
    policy: MemberPolicyModel,
  ): Observable<MemberPolicyModel> {
    return this.http
      .put<MemberPolicyModel>(`${this.apiUrl}/${householdId}/members/${personId}/policy`, policy)
      .pipe(
        tap((saved) => {
          // Keep the own-policy cache honest when a steward edits themselves.
          if (personId === this.authService.personId()) {
            this.ownPolicies.update((map) => new Map(map).set(householdId, saved));
          }
        }),
      );
  }

  // ── Restriction locks (steward only) ──

  lockRestriction(householdId: number, restrictionId: number): Observable<void> {
    return this.http.post<void>(
      `${this.apiUrl}/${householdId}/restrictions/${restrictionId}/lock`,
      {},
    );
  }

  unlockRestriction(householdId: number, restrictionId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.apiUrl}/${householdId}/restrictions/${restrictionId}/lock`,
    );
  }
}
