import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { PipelineService } from '../core/services/pipeline.service';
import {
  LinkUndoPreview,
  PipelineGateLane,
  PipelineLineFunnel,
  PipelineLinkStateCount,
  PipelineStatus,
  PipelineStuckFood,
  RereadPreview,
  UnfoldPreview,
} from '../core/models/pipeline.model';

interface FunnelRow {
  key: string;
  label: string;
  field: keyof PipelineLineFunnel;
  indent?: boolean;
}

interface StuckList {
  key: string;
  title: string;
  hint: string;
  items: PipelineStuckFood[];
}

/**
 * Where recipes, recipe lines and foods wait in the automated catalog pipeline, each lane's
 * settings and queue, and the model lanes' live state from the lane gate. Read-only apart from
 * the AI pause switches; the dry runs call the existing previews and never apply anything.
 */
@Component({
  selector: 'nom-pipeline-admin',
  imports: [RouterLink, DecimalPipe, DatePipe, MatButtonModule, MatIconModule, MatSlideToggleModule],
  templateUrl: './pipeline-admin.component.html',
  styleUrl: './pipeline-admin.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PipelineAdmin implements OnInit {
  private pipeline = inject(PipelineService);
  private destroyRef = inject(DestroyRef);

  readonly funnelRows: FunnelRow[] = [
    { key: 'ingredient', label: 'Ingredient lines', field: 'ingredient' },
    { key: 'on-curated-food', label: 'On a curated food', field: 'onCuratedFood' },
    { key: 'unresolved', label: 'On an unresolved wording', field: 'unresolved' },
    { key: 'not-measured', label: 'Not measured', field: 'notMeasured' },
    { key: 'no-data', label: 'No nutrition data', field: 'noData' },
    { key: 'no-conversion', label: 'No gram conversion', field: 'noConversion' },
    { key: 'counted', label: 'Counted on the label', field: 'counted' },
    { key: 'counted-by-serving-fallback', label: 'of which by serving-size fallback', field: 'countedByServingFallback', indent: true },
    { key: 'countable-strict', label: 'Countable without the fallback', field: 'countableStrict' },
    { key: 'count-without-item-weight', label: 'Piece lines without item weight', field: 'countWithoutItemWeight' },
    { key: 'volume-without-density', label: 'Volume lines without density', field: 'volumeWithoutDensity' },
  ];

  readonly linkStateLabels: Record<PipelineLinkStateCount['state'], string> = {
    pendingPick: 'Pending model pick',
    noCandidate: 'No USDA candidate',
    rejectedPick: 'Pick rejected',
    neverProposed: 'Never proposed',
  };

  data = signal<PipelineStatus | null>(null);
  loading = signal(false);
  errorMessage = signal('');
  pauseBusy = signal<string | null>(null);
  pauseError = signal('');

  unfold = signal<UnfoldPreview | null>(null);
  unfoldBusy = signal(false);
  unfoldError = signal('');
  reread = signal<RereadPreview | null>(null);
  rereadBusy = signal(false);
  rereadError = signal('');
  linkUndo = signal<LinkUndoPreview | null>(null);
  linkUndoBusy = signal(false);
  linkUndoError = signal('');
  undoSince = signal('');
  leaveMoved = signal(false);

  status = computed(() => this.data()?.status ?? null);

  stuckLists = computed<StuckList[]>(() => {
    const stuck = this.status()?.stuck;
    if (!stuck) return [];
    return [
      { key: 'wordings', title: 'Unresolved wordings', hint: 'ingredient lines', items: stuck.unresolvedWordings },
      { key: 'item-weight', title: 'Curated foods without item weight', hint: 'measured piece lines', items: stuck.countWithoutItemWeight },
      { key: 'density', title: 'Curated foods without density', hint: 'measured volume lines', items: stuck.volumeWithoutDensity },
    ];
  });

  ngOnInit(): void {
    this.load(false);
  }

  refresh(): void {
    this.load(true);
  }

  private load(refresh: boolean): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.pipeline.getStatus(refresh).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.data.set(null);
        this.loading.set(false);
        this.errorMessage.set('Unable to load the pipeline status.');
      },
    });
  }

  gateFor(lane: string | null): PipelineGateLane | null {
    if (!lane) return null;
    return this.data()?.gate.lanes.find((l) => l.lane === lane) ?? null;
  }

  setPause(lane: string, paused: boolean): void {
    this.pauseBusy.set(lane);
    this.pauseError.set('');
    this.pipeline.setPause(lane, paused).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (gate) => {
        const current = this.data();
        if (current) this.data.set({ ...current, gate });
        this.pauseBusy.set(null);
      },
      error: () => {
        this.pauseBusy.set(null);
        this.pauseError.set('Unable to change the pause switch.');
      },
    });
  }

  previewUnfold(): void {
    this.unfoldBusy.set(true);
    this.unfoldError.set('');
    this.pipeline.previewUnfold().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (preview) => {
        this.unfold.set(preview);
        this.unfoldBusy.set(false);
      },
      error: () => {
        this.unfoldBusy.set(false);
        this.unfoldError.set('Unable to run the unfold dry run.');
      },
    });
  }

  previewReread(): void {
    this.rereadBusy.set(true);
    this.rereadError.set('');
    this.pipeline.previewReread().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (preview) => {
        this.reread.set(preview);
        this.rereadBusy.set(false);
      },
      error: () => {
        this.rereadBusy.set(false);
        this.rereadError.set('Unable to run the re-read dry run.');
      },
    });
  }

  setUndoSince(value: string): void {
    this.undoSince.set(value);
  }

  setLeaveMoved(value: boolean): void {
    this.leaveMoved.set(value);
  }

  previewLinkUndo(): void {
    const since = this.undoSince();
    const at = since ? new Date(since) : null;
    if (!at || Number.isNaN(at.getTime())) {
      this.linkUndoError.set('Choose the time to take links back from.');
      return;
    }
    this.linkUndoBusy.set(true);
    this.linkUndoError.set('');
    this.pipeline.previewLinkUndo(at.toISOString(), this.leaveMoved()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (preview) => {
        this.linkUndo.set(preview);
        this.linkUndoBusy.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.linkUndoBusy.set(false);
        this.linkUndoError.set(err.status === 400 ? 'The time was not accepted.' : 'Unable to run the link undo dry run.');
      },
    });
  }
}
