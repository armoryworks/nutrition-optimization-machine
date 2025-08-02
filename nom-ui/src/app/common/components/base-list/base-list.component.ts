import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';

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
}

@Component({
    selector: 'app-base-list',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatButtonModule,
        MatIconModule,
        MatFormFieldModule,
        MatInputModule,
        MatProgressSpinnerModule,
        MatChipsModule,
        MatDividerModule,
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
} 