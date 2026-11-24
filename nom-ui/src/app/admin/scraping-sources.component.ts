import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { ScrapingSourceService } from '../core/services/scraping-source.service';
import {
  RightsClass,
  RightsStep,
  ScrapingSourceModel,
  SourceCandidate,
  SourceCheck,
  SourceDiscoveryRun,
  SourceFacts,
  SourceJournalEntry,
  SourceReason,
  SourceRecipeStep,
  SourceRefusingHost,
  SourceReview,
  SourceRightsByHandResult,
  SourceRightsRecord,
  SourceRightsRun,
  SourceTaken,
} from '../core/models/scraping-source.model';
import { LoadingService } from '../core/services/loading.service';

type SourceAction = 'pause' | 'withhold' | 'release';

interface PendingAction {
  sourceId: number;
  action: SourceAction;
  recipes: number | null;
}

@Component({
  selector: 'nom-scraping-sources',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    ErrorBanner,
  ],
  templateUrl: './scraping-sources.component.html',
  styleUrl: './scraping-sources.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScrapingSources implements OnInit {
  private scrapingSourceService = inject(ScrapingSourceService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  readonly licenceKinds = ['public-domain', 'cc0', 'public-domain-mark', 'cc-by'];
  readonly handClasses: RightsClass[] = ['forbidden', 'permission-needed', 'facts-only', 'clear'];

  review = signal<SourceReview | null>(null);
  pending = signal<ScrapingSourceModel[]>([]);
  loading = signal(true);
  processing = signal(false);
  errorMessage = signal('');
  handError = signal('');
  /** Source id whose inline reject-notes prompt is open. */
  rejectingId = signal<number | null>(null);
  rejectNotes = new FormControl('');
  /** A pause, withholding or release waiting for its confirmation, with the dry run's recipe count. */
  pendingAction = signal<PendingAction | null>(null);
  actionReason = new FormControl('');

  rightsRun = signal<SourceRightsRun | null>(null);
  stepResult = signal<SourceRecipeStep | null>(null);
  taken = signal<SourceTaken[]>([]);
  /** Source id whose written-permission form is open. */
  permissionFor = signal<number | null>(null);
  permissionError = signal('');

  permissionForm = new FormGroup({
    grantedBy: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    grantedOn: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    text: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(2000)] }),
  });

  byHandResult = signal<SourceRightsByHandResult | null>(null);
  byHandError = signal('');

  byHandForm = new FormGroup({
    host: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    rightsClass: new FormControl<RightsClass>('forbidden', { nonNullable: true, validators: [Validators.required] }),
    quote: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1000)] }),
    quoteUrl: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    readOn: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  handForm = new FormGroup({
    domain: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    licenceKind: new FormControl('public-domain', { nonNullable: true, validators: [Validators.required] }),
    licenceStatement: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    licenceFoundAt: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  queue = computed(() => this.review()?.queue ?? []);
  notClear = computed(() => this.review()?.notClear ?? []);
  approved = computed(() => this.review()?.approved ?? []);
  hand = computed(() => this.review()?.hand ?? []);
  runs = computed(() => this.review()?.runs ?? []);
  journal = computed(() => this.review()?.journal ?? []);
  rightsRecords = computed(() => this.review()?.rightsRecords ?? []);
  refusingHosts = computed(() => this.review()?.refusingHosts ?? []);
  heldRefusing = computed(() => this.refusingHosts().filter((h) => h.held));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.scrapingSourceService.getReview().pipe(
      this.loadingService.loading('Loading recipe sources...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (review) => {
        this.review.set(review);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load recipe sources. You may not have permission.');
      },
    });
    this.scrapingSourceService.getSources('Pending').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (sources) => this.pending.set(sources.filter((s) => (s.kind ?? 'web') === 'web')),
      error: () => this.pending.set([]),
    });
    this.scrapingSourceService.taken().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (taken) => this.taken.set(taken),
      error: () => this.taken.set([]),
    });
  }

  refusingSummary(run: SourceDiscoveryRun): string {
    return (run.refusingHostsList ?? [])
      .map((h) => `${h.domain} (${h.botChallenge ? 'bot challenge' : 'HTTP ' + (h.httpStatus ?? '?')})`)
      .join(', ');
  }

  recordByHand(apply: boolean): void {
    if (this.byHandForm.invalid) {
      this.byHandForm.markAllAsTouched();
      return;
    }
    this.processing.set(true);
    this.byHandError.set('');
    this.scrapingSourceService.recordRightsByHand(this.byHandForm.getRawValue(), apply).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (result) => {
        this.byHandResult.set(result);
        this.processing.set(false);
        if (apply && result.applied) {
          this.byHandForm.reset({ rightsClass: 'forbidden' });
          this.load();
        }
      },
      error: (err) => {
        const result = err?.error as SourceRightsByHandResult | undefined;
        if (result?.refused) this.byHandResult.set(result);
        this.byHandError.set(result?.refused ?? err?.error?.error ?? 'Failed to record the class.');
        this.processing.set(false);
      },
    });
  }

  byHandSources(result: SourceRightsByHandResult): string {
    return result.sources
      .map((s) => `${s.domain} ${s.oldClass ?? 'unclassed'} → ${s.newClass} (${s.recipes} recipes)`)
      .join(', ');
  }

  withdrawRecord(record: SourceRightsRecord): void {
    if (record.journalEntryId == null) return;
    this.run(this.scrapingSourceService.undo(record.journalEntryId), 'Failed to withdraw the class recorded by hand.');
  }

  releaseRefusing(host: SourceRefusingHost): void {
    this.run(this.scrapingSourceService.releaseRefusing(host.id), 'Failed to release the host.');
  }

  excludedHosts(run: SourceDiscoveryRun): string {
    return (run.candidatesExcludedHosts ?? []).map((h) => h.domain).join(', ');
  }

  canRecordPermission(source: ScrapingSourceModel): boolean {
    return !!source.rightsClass && source.rightsClass !== 'clear' && !source.permissionRecordedAtUtc;
  }

  startPermission(source: ScrapingSourceModel): void {
    this.permissionForm.reset();
    this.permissionError.set('');
    this.permissionFor.set(source.id);
  }

  cancelPermission(): void {
    this.permissionFor.set(null);
  }

  savePermission(source: ScrapingSourceModel): void {
    if (this.permissionForm.invalid) {
      this.permissionForm.markAllAsTouched();
      return;
    }
    this.processing.set(true);
    this.permissionError.set('');
    this.scrapingSourceService.recordPermission(source.id, this.permissionForm.getRawValue()).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.processing.set(false);
        this.permissionFor.set(null);
        this.load();
      },
      error: (err) => {
        this.permissionError.set(err?.error?.error ?? 'Failed to record the permission.');
        this.processing.set(false);
      },
    });
  }

  termsCheck(source: ScrapingSourceModel): void {
    this.run(this.scrapingSourceService.termsCheck(source.id), 'Failed to start the terms-only check.');
  }

  termsCheckWaiting(source: ScrapingSourceModel): boolean {
    const status = source.termsCheck?.status;
    return status === 'queued' || status === 'running';
  }

  checkKind(check: SourceCheck): string {
    const kind = check.mode === 'terms-only'
      ? 'terms-only (robots.txt, the homepage and the terms pages, no recipe page)'
      : 'full (with sample recipe pages)';
    return `Last check: ${kind}, ${check.status}`;
  }

  accessNotes(facts: SourceFacts): string[] {
    const notes: string[] = [];
    if (facts.noAddress) notes.push('the host has no address, so nothing could be read');
    if (facts.termsOnly) notes.push('terms-only check: robots.txt, the homepage and the terms pages, no recipe page');
    if (facts.homepageLinks === 0) notes.push('the homepage as served carries no links (built by scripts)');
    const refused = facts.refusedPages ?? [];
    if (refused.length) notes.push(`refused NomBot: ${refused.join(', ')}`);
    if (facts.samplesRefused) notes.push(`${facts.samplesRefused} sampled recipe page(s) refused NomBot`);
    return notes;
  }

  factsOnlyUse(source: ScrapingSourceModel, approve: boolean): void {
    this.run(this.scrapingSourceService.factsOnlyUse(source.id, approve), 'Failed to change facts-only use.');
  }

  deriveRights(apply: boolean): void {
    this.processing.set(true);
    this.errorMessage.set('');
    this.scrapingSourceService.deriveRights(apply).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (run) => {
        this.rightsRun.set(run);
        this.processing.set(false);
        if (apply) this.load();
      },
      error: (err) => {
        this.errorMessage.set(err?.error?.error ?? 'Failed to derive the rights classes.');
        this.processing.set(false);
      },
    });
  }

  rightsStep(step: RightsStep, apply: boolean): void {
    this.stepRequest(this.scrapingSourceService.rightsStep(step, apply), apply);
  }

  undoStep(result: SourceRecipeStep): void {
    const step = result.step as RightsStep;
    this.stepRequest(this.scrapingSourceService.undoRightsStep(step, result.stamp, true), true);
  }

  skipped(result: SourceRecipeStep): string[] {
    return Object.entries(result.skippedByReason ?? {}).map(([reason, count]) => `${count} ${reason}`);
  }

  private stepRequest(request: Observable<SourceRecipeStep>, reload: boolean): void {
    this.processing.set(true);
    this.errorMessage.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        this.stepResult.set(result);
        this.processing.set(false);
        if (reload) this.load();
      },
      error: (err) => {
        this.errorMessage.set(err?.error?.error ?? 'The step failed.');
        this.processing.set(false);
      },
    });
  }

  against(reasons: SourceReason[] | undefined): SourceReason[] {
    return (reasons ?? []).filter((r) => r.outcome === 'fail' || r.outcome === 'unknown');
  }

  approveCandidate(candidate: SourceCandidate): void {
    this.run(this.scrapingSourceService.approveCandidate(candidate.id), 'Failed to approve the source.');
  }

  pauseHarvest(source: ScrapingSourceModel): void {
    this.actionReason.setValue('');
    this.pendingAction.set({ sourceId: source.id, action: 'pause', recipes: null });
  }

  resumeHarvest(source: ScrapingSourceModel): void {
    this.run(this.scrapingSourceService.resumeHarvest(source.id), 'Failed to resume harvesting.');
  }

  startWithhold(source: ScrapingSourceModel): void {
    this.actionReason.setValue('');
    this.preview(source.id, 'withhold');
  }

  startRelease(source: ScrapingSourceModel): void {
    this.preview(source.id, 'release');
  }

  cancelAction(): void {
    this.pendingAction.set(null);
    this.actionReason.setValue('');
  }

  confirmAction(): void {
    const action = this.pendingAction();
    if (!action) return;
    const reason = this.actionReason.value ?? '';
    const request =
      action.action === 'pause'
        ? this.scrapingSourceService.pauseHarvest(action.sourceId, reason)
        : action.action === 'withhold'
          ? this.scrapingSourceService.withholdRecipes(action.sourceId, true, reason)
          : this.scrapingSourceService.releaseRecipes(action.sourceId, true);
    this.run(request, 'The action failed.');
  }

  undo(entry: SourceJournalEntry): void {
    this.run(this.scrapingSourceService.undo(entry.id), 'Failed to undo the entry.');
  }

  recordHand(): void {
    if (this.handForm.invalid) {
      this.handForm.markAllAsTouched();
      return;
    }
    this.handError.set('');
    this.processing.set(true);
    this.scrapingSourceService.recordHand(this.handForm.getRawValue()).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.handForm.reset();
        this.processing.set(false);
        this.load();
      },
      error: (err) => {
        this.handError.set(err?.error?.error ?? 'Failed to record the hand source.');
        this.processing.set(false);
      },
    });
  }

  startReject(source: ScrapingSourceModel): void {
    this.rejectNotes.setValue('');
    this.rejectingId.set(source.id);
  }

  cancelReject(): void {
    this.rejectingId.set(null);
    this.rejectNotes.setValue('');
  }

  confirmReject(source: ScrapingSourceModel): void {
    this.run(this.scrapingSourceService.reject(source.id, this.rejectNotes.value ?? ''), 'Failed to reject source.');
  }

  private preview(sourceId: number, action: 'withhold' | 'release'): void {
    this.processing.set(true);
    const request = action === 'withhold'
      ? this.scrapingSourceService.withholdRecipes(sourceId, false)
      : this.scrapingSourceService.releaseRecipes(sourceId, false);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (report) => {
        this.pendingAction.set({ sourceId, action, recipes: report.recipes });
        this.processing.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err?.error?.error ?? 'The dry run failed.');
        this.processing.set(false);
      },
    });
  }

  private run(request: Observable<unknown>, failure: string): void {
    this.processing.set(true);
    this.errorMessage.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.processing.set(false);
        this.pendingAction.set(null);
        this.rejectingId.set(null);
        this.load();
      },
      error: (err) => {
        this.errorMessage.set(err?.error?.error ?? failure);
        this.processing.set(false);
      },
    });
  }
}
