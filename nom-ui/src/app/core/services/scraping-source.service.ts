import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ScrapingSourceModel, ScrapingSourceStatus } from '../models/scraping-source.model';

@Injectable({ providedIn: 'root' })
export class ScrapingSourceService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/ScrapingSource`;

  getSources(status?: ScrapingSourceStatus): Observable<ScrapingSourceModel[]> {
    const params = status ? new HttpParams().set('status', status) : new HttpParams();
    return this.http.get<ScrapingSourceModel[]>(this.apiUrl, { params });
  }

  approve(id: number, notes?: string): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/approve`, {
      notes: notes || undefined,
    });
  }

  reject(id: number, notes?: string): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/reject`, {
      notes: notes || undefined,
    });
  }
}
