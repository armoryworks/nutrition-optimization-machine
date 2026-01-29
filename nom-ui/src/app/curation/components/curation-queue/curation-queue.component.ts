// File: nom-ui/src/app/curation/components/curation-queue/curation-queue.component.ts

import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { FormsModule, ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';

import { Subject, takeUntil, finalize } from 'rxjs';

import { AmwButtonComponent, AmwCardComponent, AmwTextareaComponent, AmwIconButtonComponent, AmwTooltipDirective, AmwIconComponent, AmwMenuComponent, AmwMenuItemComponent, AmwMenuTriggerForDirective, AmwAccordionComponent, AmwAccordionPanelComponent, AmwInlineLoadingComponent } from 'angular-material-wrap';

import { CurationService } from '../../services/curation.service';
import { CurationQueueItemModel } from '../../models/curation-queue-item.model';
import { CurationDecisionRequestModel } from '../../models/curation-decision-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-curation-queue',
  standalone: true,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    AmwButtonComponent,
    AmwCardComponent,
    AmwTextareaComponent,
    AmwIconButtonComponent,
    AmwTooltipDirective,
    AmwIconComponent,
    AmwMenuComponent,
    AmwMenuItemComponent,
    AmwMenuTriggerForDirective,
    AmwAccordionComponent,
    AmwAccordionPanelComponent,
    AmwInlineLoadingComponent,
  ],
  templateUrl: './curation-queue.component.html',
  styleUrls: ['./curation-queue.component.scss']
})
export class CurationQueueComponent implements OnInit, OnDestroy {
  private curationService = inject(CurationService);
  private notificationService = inject(NotificationService);
  private fb = inject(NonNullableFormBuilder);

  queueItems = signal<CurationQueueItemModel[]>([]);
  isLoading = signal(true);
  selectedItem = signal<CurationQueueItemModel | null>(null);
  decisionForm: FormGroup;
  isSubmitting = signal(false);
  error = signal<string | null>(null);
  lastRefreshTime = signal<Date | null>(null);

  private destroy$ = new Subject<void>();

  // Computed properties for template filtering
  recipeCount = computed(() => {
    return this.queueItems().filter(item => item.entityType === 'Recipe').length;
  });

  ingredientCount = computed(() => {
    return this.queueItems().filter(item => item.entityType === 'Ingredient').length;
  });

  hasItems = computed(() => {
    return this.queueItems().length > 0;
  });

  selectedItemIndex = computed(() => {
    if (!this.selectedItem()) return -1;
    return this.queueItems().findIndex(item => item.id === this.selectedItem()!.id);
  });

  progressText = computed(() => {
    if (this.hasItems() && this.selectedItem()) {
      return `Reviewing item ${this.selectedItemIndex() + 1} of ${this.queueItems().length}`;
    } else if (this.hasItems()) {
      return 'Select an item to review';
    } else {
      return 'No items to review';
    }
  });

