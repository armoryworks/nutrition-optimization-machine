import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { WebhookService } from '../core/services/webhook.service';
import { WebhookResponse } from '../core/models/webhook-response.model';
import { HouseholdStore } from '../core/services/household-store';
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
  templateUrl: './webhooks.component.html',
  styleUrl: './webhooks.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Webhooks implements OnInit {
  private fb = inject(FormBuilder);
  private webhookService = inject(WebhookService);
  private householdStore = inject(HouseholdStore);
  private loadingService = inject(LoadingService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private destroyRef = inject(DestroyRef);

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
    this.householdStore.getHouseholds().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
      this.loadingService.loading('Loading webhooks...'),
      takeUntilDestroyed(this.destroyRef),
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
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
    this.webhookService.testWebhook(wh.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
