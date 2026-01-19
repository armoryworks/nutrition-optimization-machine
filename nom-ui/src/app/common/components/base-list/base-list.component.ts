import { Component, Input, Output, EventEmitter } from '@angular/core';

import { ReactiveFormsModule, FormControl } from '@angular/forms';

import { AmwButtonComponent, AmwInputComponent, AmwIconComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

export interface ListAction {
    label: string;
    icon: string;
    color?: 'primary' | 'accent' | 'warn';
    action: () => void;
}

export interface BaseListConfig {
    title: string;
    subtitle?: string;
    showCreateButton?: boolean;
    createButtonText?: string;
    createButtonIcon?: string;
    showSearch?: boolean;
    searchPlaceholder?: string;
    showRefreshButton?: boolean;
    refreshButtonText?: string;
    actions?: ListAction[];
    maxWidth?: string;

    // Extended functionality for specialized lists
    showStats?: boolean;
    stats?: Array<{
        label: string;
        value: number;
        type: 'pending' | 'recipe' | 'ingredient' | 'plan' | 'custom';
        color?: string;
    }>;
    showProgress?: boolean;
    progressText?: string;
    progressValue?: number;
    progressTotal?: number;
    showCustomActions?: boolean;
    customActions?: Array<{
        label: string;
        icon: string;
        color: 'primary' | 'accent' | 'warn';
        disabled?: boolean;
        action: () => void;
    }>;
    showLastUpdated?: boolean;
    lastUpdated?: Date;

    // Control buttons for curation interfaces
    showControlButtons?: boolean;
    controlButtons?: Array<{
        label: string;
        icon: string;
        color: 'primary' | 'accent' | 'warn';
        disabled?: boolean;
        action: () => void;
    }>;
}

@Component({
    selector: 'nom-base-list',
    standalone: true,
    imports: [
    ReactiveFormsModule,
    AmwButtonComponent,
    AmwInputComponent,
    AmwIconComponent,
    AmwProgressSpinnerComponent,
],
    templateUrl: './base-list.component.html',
    styleUrls: ['./base-list.component.scss']
})
export class BaseListComponent {
    @Input() config?: BaseListConfig;
    @Input() isLoading = false;
    @Input() error: string | null = null;
    @Input() isEmpty = false;
    @Input() loadingMessage = 'Loading...';
    @Input() errorTitle = 'Error';
    @Input() emptyTitle = 'No Items';
    @Input() emptyMessage = 'No items found.';
    @Input() showRetryButton = true;
    @Input() retryButtonText = 'Try Again';
    @Input() searchControl = new FormControl('');

    @Output() create = new EventEmitter<void>();
    @Output() refresh = new EventEmitter<void>();
    @Output() retry = new EventEmitter<void>();

    onCreate(): void {
        this.create.emit();
    }

    onRefresh(): void {
        this.refresh.emit();
    }

    onRetry(): void {
        this.retry.emit();
    }

    formatRelativeTime(date: Date): string {
        const now = new Date();
        const diffInSeconds = Math.floor((now.getTime() - date.getTime()) / 1000);

        if (diffInSeconds < 60) {
            return 'Just now';
        } else if (diffInSeconds < 3600) {
            const minutes = Math.floor(diffInSeconds / 60);
            return `${minutes} minute${minutes > 1 ? 's' : ''} ago`;
        } else if (diffInSeconds < 86400) {
            const hours = Math.floor(diffInSeconds / 3600);
            return `${hours} hour${hours > 1 ? 's' : ''} ago`;
        } else {
            const days = Math.floor(diffInSeconds / 86400);
            return `${days} day${days > 1 ? 's' : ''} ago`;
        }
    }
} 