  constructor() {
    this.decisionForm = this.fb.group({
      decisionNotes: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(1000)]],
      publicNotes: ['', [Validators.maxLength(500)]]
    });
  }

  ngOnInit(): void {
    console.log('CurationQueueComponent initialized');
    this.loadCurationQueue();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadCurationQueue(): void {
    console.log('Loading curation queue...');
    this.isLoading.set(true);
    this.error.set(null);

    this.curationService.getCurationQueue().pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.isLoading.set(false);
        this.lastRefreshTime.set(new Date());
      })
    ).subscribe({
      next: (items) => {
        console.log('Curation queue loaded:', items);
        this.queueItems.set(items);

        // If the currently selected item is no longer in the queue, clear selection
        if (this.selectedItem() && !items.find(item => item.id === this.selectedItem()!.id)) {
          this.selectedItem.set(null);
          this.decisionForm.reset();
        }
      },
      error: (error: unknown) => {
        console.error('Error loading curation queue:', error);
        this.error.set(ERROR_MESSAGES.CURATION.LOAD_FAILED);
        this.notificationService.error(ERROR_MESSAGES.CURATION.LOAD_FAILED);
      }
    });
  }

  onRefresh(): void {
    this.loadCurationQueue();
  }

  onRetry(): void {
    this.error.set(null);
    this.loadCurationQueue();
  }

  selectItem(item: CurationQueueItemModel): void {
    if (this.selectedItem()?.id === item.id) {
      // If clicking the same item, deselect it
      this.selectedItem.set(null);
      this.decisionForm.reset();
    } else {
      this.selectedItem.set(item);
      this.decisionForm.reset();
    }
  }

  selectNextItem(): void {
    if (this.selectedItemIndex() >= 0 && this.selectedItemIndex() < this.queueItems().length - 1) {
      this.selectItem(this.queueItems()[this.selectedItemIndex() + 1]);
    }
  }

  selectPreviousItem(): void {
    if (this.selectedItemIndex() > 0) {
      this.selectItem(this.queueItems()[this.selectedItemIndex() - 1]);
    }
  }

  approve(): void {
    if (!this.selectedItem() || this.decisionForm.invalid || this.isSubmitting()) {
      return;
    }

    this.submitDecision('approve', () => {
      this.notificationService.success('Item approved successfully');
      this.removeItemFromQueue();
    });
  }

  requestRevision(): void {
    if (!this.selectedItem() || this.decisionForm.invalid || this.isSubmitting()) {
      return;
    }

    this.submitDecision('revision', () => {
      this.notificationService.success('Revision requested successfully');
      this.removeItemFromQueue();
    });
  }

  reject(): void {
    if (!this.selectedItem() || this.decisionForm.invalid || this.isSubmitting()) {
      return;
    }

    this.submitDecision('reject', () => {
      this.notificationService.success('Item rejected successfully');
      this.removeItemFromQueue();
    });
  }

  private submitDecision(action: 'approve' | 'revision' | 'reject', onSuccess: () => void): void {
    this.isSubmitting.set(true);
    this.error.set(null);

    const decision: CurationDecisionRequestModel = {
      entityId: this.selectedItem()!.id,
      entityType: this.selectedItem()!.entityType,
      decisionNotes: this.decisionForm.get('decisionNotes')?.value
    };

    const serviceCall = action === 'approve'
      ? this.curationService.approve(decision)
      : action === 'revision'
        ? this.curationService.requestRevision(decision)
        : this.curationService.reject(decision);

    serviceCall.pipe(
      takeUntil(this.destroy$),
      finalize(() => {
        this.isSubmitting.set(false);
      })
    ).subscribe({
      next: () => {
        onSuccess();
      },
      error: (error: unknown) => {
        console.error(`Error ${action}ing item:`, error);
        const errorMessage = action === 'approve' ? ERROR_MESSAGES.CURATION.APPROVE_FAILED
          : action === 'reject' ? ERROR_MESSAGES.CURATION.REJECT_FAILED
          : ERROR_MESSAGES.CURATION.REVISION_FAILED;
        this.error.set(errorMessage);
        this.notificationService.error(errorMessage);
      }
    });
  }

  private removeItemFromQueue(): void {
    this.queueItems.set(this.queueItems().filter(item => item.id !== this.selectedItem()!.id));
    this.selectedItem.set(null);
    this.decisionForm.reset();
  }

  cancel(): void {
    this.selectedItem.set(null);
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

  formatRelativeTime(date: Date): string {
    const now = new Date();
    const diffInMs = now.getTime() - new Date(date).getTime();
    const diffInHours = diffInMs / (1000 * 60 * 60);
    const diffInDays = diffInHours / 24;

    if (diffInDays >= 1) {
      return `${Math.floor(diffInDays)} day${Math.floor(diffInDays) === 1 ? '' : 's'} ago`;
    } else if (diffInHours >= 1) {
      return `${Math.floor(diffInHours)} hour${Math.floor(diffInHours) === 1 ? '' : 's'} ago`;
    } else {
      return 'Just now';
    }
  }

  truncateText(text: string, maxLength = 100): string {
    if (!text) return '';
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
  }

  parseIngredients(ingredientsString: string | undefined): { name: string, quantity: string, unit: string }[] {
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

  // Keyboard navigation
  onKeyDown(event: KeyboardEvent, item: CurationQueueItemModel): void {
    switch (event.key) {
      case 'Enter':
      case ' ':
        event.preventDefault();
        this.selectItem(item);
        break;
      case 'ArrowDown':
        event.preventDefault();
        this.selectNextItem();
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.selectPreviousItem();
        break;
      case 'Escape':
        event.preventDefault();
        this.cancel();
        break;
    }
  }

  // Utility method to check if form has unsaved changes
  hasUnsavedChanges = computed(() => {
    return this.decisionForm.dirty && this.decisionForm.valid;
  });

  // Method to handle beforeunload event
  onBeforeUnload(): boolean {
    if (this.hasUnsavedChanges()) {
      return true; // Will show browser warning
    }
    return false;
  }
}