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

  constructor(private http: HttpClient) { }

  /**
   * Get all measurement types from the reference data
   */
  getMeasurementTypes(): Observable<ReferenceItemModel[]> {
    return this.http.get<ReferenceItemModel[]>(`${this.apiUrl}/measurement-types`);
  }

  /**
   * Get attribute types for person health attributes
   */
  getAttributeTypes(): Observable<ReferenceItemModel[]> {
    return this.http.get<ReferenceItemModel[]>(`${this.apiUrl}/attribute-types`);
  }
} 