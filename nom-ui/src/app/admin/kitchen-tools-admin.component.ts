import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { KitchenToolsService } from '../core/services/kitchen-tools.service';
import {
  KitchenToolSuggestion,
  KitchenToolSuggestionStatus,
  KitchenToolTaggingStatus,
} from '../core/models/kitchen-tools.model';

/**
 * Admin panel for the AI kitchen-tool lane: how far tagging has got, and the queue of
 * uncommon equipment the model noticed. Approving adds the tool to every household's
 * Kitchen list (unticked) and links it to the recipes it was seen in.
 */
@Component({
  selector: 'nom-kitchen-tools-admin',
  imports: [RouterLink, DecimalPipe, MatButtonModule, MatIconModule],
  templateUrl: './kitchen-tools-admin.component.html',
  styleUrl: './kitchen-tools-admin.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KitchenToolsAdmin implements OnInit {
  private kitchenService = inject(KitchenToolsService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  readonly categories = ['Stove & oven', 'Appliances', 'Cookware & bakeware', 'Outdoor', 'Gadgets', 'Specialty equipment'];
  readonly statuses: KitchenToolSuggestionStatus[] = ['pending', 'approved', 'rejected'];

  status = signal<KitchenToolTaggingStatus | null>(null);
  suggestions = signal<KitchenToolSuggestion[]>([]);
  filter = signal<KitchenToolSuggestionStatus>('pending');
  loading = signal(false);
  tagging = signal(false);
  busyId = signal<number | null>(null);
  errorMessage = signal('');

  private names = new Map<number, string>();
  private chosenCategories = new Map<number, string>();

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.kitchenService.getTaggingStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (s) => this.status.set(s),
      error: () => this.errorMessage.set('Unable to load tagging status.'),
    });
    this.kitchenService.getSuggestions(this.filter()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (list) => {
        this.suggestions.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to load suggestions.');
      },
    });
  }

  setFilter(status: KitchenToolSuggestionStatus): void {
    this.filter.set(status);
    this.refresh();
  }

  nameFor(s: KitchenToolSuggestion): string {
    return this.names.get(s.id) ?? s.displayName;
  }

  categoryFor(s: KitchenToolSuggestion): string {
    return this.chosenCategories.get(s.id) ?? s.suggestedCategory ?? 'Specialty equipment';
  }

  setName(s: KitchenToolSuggestion, value: string): void {
    this.names.set(s.id, value);
  }

  setCategory(s: KitchenToolSuggestion, value: string): void {
    this.chosenCategories.set(s.id, value);
  }

  approve(s: KitchenToolSuggestion): void {
    const name = this.nameFor(s).trim();
    if (!name) return;
    this.busyId.set(s.id);
    this.kitchenService.approveSuggestion(s.id, name, this.categoryFor(s)).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (r) => {
        this.busyId.set(null);
        this.snackBar.open(`Added “${name}” — linked to ${r.recipesLinked} recipe${r.recipesLinked === 1 ? '' : 's'}.`, 'OK', { duration: 4000 });
        this.refresh();
      },
      error: () => {
        this.busyId.set(null);
        this.errorMessage.set(`Couldn't approve “${name}”. It may already have been reviewed.`);
      },
    });
  }

  reject(s: KitchenToolSuggestion): void {
    this.busyId.set(s.id);
    this.kitchenService.rejectSuggestion(s.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busyId.set(null);
        this.refresh();
      },
      error: () => {
        this.busyId.set(null);
        this.errorMessage.set(`Couldn't reject “${s.displayName}”.`);
      },
    });
  }

  tagNow(): void {
    this.tagging.set(true);
    this.errorMessage.set('');
    this.kitchenService.tagBatch().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (r) => {
        this.tagging.set(false);
        this.snackBar.open(`Tagged ${r.tagged} recipes · ${r.toolLinks} tool links · ${r.suggestions} new suggestions.`, 'OK', { duration: 4000 });
        this.refresh();
      },
      error: (err) => {
        this.tagging.set(false);
        this.errorMessage.set(
          err.status === 503 ? 'The tagging model is not configured or not responding.' : 'Tagging failed. Please try again.',
        );
      },
    });
  }
}
