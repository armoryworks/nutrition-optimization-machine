import { Component, OnInit, inject } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AmwButtonComponent, AmwCardComponent, AmwTooltipDirective, AmwIconComponent } from 'angular-material-wrap';

import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';
import { PrivacyAnalyticsService } from '../../services/privacy-analytics.service';
import { PrivacyAnalyticsModel } from '../../models/i-privacy-analytics.model';
import { DataProcessingLogModel } from '../../models/i-privacy-analytics.model';

@Component({
    selector: 'nom-privacy-analytics',
    standalone: true,
    imports: [
        MatTableModule,
        MatPaginatorModule,
        MatSortModule,
        MatChipsModule,
        AmwButtonComponent,
        AmwCardComponent,
        AmwTooltipDirective,
        AmwIconComponent,
        BasePageComponent,
    ],
    templateUrl: './privacy-analytics.component.html',
    styleUrls: ['./privacy-analytics.component.scss']
})
export class PrivacyAnalyticsComponent implements OnInit {
    private privacyAnalyticsService = inject(PrivacyAnalyticsService);
    private snackBar = inject(MatSnackBar);

    analytics: PrivacyAnalyticsModel | null = null;
    processingLogs: DataProcessingLogModel[] = [];
    isLoading = false;
    error: string | null = null;

    listConfig: BasePageConfig = {
        title: 'Privacy Analytics',
        subtitle: 'Monitor data processing activities and compliance metrics',
        showRefreshButton: true,
        refreshButtonText: 'Refresh',
        maxWidth: 'none'
    };

    displayedColumns: string[] = ['timestamp', 'actionType', 'actorId', 'ipAddress', 'details'];



    ngOnInit(): void {
        this.loadPrivacyAnalytics();
    }

    loadPrivacyAnalytics(): void {
        this.isLoading = true;
        this.error = null;

        this.privacyAnalyticsService.getPrivacyAnalytics().subscribe({
            next: (analytics) => {
                this.analytics = analytics;
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading privacy analytics:', error);
                this.error = 'Failed to load privacy analytics';
                this.isLoading = false;
                this.snackBar.open('Failed to load privacy analytics', 'Close', { duration: 3000 });
            }
        });

        this.privacyAnalyticsService.getDataProcessingLogs().subscribe({
            next: (logs) => {
                this.processingLogs = logs;
            },
            error: (error) => {
                console.error('Error loading processing logs:', error);
            }
        });
    }

    onRefresh(): void {
        this.loadPrivacyAnalytics();
    }

    onRetry(): void {
        this.loadPrivacyAnalytics();
    }

    getComplianceStatus(): string {
        if (!this.analytics) return 'Unknown';

        const complianceScore = this.analytics.complianceScore;
        if (complianceScore >= 90) return 'Excellent';
        if (complianceScore >= 80) return 'Good';
        if (complianceScore >= 70) return 'Fair';
        return 'Needs Attention';
    }

    getComplianceColor(): string {
        const status = this.getComplianceStatus();
        switch (status) {
            case 'Excellent': return 'accent';
            case 'Good': return 'primary';
            case 'Fair': return 'warn';
            default: return 'warn';
        }
    }

    getRiskLevel(): string {
        if (!this.analytics) return 'Unknown';

        const riskScore = this.analytics.riskScore;
        if (riskScore <= 20) return 'Low';
        if (riskScore <= 40) return 'Medium';
        if (riskScore <= 60) return 'High';
        return 'Critical';
    }

    getRiskColor(): string {
        const level = this.getRiskLevel();
        switch (level) {
            case 'Low': return 'accent';
            case 'Medium': return 'primary';
            case 'High': return 'warn';
            default: return 'warn';
        }
    }

    formatDate(date: string): string {
        return new Date(date).toLocaleDateString();
    }

    formatTime(date: string): string {
        return new Date(date).toLocaleTimeString();
    }

    getActionTypeIcon(actionType: string): string {
        switch (actionType.toLowerCase()) {
            case 'read': return 'visibility';
            case 'update': return 'edit';
            case 'delete': return 'delete';
            case 'export': return 'download';
            case 'import': return 'upload';
            case 'consent': return 'check_circle';
            case 'withdraw': return 'cancel';
            default: return 'info';
        }
    }

    getActionTypeColor(actionType: string): string {
        switch (actionType.toLowerCase()) {
            case 'read': return 'primary';
            case 'update': return 'accent';
            case 'delete': return 'warn';
            case 'export': return 'primary';
            case 'import': return 'accent';
            case 'consent': return 'accent';
            case 'withdraw': return 'warn';
            default: return 'primary';
        }
    }

    onGenerateComplianceReport(): void {
        this.privacyAnalyticsService.generateComplianceReport().subscribe({
            next: () => {
                this.snackBar.open('Compliance report generated successfully', 'Close', { duration: 3000 });
                // Handle report download or display
            },
            error: (error) => {
                console.error('Error generating compliance report:', error);
                this.snackBar.open('Failed to generate compliance report', 'Close', { duration: 3000 });
            }
        });
    }

    onExportAnalytics(): void {
        this.privacyAnalyticsService.exportAnalytics().subscribe({
            next: (data) => {
                const blob = new Blob([data], { type: 'application/json' });
                const url = window.URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = 'privacy-analytics.json';
                link.click();
                window.URL.revokeObjectURL(url);
                this.snackBar.open('Analytics exported successfully', 'Close', { duration: 3000 });
            },
            error: (error) => {
                console.error('Error exporting analytics:', error);
                this.snackBar.open('Failed to export analytics', 'Close', { duration: 3000 });
            }
        });
    }
} 