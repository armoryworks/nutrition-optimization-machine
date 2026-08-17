import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PlatformFeatureModel } from '../models/platform-feature.model';

/**
 * Platform feature switches: a subsystem can ship dark and be switched on
 * deliberately. Listing and setting are admin-only (CanManageUserRoles);
 * reading a single key is open so the apps can hide entry points.
 */
@Injectable({ providedIn: 'root' })
export class PlatformFeatureService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/PlatformFeature`;

  /** All switches with descriptions and audit timestamps (admin only). */
  list(): Observable<PlatformFeatureModel[]> {
    return this.http.get<PlatformFeatureModel[]>(this.apiUrl);
  }

  /** Flip one switch; the server returns the stored state (admin only). */
  set(key: string, isEnabled: boolean): Observable<PlatformFeatureModel> {
    return this.http.put<PlatformFeatureModel>(
      `${this.apiUrl}/${encodeURIComponent(key)}`,
      { isEnabled },
    );
  }

  /**
   * Whether one feature is on — safe for any caller. Treats an unreachable
   * or unknown switch as OFF so a dark feature never leaks on error.
   */
  isEnabled(key: string): Observable<boolean> {
    return this.http.get<PlatformFeatureModel>(`${this.apiUrl}/${encodeURIComponent(key)}`).pipe(
      map((feature) => feature.isEnabled),
      catchError(() => of(false)),
    );
  }
}
