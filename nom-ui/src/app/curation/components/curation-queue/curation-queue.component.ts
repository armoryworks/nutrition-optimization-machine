// File: nom-ui/src/app/curation/components/curation-queue/curation-queue.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { CurationService } from '../../services/curation.service';
import { CurationQueueItemModel } from '../../models/curation-queue-item.model';
import { CurationDecisionRequestModel } from '../../models/curation-decision-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
  selector: 'app-curation-queue',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    MatSnackBarModule
  ],
  templateUrl: './curation-queue.component.html',
  styleUrls: ['./curation-queue.component.scss']
})
export class CurationQueueComponent implements OnInit {
  queueItems: CurationQueueItemModel[] = [];
  isLoading = true;
  selectedItem: CurationQueueItemModel | null = null;
  decisionForm: FormGroup;
  isSubmitting = false;

  // Computed properties for template filtering
  get recipeCount(): number {
    return this.queueItems.filter(item => item.entityType === 'Recipe').length;
  }

  get ingredientCount(): number {
    return this.queueItems.filter(item => item.entityType === 'Ingredient').length;
  }

  constructor(
    private curationService: CurationService,
    private notificationService: NotificationService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog,
    private fb: FormBuilder
  ) {
    this.decisionForm = this.fb.group({
      decisionNotes: ['', [Validators.required, Validators.minLength(10)]],
      publicNotes: ['', [Validators.maxLength(500)]]
    });
  }

  ngOnInit(): void {
    console.log('CurationQueueComponent initialized');
    console.log('Component template loaded successfully');

    // Add a simple test to verify the component is working
    setTimeout(() => {
      console.log('Component is fully loaded and ready');
    }, 100);

    this.loadCurationQueue();
  }

  loadCurationQueue(): void {
    console.log('Loading curation queue...');
    this.isLoading = true;
    this.curationService.getCurationQueue().subscribe({
      next: (items) => {
        console.log('Curation queue loaded:', items);
        this.queueItems = items;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading curation queue:', error);
        console.error('Error details:', {
          status: error.status,
          statusText: error.statusText,
          message: error.message,
          error: error.error
        });

        if (error.status === 403) {
          this.notificationService.error('Access denied. You do not have permission to view the curation queue.');
        } else if (error.status === 401) {
          this.notificationService.error('Authentication required. Please log in again.');
        } else {
          this.notificationService.error('Failed to load curation queue: ' + (error.error?.message || error.message || 'Unknown error'));
        }

        this.isLoading = false;
      }
    });
  }

  selectItem(item: CurationQueueItemModel): void {
    this.selectedItem = item;
    this.decisionForm.reset();
  }

  approve(): void {
    if (!this.selectedItem || this.decisionForm.invalid) return;

    this.isSubmitting = true;
    const request: CurationDecisionRequestModel = {
      entityId: this.selectedItem.id,
      entityType: this.selectedItem.entityType,
      decisionNotes: this.decisionForm.get('decisionNotes')?.value
    };

    this.curationService.approve(request).subscribe({
      next: () => {
        this.notificationService.success('Item approved successfully');
        this.selectedItem = null;
        this.decisionForm.reset();
        this.loadCurationQueue();
      },
      error: (error) => {
        console.error('Error approving item:', error);
        this.notificationService.error('Failed to approve item');
        this.isSubmitting = false;
      }
    });
  }

  requestRevision(): void {
    if (!this.selectedItem || this.decisionForm.invalid) return;

    this.isSubmitting = true;
    const request: CurationDecisionRequestModel = {
      entityId: this.selectedItem.id,
      entityType: this.selectedItem.entityType,
      decisionNotes: this.decisionForm.get('decisionNotes')?.value
    };

    this.curationService.requestRevision(request).subscribe({
      next: () => {
        this.notificationService.success('Revision requested successfully');
        this.selectedItem = null;
        this.decisionForm.reset();
        this.loadCurationQueue();
      },
      error: (error) => {
        console.error('Error requesting revision:', error);
        this.notificationService.error('Failed to request revision');
        this.isSubmitting = false;
      }
    });
  }

  reject(): void {
    if (!this.selectedItem || this.decisionForm.invalid) return;

    this.isSubmitting = true;
    const request: CurationDecisionRequestModel = {
      entityId: this.selectedItem.id,
      entityType: this.selectedItem.entityType,
      decisionNotes: this.decisionForm.get('decisionNotes')?.value
    };

    this.curationService.reject(request).subscribe({
      next: () => {
        this.notificationService.success('Item rejected successfully');
        this.selectedItem = null;
        this.decisionForm.reset();
        this.loadCurationQueue();
      },
      error: (error) => {
        console.error('Error rejecting item:', error);
        this.notificationService.error('Failed to reject item');
        this.isSubmitting = false;
      }
    });
  }

  cancel(): void {
    this.selectedItem = null;
    this.decisionForm.reset();
  }

  getItemTypeColor(entityType: string): string {
    return entityType === 'Recipe' ? 'primary' : 'accent';
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  truncateText(text: string, maxLength: number = 100): string {
    if (!text) return '';
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
  }
}