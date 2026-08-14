import { Component, DestroyRef, ChangeDetectionStrategy, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { GroceryExportService } from '../core/services/grocery-export.service';
import {
  GroceryExportFormat,
  GroceryExportResult,
  GroceryProviderInfo,
  GroceryStore,
} from '../core/models/grocery-export.model';

export interface GroceryExportDialogData {
  /** Destinations the API reported — never empty; the caller hides the entry point otherwise. */
  providers: GroceryProviderInfo[];
  /** The saved list being sent. Null when the household has none yet. */
  shoppingListId: number | null;
  listName: string;
  /** App path the retailer consent flow returns to, e.g. `/shopping`. */
  returnUrl: string;
}

type DialogStep = 'pick' | 'result' | 'store';

/**
 * "Send to…" — picks a destination and runs the export. Every provider label
 * comes from the API; nothing here knows which retailers exist.
 */
@Component({
  selector: 'nom-grocery-export-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTooltipModule,
  ],
  templateUrl: './grocery-export-dialog.component.html',
  styleUrl: './grocery-export-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroceryExportDialog {
  private groceryExport = inject(GroceryExportService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  data = inject<GroceryExportDialogData>(MAT_DIALOG_DATA);

  /** Local copy so connect/disconnect can update state without touching the caller's array. */
  providers = signal<GroceryProviderInfo[]>(this.data.providers);

  step = signal<DialogStep>('pick');
  selectedKey = signal<string | null>(null);
  format = signal<GroceryExportFormat>('plain');
  excludeChecked = signal(true);

  sending = signal(false);
  connecting = signal(false);
  result = signal<GroceryExportResult | null>(null);
  error = signal('');

  // --- Store picker (Cart providers) ---
  postalCode = signal('');
  stores = signal<GroceryStore[]>([]);
  searchingStores = signal(false);
  savingStore = signal(false);
  storeError = signal('');
  storeSearched = signal(false);

  readonly formats: { value: GroceryExportFormat; label: string }[] = [
    { value: 'plain', label: 'Plain text' },
    { value: 'markdown', label: 'Markdown' },
    { value: 'csv', label: 'CSV' },
  ];

  /** Web Share is mobile/secure-context only — the button hides everywhere else. */
  readonly canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';
  readonly canCopy =
    typeof navigator !== 'undefined' && typeof navigator.clipboard?.writeText === 'function';

  selected = computed<GroceryProviderInfo | null>(
    () => this.providers().find((p) => p.key === this.selectedKey()) ?? null,
  );

  /** Cart destinations need a linked account before they can be filled. */
  needsConnection = computed(() => {
    const provider = this.selected();
    return !!provider && provider.requiresConnection && !provider.connected;
  });

  canSend = computed(() => {
    const provider = this.selected();
    if (!provider || !provider.configured || this.sending()) return false;
    if (this.data.shoppingListId === null) return false;
    return !this.needsConnection();
  });

  unmatched = computed(() => this.result()?.unmatched ?? []);

  selectProvider(provider: GroceryProviderInfo): void {
    if (!provider.configured) return;
    this.selectedKey.set(provider.key);
    this.result.set(null);
    this.error.set('');
  }

  setFormat(value: GroceryExportFormat): void {
    this.format.set(value);
    // A finished text export is regenerated so the preview matches the choice.
    if (this.step() === 'result' && this.result()?.kind === 'Text') {
      this.send();
    }
  }

  send(): void {
    const provider = this.selected();
    const listId = this.data.shoppingListId;
    if (!provider || listId === null || this.sending()) return;

    this.sending.set(true);
    this.error.set('');

    this.groceryExport
      .exportList(listId, {
        provider: provider.key,
        format: provider.kind === 'Text' ? this.format() : undefined,
        excludeChecked: this.excludeChecked(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.sending.set(false);
          this.result.set(result);
          this.step.set('result');

          // 200 with success:false is the normal shape for an expected failure.
          if (!result.success) {
            this.error.set(result.error || 'The export could not be completed.');
            return;
          }

          if (result.kind === 'Link' && result.url) {
            // Also rendered as a link below in case the popup is blocked.
            window.open(result.url, '_blank', 'noopener');
          }
        },
        error: () => {
          this.sending.set(false);
          this.error.set('Could not reach the grocery service. Please try again.');
        },
      });
  }

  /** Hand the browser to the retailer's consent screen (full-page, never framed). */
  connect(): void {
    const provider = this.selected();
    if (!provider || this.connecting()) return;

    this.connecting.set(true);
    this.error.set('');

    this.groceryExport
      .startConnection(provider.key, this.data.returnUrl)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ url }) => {
          window.location.href = url;
        },
        error: () => {
          this.connecting.set(false);
          this.error.set(`${provider.displayName} could not be connected right now.`);
        },
      });
  }

  disconnect(): void {
    const provider = this.selected();
    if (!provider) return;

    this.groceryExport
      .disconnect(provider.key)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.providers.update((list) =>
            list.map((p) => (p.key === provider.key ? { ...p, connected: false } : p)),
          );
          this.result.set(null);
          this.step.set('pick');
          this.snackBar.open(`Disconnected from ${provider.displayName}.`, 'OK', {
            duration: 3000,
          });
        },
        error: () => this.error.set('Could not disconnect. Please try again.'),
      });
  }

  share(): void {
    const text = this.result()?.text;
    if (!text) return;
    navigator.share({ title: this.data.listName, text }).catch(() => {
      /* the user dismissed the share sheet */
    });
  }

  copy(): void {
    const text = this.result()?.text;
    if (!text) return;
    navigator.clipboard.writeText(text).then(
      () => this.snackBar.open('Copied to clipboard', 'OK', { duration: 2000 }),
      () => this.error.set('Could not copy to the clipboard.'),
    );
  }

  // --- Store picker ---

  openStorePicker(): void {
    this.storeError.set('');
    this.storeSearched.set(false);
    this.stores.set([]);
    this.step.set('store');
  }

  backToPick(): void {
    this.step.set('pick');
  }

  findStores(): void {
    const provider = this.selected();
    const postal = this.postalCode().trim();
    if (!provider || !postal || this.searchingStores()) return;

    this.searchingStores.set(true);
    this.storeError.set('');

    this.groceryExport
      .findStores(provider.key, postal)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (stores) => {
          this.stores.set(stores);
          this.storeSearched.set(true);
          this.searchingStores.set(false);
        },
        error: () => {
          this.searchingStores.set(false);
          this.storeError.set('Could not look up stores for that postal code.');
        },
      });
  }

  chooseStore(store: GroceryStore): void {
    const provider = this.selected();
    if (!provider || this.savingStore()) return;

    this.savingStore.set(true);
    this.storeError.set('');

    this.groceryExport
      .setStore(provider.key, { locationId: store.id, locationName: store.name })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingStore.set(false);
          this.snackBar.open(`Shopping ${store.name}.`, 'OK', { duration: 3000 });
          this.step.set('pick');
        },
        error: () => {
          this.savingStore.set(false);
          this.storeError.set('Could not save that store. Please try again.');
        },
      });
  }
}
