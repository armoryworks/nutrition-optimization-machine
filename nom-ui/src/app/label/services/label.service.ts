import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LabelResponseModel, LabelCreateRequestModel } from '../models/label.models';

@Injectable({
    providedIn: 'root'
})
export class LabelService {
    private http = inject(HttpClient);
    private apiUrl = `${environment.apiUrl}/label`;

    getLabels(): Observable<LabelResponseModel[]> {
        return this.http.get<LabelResponseModel[]>(this.apiUrl);
    }

    createLabel(request: LabelCreateRequestModel): Observable<number> {
        return this.http.post<number>(this.apiUrl, request);
    }

    updateLabel(id: number, request: LabelCreateRequestModel): Observable<LabelResponseModel> {
        return this.http.put<LabelResponseModel>(`${this.apiUrl}/${id}`, request);
    }

    deleteLabel(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }
}
