import { Component, Input, Output, EventEmitter } from '@angular/core';

import { AmwButtonComponent, AmwCardComponent, AmwIconButtonComponent, AmwIconComponent, AmwMenuComponent, AmwMenuItemComponent, AmwMenuTriggerForDirective } from 'angular-material-wrap';

export interface DetailAction {
    label: string;
    icon: string;
    color?: 'primary' | 'accent' | 'warn';
    action: () => void;
}

export interface BaseDetailConfig {
    title: string;
    subtitle?: string;
    showBackButton?: boolean;
    backButtonText?: string;
    showEditButton?: boolean;
    editButtonText?: string;
    actions?: DetailAction[];
    maxWidth?: string;
}

@Component({
    selector: 'nom-base-detail',
    standalone: true,
    imports: [
    AmwButtonComponent,
    AmwCardComponent,
    AmwIconButtonComponent,
    AmwIconComponent,
    AmwMenuComponent,
    AmwMenuItemComponent,
    AmwMenuTriggerForDirective,
],
    templateUrl: './base-detail.component.html',
    styleUrls: ['./base-detail.component.scss']
})
export class BaseDetailComponent {
    @Input() config?: BaseDetailConfig;
    @Input() loading = false;
    @Input() error: string | null = null;

    @Output() back = new EventEmitter<void>();
    @Output() edit = new EventEmitter<void>();

    onBack(): void {
        this.back.emit();
    }

    onEdit(): void {
        this.edit.emit();
    }
} 