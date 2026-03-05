import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MeasurementOption } from '../models/measurement.model';

@Injectable({ providedIn: 'root' })
export class MeasurementService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Measurement`;

  private cache = signal<MeasurementOption[]>([]);
  readonly measurements = this.cache.asReadonly();

  loadMeasurements(): Observable<MeasurementOption[]> {
    if (this.cache().length > 0) {
      return new Observable(sub => { sub.next(this.cache()); sub.complete(); });
    }
    return this.http.get<MeasurementOption[]>(`${this.apiUrl}/all`).pipe(
      tap(data => this.cache.set(data)),
    );
  }
}
