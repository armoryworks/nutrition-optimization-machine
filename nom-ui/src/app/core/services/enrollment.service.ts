import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { HouseholdEnrollmentInfo } from '../models/household-enrollment-info.model';

/**
 * Client side of managed-household enrollment (design doc §5, per-adult
 * consent): checks a household's managedBy via nom-api and records the
 * caller's own consent directly against the Brigade API (which validates
 * NOM's bearer tokens; the auth interceptor attaches the token as usual).
 *
 * The whole feature is hidden unless `environment.brigadeApiBaseUrl` is set.
 *
 * KNOWN GAP: neither API exposes a "did I already consent?" read endpoint
 * yet, so accepted (and banner-dismissed) household ids are tracked in
 * localStorage per person as a stopgap — clearing browser storage or
 * switching devices resurfaces the banner, and consent recorded elsewhere
 * is invisible here. Replace with a server read once Brigade exposes one.
 */
@Injectable({ providedIn: 'root' })
export class EnrollmentService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private readonly householdUrl = `${environment.apiUrl}/Household`;
  private readonly brigadeUrl = environment.brigadeApiBaseUrl.replace(/\/+$/, '');

  /** True when a Brigade API is configured for this deployment. */
  readonly brigadeEnabled = !!environment.brigadeApiBaseUrl;

  /** Bumped on every localStorage write so computeds re-read the stores. */
  private storageVersion = signal(0);

  getEnrollmentInfo(householdId: number): Observable<HouseholdEnrollmentInfo> {
    return this.http.get<HouseholdEnrollmentInfo>(
      `${this.householdUrl}/${householdId}/enrollment-info`,
    );
  }

  /** Record the caller's own consent for the household's active enrollment. */
  consentByHousehold(householdId: number): Observable<void> {
    return this.http.post<void>(
      `${this.brigadeUrl}/api/v1/enrollments/consent-by-household`,
      { nomHouseholdId: householdId },
    ).pipe(tap(() => this.markAccepted(householdId)));
  }

  /** True when the banner should show for this household. */
  needsConsentBanner(household: { id: number; managedBy?: string | null }): boolean {
    return this.brigadeEnabled
      && !!household.managedBy
      && !this.hasAccepted(household.id)
      && !this.isBannerDismissed(household.id);
  }

  hasAccepted(householdId: number): boolean {
    this.storageVersion();
    return this.readIds('accepted').has(householdId);
  }

  markAccepted(householdId: number): void {
    this.writeId('accepted', householdId);
  }

  isBannerDismissed(householdId: number): boolean {
    this.storageVersion();
    return this.readIds('dismissed').has(householdId);
  }

  dismissBanner(householdId: number): void {
    this.writeId('dismissed', householdId);
  }

  // ── localStorage stopgap (see class note) ──

  private storageKey(kind: 'accepted' | 'dismissed'): string {
    // Scoped per person so a shared browser never carries consent state across accounts.
    return `nom.enrollmentConsent.${kind}.${this.authService.personId() ?? 'anonymous'}`;
  }

  private readIds(kind: 'accepted' | 'dismissed'): Set<number> {
    try {
      const raw = localStorage.getItem(this.storageKey(kind));
      const parsed: unknown = raw ? JSON.parse(raw) : [];
      return new Set(Array.isArray(parsed) ? parsed.filter((v) => typeof v === 'number') : []);
    } catch {
      return new Set();
    }
  }

  private writeId(kind: 'accepted' | 'dismissed', householdId: number): void {
    const ids = this.readIds(kind);
    ids.add(householdId);
    try {
      localStorage.setItem(this.storageKey(kind), JSON.stringify([...ids]));
    } catch {
      // Storage unavailable — the banner will simply reappear.
    }
    this.storageVersion.update((v) => v + 1);
  }
}
