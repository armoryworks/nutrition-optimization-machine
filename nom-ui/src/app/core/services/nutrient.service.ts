import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';
import { NutrientModel } from '../models/nutrient.model';

@Injectable({ providedIn: 'root' })
export class NutrientService {
  private http = inject(HttpClient);
  private all$?: Observable<NutrientModel[]>;

  /** All nutrient definitions; reference data, cached for the session. */
  getAll(): Observable<NutrientModel[]> {
    this.all$ ??= this.http
      .get<NutrientModel[]>(`${environment.apiUrl}/Nutrient/all`)
      .pipe(shareReplay({ bufferSize: 1, refCount: false }));
    return this.all$;
  }
}
