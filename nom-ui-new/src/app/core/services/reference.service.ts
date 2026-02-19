import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ReferenceItem, ReferenceDiscriminator } from '../models/reference.model';

@Injectable({ providedIn: 'root' })
export class ReferenceService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Reference`;
  private cache = signal<Map<number, ReferenceItem[]>>(new Map());

  getReferencesByGroup(discriminatorId: number): Observable<ReferenceItem[]> {
    const cached = this.cache().get(discriminatorId);
    if (cached) return of(cached);

    return this.http.get<ReferenceItem[]>(`${this.apiUrl}/${discriminatorId}/all`).pipe(
      tap(items => this.cache.update(m => new Map(m).set(discriminatorId, items)))
    );
  }

  getReferencesBulk(discriminatorIds: number[]): Observable<Record<number, ReferenceItem[]>> {
    const uncached = discriminatorIds.filter(id => !this.cache().has(id));
    if (uncached.length === 0) {
      const result: Record<number, ReferenceItem[]> = {};
      for (const id of discriminatorIds) {
        result[id] = this.cache().get(id) ?? [];
      }
      return of(result);
    }

    return this.http.post<Record<number, ReferenceItem[]>>(`${this.apiUrl}/bulk`, uncached).pipe(
      tap(data => {
        this.cache.update(m => {
          const next = new Map(m);
          for (const [key, items] of Object.entries(data)) {
            next.set(Number(key), items);
          }
          return next;
        });
      })
    );
  }

  getActivityLevels(): Observable<ReferenceItem[]> {
    return this.getReferencesByGroup(ReferenceDiscriminator.PersonActivityLevelType);
  }

  getHealthGoals(): Observable<ReferenceItem[]> {
    return this.getReferencesByGroup(ReferenceDiscriminator.PersonHealthGoalType);
  }

  getAttributeTypes(): Observable<ReferenceItem[]> {
    return this.getReferencesByGroup(ReferenceDiscriminator.PersonAttributeType);
  }

  getRestrictionTypes(): Observable<ReferenceItem[]> {
    return this.getReferencesByGroup(ReferenceDiscriminator.RestrictionType);
  }

  getRestrictionGroups(): Observable<Record<number, ReferenceItem[]>> {
    return this.getReferencesBulk([
      ReferenceDiscriminator.PersonDietaryRestrictionType,
      ReferenceDiscriminator.AllergyType,
      ReferenceDiscriminator.MedicalConditionType,
      ReferenceDiscriminator.SocietalRestrictionType,
      ReferenceDiscriminator.PersonalPreferenceType,
    ]);
  }
}
