import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { PrivacyAnalyticsModel, DataProcessingLogModel } from '../models/i-privacy-analytics.model';

@Injectable({
    providedIn: 'root'
})
export class PrivacyAnalyticsService {
    private readonly baseUrl = `${environment.apiUrl}/privacy`;

    constructor(private http: HttpClient) { }

    getPrivacyAnalytics(): Observable<PrivacyAnalyticsModel> {
        return this.http.get<PrivacyAnalyticsModel>(`${this.baseUrl}/analytics`);
    }

    getDataProcessingLogs(): Observable<DataProcessingLogModel[]> {
        return this.http.get<DataProcessingLogModel[]>(`${this.baseUrl}/processing-logs`);
    }

    generateComplianceReport(): Observable<any> {
        return this.http.post(`${this.baseUrl}/compliance-report`, {});
    }

    exportAnalytics(): Observable<Blob> {
        return this.http.get(`${this.baseUrl}/analytics/export`, {
            responseType: 'blob'
        });
    }

    getPrivacyImpactAssessment(): Observable<any> {
        return this.http.get(`${this.baseUrl}/privacy-impact-assessment`);
    }

    getDataBreachIncidents(): Observable<any[]> {
        return this.http.get<any[]>(`${this.baseUrl}/data-breach-incidents`);
    }

    getCrossBorderTransfers(): Observable<any[]> {
        return this.http.get<any[]>(`${this.baseUrl}/cross-border-transfers`);
    }

    getDataRetentionMetrics(): Observable<any> {
        return this.http.get(`${this.baseUrl}/data-retention-metrics`);
    }

    getConsentAnalytics(): Observable<any> {
        return this.http.get(`${this.baseUrl}/consent-analytics`);
    }

    getDataSubjectRightsMetrics(): Observable<any> {
        return this.http.get(`${this.baseUrl}/data-subject-rights-metrics`);
    }

    generatePrivacyReport(startDate: string, endDate: string): Observable<Blob> {
        return this.http.post(`${this.baseUrl}/privacy-report`, {
            startDate,
            endDate
        }, {
            responseType: 'blob'
        });
    }

    getRiskAssessment(): Observable<any> {
        return this.http.get(`${this.baseUrl}/risk-assessment`);
    }

    getComplianceMetrics(): Observable<any> {
        return this.http.get(`${this.baseUrl}/compliance-metrics`);
    }
} 