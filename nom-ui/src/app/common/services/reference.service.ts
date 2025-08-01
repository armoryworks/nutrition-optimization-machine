// File: nom-ui/src/app/common/services/reference.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ReferenceItemModel } from '../models/reference-item.model';

@Injectable({
  providedIn: 'root'
})
export class ReferenceService {
  private readonly apiUrl = '/api/Reference';

  constructor(private http: HttpClient) {}

  /**
   * Get all measurement types from the reference data
   */
  getMeasurementTypes(): Observable<ReferenceItemModel[]> {
    return this.http.get<ReferenceItemModel[]>(`${this.apiUrl}/measurement-types`);
  }

  /**
   * Get attribute types for person health attributes
   * This would need to be implemented on the backend first
   */
  getAttributeTypes(): Observable<ReferenceItemModel[]> {
    // TODO: Implement backend endpoint for attribute types
    // For now, return mock data
    return new Observable(observer => {
      const mockAttributeTypes: ReferenceItemModel[] = [
        { id: 2001, name: 'Height' },
        { id: 2002, name: 'Weight' },
        { id: 2003, name: 'Activity Level' },
        { id: 2004, name: 'Goal' }
      ];
      observer.next(mockAttributeTypes);
      observer.complete();
    });
  }
} 