import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PrivacyAnalyticsModel, DataProcessingLogModel, ComplianceMetricModel, RiskFactorModel } from '../models/i-privacy-analytics.model';

@Injectable({
    providedIn: 'root'
})
export class PrivacyAnalyticsService {
    private http = inject(HttpClient);

    private readonly baseUrl = `${environment.apiUrl}/privacy`;



    getPrivacyAnalytics(): Observable<PrivacyAnalyticsModel> {
        return this.http.get<PrivacyAnalyticsModel>(`${this.baseUrl}/analytics`);
    }

    getDataProcessingLogs(): Observable<DataProcessingLogModel[]> {
        return this.http.get<DataProcessingLogModel[]>(`${this.baseUrl}/processing-logs`);
    }

    generateComplianceReport(): Observable<ComplianceMetricModel> {
        return this.http.post<ComplianceMetricModel>(`${this.baseUrl}/compliance-report`, {});
    }

    exportAnalytics(): Observable<Blob> {
        return this.http.get(`${this.baseUrl}/analytics/export`, {
            responseType: 'blob'
        });
    }

    getPrivacyImpactAssessment(): Observable<PrivacyAnalyticsModel> {
        return this.http.get<PrivacyAnalyticsModel>(`${this.baseUrl}/privacy-impact-assessment`);
    }

    getDataBreachIncidents(): Observable<DataProcessingLogModel[]> {
        return this.http.get<DataProcessingLogModel[]>(`${this.baseUrl}/data-breach-incidents`);
    }

    getCrossBorderTransfers(): Observable<DataProcessingLogModel[]> {
        return this.http.get<DataProcessingLogModel[]>(`${this.baseUrl}/cross-border-transfers`);
    }

    getDataRetentionMetrics(): Observable<PrivacyAnalyticsModel> {
        return this.http.get<PrivacyAnalyticsModel>(`${this.baseUrl}/data-retention-metrics`);
    }

    getConsentAnalytics(): Observable<PrivacyAnalyticsModel> {
        return this.http.get<PrivacyAnalyticsModel>(`${this.baseUrl}/consent-analytics`);
    }

    getDataSubjectRightsMetrics(): Observable<PrivacyAnalyticsModel> {
        return this.http.get<PrivacyAnalyticsModel>(`${this.baseUrl}/data-subject-rights-metrics`);
    }

    generatePrivacyReport(startDate: string, endDate: string): Observable<Blob> {
        return this.http.post(`${this.baseUrl}/privacy-report`, {
            startDate,
            endDate
        }, {
            responseType: 'blob'
        });
    }

    getRiskAssessment(): Observable<RiskFactorModel[]> {
        return this.http.get<RiskFactorModel[]>(`${this.baseUrl}/risk-assessment`);
    }

    getComplianceMetrics(): Observable<ComplianceMetricModel> {
        return this.http.get<ComplianceMetricModel>(`${this.baseUrl}/compliance-metrics`);
    }
} 