import { Component, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { WebhookService, WebhookResponse } from '../core/services/webhook.service';
import { HouseholdService } from '../core/services/household.service';
import { LoadingService } from '../core/services/loading.service';
import { ConfirmDeleteDialog, ConfirmDeleteDialogData } from '../shared/confirm-delete-dialog/confirm-delete-dialog.component';

@Component({
  selector: 'nom-webhooks',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSnackBarModule,
  ],
  template: `
    <div class="nom-form--full">
      <div class="nom-form__header">
        <h1 class="nom-form__title">Webhooks</h1>
        <p class="nom-form__subtitle">Get notified when events happen in your household</p>
      </div>

      @if (errorMessage()) {
        <div class="nom-form__error">
          <mat-icon>error_outline</mat-icon>
          <span>{{ errorMessage() }}</span>
        </div>
      }

      <!-- Existing webhooks -->
      <div class="nom-form__section">
        <h2 class="nom-form__section-title">Active Webhooks</h2>

        @if (loading()) {
          <div class="nom-webhooks__loading">
            <mat-spinner diameter="32"></mat-spinner>
          </div>
        } @else if (webhooks().length === 0) {
          <p class="nom-webhooks__empty">No webhooks configured. Add one below to receive event notifications.</p>
        } @else {
          @for (wh of webhooks(); track wh.id) {
            <div class="nom-webhooks__item" [class.nom-webhooks__item--inactive]="!wh.isActive">
              <div class="nom-webhooks__item-info">
                <span class="nom-webhooks__item-name">{{ wh.name }}</span>
                <span class="nom-webhooks__item-url">{{ wh.url }}</span>
                <span class="nom-webhooks__item-event">Event: {{ wh.eventType }}</span>
              </div>
              <div class="nom-webhooks__item-actions">
                <button mat-icon-button (click)="testWebhook(wh)" [disabled]="testing() === wh.id" matTooltip="Test webhook">
                  @if (testing() === wh.id) {
                    <mat-spinner diameter="20"></mat-spinner>
                  } @else {
                    <mat-icon>send</mat-icon>
                  }
                </button>
                <button mat-icon-button (click)="deleteWebhook(wh)" matTooltip="Delete webhook">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            </div>
          }
        }
      </div>

      <!-- Add webhook form -->
      <div class="nom-form__section">
        <h2 class="nom-form__section-title">Add Webhook</h2>
        <form [formGroup]="webhookForm" (ngSubmit)="onAddWebhook()">
          <div class="nom-form__fields">
            <mat-form-field appearance="outline">
              <mat-label>Name</mat-label>
              <input matInput formControlName="name" placeholder="e.g. Meal Plan Updated" />
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>URL</mat-label>
              <input matInput formControlName="url" placeholder="https://example.com/webhook" type="url" />
              <mat-hint>The endpoint to receive POST requests</mat-hint>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Event Type</mat-label>
              <mat-select formControlName="eventType">
                <mat-option value="meal_plan.updated">Meal Plan Updated</mat-option>
                <mat-option value="meal_plan.created">Meal Plan Created</mat-option>
                <mat-option value="shopping_list.generated">Shopping List Generated</mat-option>
                <mat-option value="recipe.created">Recipe Created</mat-option>
                <mat-option value="recipe.updated">Recipe Updated</mat-option>
                <mat-option value="pantry.low_stock">Pantry Low Stock</mat-option>
                <mat-option value="household.member_joined">Member Joined</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-slide-toggle formControlName="isActive">Active</mat-slide-toggle>
          </div>

          <div class="nom-form__actions">
            <a mat-button routerLink="/admin">Back to Admin</a>
            <button mat-flat-button type="submit" [disabled]="webhookForm.invalid || saving()">
              @if (saving()) {
                <mat-spinner diameter="20"></mat-spinner>
              } @else {
                Add Webhook
              }
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    @use 'variables' as vars;

    .nom-webhooks__loading {
      display: flex;
      justify-content: center;
      padding: vars.$spacing-8;
    }

    .nom-webhooks__empty {
      color: var(--mat-sys-on-surface-variant);
      font-size: vars.$font-size-sm;
      padding: vars.$spacing-4 0;
    }

    .nom-webhooks__item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: vars.$spacing-3 vars.$spacing-4;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: vars.$nom-border-radius;
      margin-bottom: vars.$spacing-2;
      background: var(--mat-sys-surface-container-low);

      &--inactive {
        opacity: 0.6;
      }
    }

    .nom-webhooks__item-info {
      display: flex;
      flex-direction: column;
      gap: vars.$spacing-1;
      min-width: 0;
      flex: 1;
    }

    .nom-webhooks__item-name {
      font-weight: vars.$font-weight-medium;
      color: var(--mat-sys-on-surface);
    }

    .nom-webhooks__item-url {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-primary);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nom-webhooks__item-event {
      font-size: vars.$font-size-xs;
      color: var(--mat-sys-on-surface-variant);
    }

    .nom-webhooks__item-actions {
      display: flex;
      gap: vars.$spacing-1;
      flex-shrink: 0;
    }
  `],
})
export class Webhooks implements OnInit {
  private fb = inject(FormBuilder);
  private webhookService = inject(WebhookService);
  private householdService = inject(HouseholdService);
  private loadingService = inject(LoadingService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  webhooks = signal<WebhookResponse[]>([]);
  loading = signal(true);
  saving = signal(false);
  testing = signal<number | null>(null);
  errorMessage = signal('');
  householdId = signal(0);

  webhookForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    url: ['', [Validators.required, Validators.maxLength(2047)]],
    eventType: ['', Validators.required],
    isActive: [true],
  });

  ngOnInit(): void {
    this.householdService.getHouseholds().subscribe({
      next: (list) => {
        if (list.length > 0) {
          this.householdId.set(list[0].id);
          this.loadWebhooks();
        } else {
          this.loading.set(false);
          this.errorMessage.set('Create a household first to manage webhooks.');
        }
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load household.');
      },
    });
  }

  private loadWebhooks(): void {
    this.loading.set(true);
    this.webhookService.getWebhooks(this.householdId()).pipe(
      this.loadingService.loading('Loading webhooks...')
    ).subscribe({
      next: (webhooks) => {
        this.webhooks.set(webhooks);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load webhooks.');
      },
    });
  }

  onAddWebhook(): void {
    if (this.webhookForm.invalid || this.saving()) return;
    this.saving.set(true);
    this.errorMessage.set('');

    const form = this.webhookForm.getRawValue();
    this.webhookService.createWebhook({
      householdId: this.householdId(),
      name: form.name!,
      url: form.url!,
      eventType: form.eventType!,
      isActive: form.isActive ?? true,
    }).subscribe({
      next: () => {
        this.webhookForm.reset({ isActive: true });
        this.saving.set(false);
        this.loadWebhooks();
      },
      error: () => {
        this.saving.set(false);
        this.errorMessage.set('Failed to create webhook.');
      },
    });
  }

  testWebhook(wh: WebhookResponse): void {
    this.testing.set(wh.id);
    this.webhookService.testWebhook(wh.id).subscribe({
      next: () => {
        this.snackBar.open('Test payload sent successfully', 'OK', { duration: 3000 });
        this.testing.set(null);
      },
      error: () => {
        this.snackBar.open('Test failed — check the URL', 'OK', { duration: 4000 });
        this.testing.set(null);
      },
    });
  }

  deleteWebhook(wh: WebhookResponse): void {
    this.dialog.open(ConfirmDeleteDialog, {
      data: {
        title: 'Delete Webhook',
        message: `Delete webhook "${wh.name}"? This cannot be undone.`,
        confirmText: 'Delete',
      } as ConfirmDeleteDialogData,
    }).afterClosed().subscribe(confirmed => {
      if (!confirmed) return;
      this.webhookService.deleteWebhook(wh.id).subscribe({
        next: () => this.webhooks.update(list => list.filter(w => w.id !== wh.id)),
        error: () => this.errorMessage.set('Failed to delete webhook.'),
      });
    });
  }
}
