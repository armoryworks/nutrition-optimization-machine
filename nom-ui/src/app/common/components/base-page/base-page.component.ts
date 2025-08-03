import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

export interface BasePageConfig {
    title: string;
    subtitle?: string;
    showBackButton?: boolean;
    backButtonText?: string;
    backButtonRoute?: string[];
    showRefreshButton?: boolean;
    refreshButtonText?: string;
    fullCanvas?: boolean;
    maxWidth?: string;
}

@Component({
    selector: 'nom-base-page',
    standalone: true,
    imports: [
        CommonModule,
        MatProgressSpinnerModule,
        MatIconModule,
        MatButtonModule,
    ],
    templateUrl: './base-page.component.html',
    styleUrls: ['./base-page.component.scss']
})
export class BasePageComponent {
    @Input() config?: BasePageConfig;
    @Input() isLoading = false;
    @Input() error: string | null = null;
    @Input() loadingMessage = 'Loading...';
    @Input() errorTitle = 'Error';
    @Input() showRetryButton = true;
    @Input() retryButtonText = 'Try Again';

    onBack(): void {
        // This will be overridden by parent components
    }

    onRefresh(): void {
        // This will be overridden by parent components
    }

    onRetry(): void {
        // This will be overridden by parent components
    }
} 