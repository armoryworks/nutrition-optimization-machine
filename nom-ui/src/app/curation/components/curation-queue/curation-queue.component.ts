// File: nom-ui/src/app/curation/components/curation-queue/curation-queue.component.ts

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
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
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject, takeUntil } from 'rxjs';
import { CurationService } from '../../services/curation.service';
import { CurationQueueItemModel } from '../../models/curation-queue-item.model';
import { CurationDecisionRequestModel } from '../../models/curation-decision-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { BaseListComponent, BaseListConfig } from '../../../common/components/base-list/base-list.component';
// Using inline interfaces instead of missing models
interface CurationFeedbackModel {
  id?: number;
  feedback: string;
  rating?: number;
}
interface CurationFeedbackCreateRequestModel {
  feedback: string;
  rating?: number;
}
interface CurationFeedbackResponseModel extends CurationFeedbackModel {
  id: number;
  createdAt: Date;
}
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'nom-curation-queue',
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
    MatSnackBarModule,
    MatTooltipModule,
    BaseListComponent
  ],
  templateUrl: './curation-queue.component.html',
  styleUrls: ['./curation-queue.component.scss']
})
export class CurationQueueComponent implements OnInit, OnDestroy {
  queueItems: CurationQueueItemModel[] = [];
  isLoading = true;
  selectedItem: CurationQueueItemModel | null = null;
  decisionForm: FormGroup;
  isSubmitting = false;
  error: string | null = null;

  listConfig: BaseListConfig = {
    title: 'Curation Queue',
    subtitle: 'Review and approve submitted content',
    showSearch: false,
    showRefreshButton: true,
    refreshButtonText: 'Refresh',
    maxWidth: 'none'
  };

  private destroy$ = new Subject<void>();

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
    private fb: NonNullableFormBuilder
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

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadCurationQueue(): void {
    console.log('Loading curation queue...');
    this.isLoading = true;
    this.error = null;

    this.curationService.getCurationQueue().pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: (items) => {
        console.log('Curation queue loaded:', items);
        this.queueItems = items;
        this.isLoading = false;
      },
      error: (error: any) => {
        console.error('Error loading curation queue:', error);
        this.error = 'Failed to load curation queue. Please try again.';
        this.isLoading = false;
        this.notificationService.error('Failed to load curation queue');
      }
    });
  }

  onRefresh(): void {
    this.loadCurationQueue();
  }

  onRetry(): void {
    this.error = null;
    this.loadCurationQueue();
  }

  selectItem(item: CurationQueueItemModel): void {
    this.selectedItem = item;
    this.decisionForm.reset();
  }

  approve(): void {
    if (!this.selectedItem || this.decisionForm.invalid || this.isSubmitting) {
      return;
    }

    this.isSubmitting = true;
    this.error = null;

    const decision: CurationDecisionRequestModel = {
      entityId: this.selectedItem.id,
      entityType: this.selectedItem.entityType,
      decisionNotes: this.decisionForm.get('decisionNotes')?.value
    };

    this.curationService.approve(decision).pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: () => {
        this.notificationService.success('Item approved successfully');
        this.queueItems = this.queueItems.filter(item => item.id !== this.selectedItem!.id);
        this.selectedItem = null;
        this.decisionForm.reset();
        this.isSubmitting = false;
      },
      error: (error: any) => {
        console.error('Error approving item:', error);
        this.error = 'Failed to approve item. Please try again.';
        this.isSubmitting = false;
      }
    });
  }

  requestRevision(): void {
    if (!this.selectedItem || this.decisionForm.invalid || this.isSubmitting) {
      return;
    }

    this.isSubmitting = true;
    this.error = null;

    const decision: CurationDecisionRequestModel = {
      entityId: this.selectedItem.id,
      entityType: this.selectedItem.entityType,
      decisionNotes: this.decisionForm.get('decisionNotes')?.value
    };

    this.curationService.requestRevision(decision).pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: () => {
        this.notificationService.success('Revision requested successfully');
        this.queueItems = this.queueItems.filter(item => item.id !== this.selectedItem!.id);
        this.selectedItem = null;
        this.decisionForm.reset();
        this.isSubmitting = false;
      },
      error: (error: any) => {
        console.error('Error requesting revision:', error);
        this.error = 'Failed to request revision. Please try again.';
        this.isSubmitting = false;
      }
    });
  }

  reject(): void {
    if (!this.selectedItem || this.decisionForm.invalid || this.isSubmitting) {
      return;
    }

    this.isSubmitting = true;
    this.error = null;

    const decision: CurationDecisionRequestModel = {
      entityId: this.selectedItem.id,
      entityType: this.selectedItem.entityType,
      decisionNotes: this.decisionForm.get('decisionNotes')?.value
    };

    this.curationService.reject(decision).pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: () => {
        this.notificationService.success('Item rejected successfully');
        this.queueItems = this.queueItems.filter(item => item.id !== this.selectedItem!.id);
        this.selectedItem = null;
        this.decisionForm.reset();
        this.isSubmitting = false;
      },
      error: (error: any) => {
        console.error('Error rejecting item:', error);
        this.error = 'Failed to reject item. Please try again.';
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
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
  }

  parseIngredients(ingredientsString: string): Array<{ name: string, quantity: string, unit: string }> {
    if (!ingredientsString) return [];

    try {
      // Parse the ingredients string - this is a simplified parser
      // In a real implementation, you'd want more robust parsing
      const lines = ingredientsString.split('\n').filter(line => line.trim());
      return lines.map(line => {
        const parts = line.split(' ');
        if (parts.length >= 3) {
          const quantity = parts[0];
          const unit = parts[1];
          const name = parts.slice(2).join(' ');
          return { name, quantity, unit };
        } else {
          return { name: line, quantity: '', unit: '' };
        }
      });
    } catch (error) {
      console.error('Error parsing ingredients:', error);
      return [];
    }
  }

  getRecipeSteps(item: CurationQueueItemModel): string[] {
    if (!item.instructions) return [];

    try {
      // Parse the instructions string
      const lines = item.instructions.split('\n').filter(line => line.trim());
      return lines.map(line => line.trim());
    } catch (error) {
      console.error('Error parsing recipe steps:', error);
      return [];
    }
  }
}