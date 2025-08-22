import { Component, Input, Output, EventEmitter, OnInit, OnChanges, SimpleChanges } from '@angular/core';
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
export class BasePageComponent implements OnInit, OnChanges {
    @Input() config?: BasePageConfig;
    @Input() backButtonText?: string; // Separate input for back button text
    @Input() isLoading = false;
    @Input() error: string | null = null;
    @Input() loadingMessage = 'Loading...';
    @Input() errorTitle = 'Error';
    @Input() showRetryButton = true;
    @Input() retryButtonText = 'Try Again';

    @Output() back = new EventEmitter<void>();
    @Output() refresh = new EventEmitter<void>();
    @Output() retry = new EventEmitter<void>();

    ngOnInit(): void {
        console.log('BasePageComponent - ngOnInit - backButtonText:', this.backButtonText);
        console.log('BasePageComponent - ngOnInit - config:', this.config);
    }

    ngOnChanges(changes: SimpleChanges): void {
        console.log('BasePageComponent - ngOnChanges:', changes);
        if (changes['backButtonText']) {
            console.log('BasePageComponent - backButtonText changed:', {
                previousValue: changes['backButtonText'].previousValue,
                currentValue: changes['backButtonText'].currentValue
            });
        }
    }

    onBack(): void {
        console.log('BasePageComponent - onBack called, emitting back event');
        this.back.emit();
    }

    onRefresh(): void {
        console.log('BasePageComponent - onRefresh called, emitting refresh event');
        this.refresh.emit();
    }

    onRetry(): void {
        console.log('BasePageComponent - onRetry called, emitting retry event');
        this.retry.emit();
    }
} 