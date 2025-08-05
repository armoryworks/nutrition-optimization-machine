import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';

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
        CommonModule,
        MatCardModule,
        MatButtonModule,
        MatIconModule,
        MatMenuModule,
        MatDividerModule,
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