import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { PipelineAdmin } from './pipeline-admin.component';
import { LaneFrameLane, LinkRecheckRun, PipelineFrame, PipelineGate, PipelineLineFunnel, PipelineStatus } from '../core/models/pipeline.model';

const funnel = (counted: number): PipelineLineFunnel => ({
  total: 818084,
  notAnIngredient: 37346,
  ingredient: 780738,
  onCuratedFood: 591403,
  unresolved: 189335,
  notMeasured: 233233,
  noData: 122163,
  noConversion: 16331,
  counted,
  countedByServingFallback: 26103,
  countWithoutItemWeight: 30610,
  volumeWithoutDensity: 12024,
  countableStrict: 382911,
});

const gate = (pausedLink = false): PipelineGate => ({
  pauseAll: false,
  lanes: [
    {
      lane: 'link',
      label: 'ingredient link picker and verifier',
      paused: pausedLink,
      status: {
        lane: 'link', state: pausedLink ? 'paused' : 'idle', stateSince: null, stateReason: null, model: 'qwen2.5:14b-instruct',
        lastPass: null, lastPassItems: 0, lastError: null, lastErrorAt: null, itemsToday: 0, itemsTodayFromLedger: false, dailyCap: null,
        modelCalls: 0, modelCallErrors: 0, lastCall: null, lastSuccess: null, latencyMedianMs: null, latencyP95Ms: null,
        countingSince: '2026-11-20T00:00:00Z',
      },
    },
  ],
  hosts: [],
});

const frameLane = (overrides: Partial<LaneFrameLane> = {}): LaneFrameLane => ({
  lane: 'wording-read',
  tier: '*',
  mode: 'paused',
  reason: 'paused: 10 confirmed errors in the last 200 audited writes, error upper bound 8.97% above the pause bar 3.0%',
  since: '2026-11-20T10:00:00Z',
  signatureHash: 'sig-1',
  evaluatedAt: '2026-11-20T10:00:00Z',
  lastCleanAt: '2026-11-20T08:00:00Z',
  figures: {
    evaluatedAt: '2026-11-20T10:00:00Z', signature: 'sig-1', decisions: 400, written: 200, audited: 400, errors: 10, flagged: 4,
    observedError: 0.025, correctedError: 0.026, precisionLower: 0.955, precisionUpper: 0.987, qualifyBar: 0.98, neededAudited: 210,
    windowAudited: 200, windowErrors: 10, windowError: 0.05, windowErrorLower: 0.027, windowErrorUpper: 0.0897, pauseBar: 0.03, window: 200,
    auditorSource: 'link-audit golden', auditorSensitivity: 0.95, auditorFalseFlagRate: 0.01, auditorKnownBad: 400, auditorKnownGood: 1000,
    auditorExact: false, auditorProblem: null, goldenProblem: null, pauseReasons: [],
  },
  decisionsLast24h: 400,
  writtenLast24h: 200,
  canUndo: true,
  ...overrides,
});

const frame = (lanes: LaneFrameLane[]): PipelineFrame => ({
  lanes,
  roster: { readerA: 'qwen2.5:14b-instruct', readerAFamily: 'qwen', readerB: 'mistral-nemo', readerBFamily: 'mistral', auditor: 'phi4', auditorFamily: 'phi' },
});

function status(quietMinutesLeft: number | null = null): PipelineStatus {
  return {
    status: {
      generatedAt: '2026-11-20T12:00:00Z',
      computeMs: 2400,
      fromCache: false,
      recipes: {
        approved: 22842, waiting: 71185, needsRevision: 27754, pendingReview: 0, rejected: 724,
        waitingBlockedBy: { none: 1238, one: 29568, two: 20445, threeOrMore: 19934 },
        waitingPublicDomain: 53027, waitingScrapedWithoutOwnImage: 18155, waitingWithSourceProse: 567, proseWithheld: 1710, proseWithheldApproved: 1389, proseGaveUp: 12, proseGaveUpByClass: { 'copied-text': 9, 'judge:missing': 3 },
        revisionMissingQuantities: 22932, revisionSteps: 1267, approvedWithUncountedLine: 2716, approvedWithoutImage: 22569,
        withoutNutritionLabel: 24012, lastAutoApprovedAt: null, autoApprovedLast24h: 0, lastScrapedAt: null, createdLast7d: 0,
      },
      lines: { all: funnel(409011), approved: funnel(89320), folded: 17560, foldedOnApproved: 2467 },
      foods: {
        curated: 6997, pendingReview: 5292, notCurated: 75501, curatedWithoutNutrition: 1, curatedWithoutUsdaLink: 15, curatedWithoutFoodGroup: 257,
        foodsMissingItemWeight: 936, foodsMissingDensity: 366, wordingsInUse: 75493, unresolvedWordings: 65967,
        wordingsByUse: [{ bucket: '1', wordings: 51826, lines: 51826 }],
        unresolvedByLinkState: [{ state: 'pendingPick', foods: 68252, lines: 170223 }],
      },
      stuck: {
        unresolvedWordings: [
          { ingredientId: 334, name: 'Vanilla', lines: 2433 },
          { ingredientId: 9234, name: 'white wine', lines: 1461 },
        ],
        countWithoutItemWeight: [{ ingredientId: 275119, name: 'Lemon Juice', lines: 2481 }],
        volumeWithoutDensity: [],
      },
      proposals: {
        pending: 68269, pendingModelPicks: 68267, pendingModelPicksVerified: 0, pendingExactMatches: 2, pendingOther: 0, linkQueue: 0,
        verifiedAppliedLast24h: 0, verifiedDailyCap: 500, verifiedPerFoodCap: 25, foodsAtCap: 0,
        nameMatchAppliedLast24h: 0, nameMatchDailyCap: 200, nameMatchPerFoodCap: 25, nameMatchFoodsAtCap: 0,
        lastProposedAt: null, lastAppliedBySystemAt: null,
      },
      images: {
        approvedWithoutImage: 22569, neverSearched: 21779, lastSearchedAt: null,
        candidates: { pending: 662, attached: 122, approved: 7, rejected: 2, superseded: 9, removed: 0 },
      },
      lanes: [
        {
          key: 'ingredient-link', name: 'Ingredient link picker and verifier', gateLane: 'link', external: false, enabled: true,
          usesModel: true, modelConfigured: true, model: 'qwen2.5:14b-instruct',
          switches: [
            { key: 'AutoApplyCertain', label: 'Apply staples and same-name attaches', on: true },
            { key: 'AutoApplyVerifiedAi', label: 'Apply verified model picks', on: false },
          ],
          caps: [{ label: 'Verified model picks per day', limit: 500, used: 0 }],
          queue: 0, queueLabel: 'foods in use waiting for a link proposal', lastActivityAt: null, activityLast24h: 0,
        },
        {
          key: 'intake', name: 'Recipe harvest', gateLane: null, external: true, enabled: true, usesModel: false, modelConfigured: null,
          model: null, switches: [], caps: [], queue: null, queueLabel: null, lastActivityAt: null, activityLast24h: null,
        },
      ],
      ai: { quietHours: '01:00-05:00', quietTimeZone: 'America/Denver', quietMinutesLeft },
      journal: [{ entityType: 'RecipeIngredient', changeType: 'LineRelink', last24h: 0, last7d: 72354, total: 72354, lastAt: '2026-11-17T04:54:00Z' }],
    },
    gate: gate(),
  };
}

describe('PipelineAdmin', () => {
  let http: HttpTestingController;

  function create() {
    TestBed.configureTestingModule({
      imports: [PipelineAdmin],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PipelineAdmin);
    fixture.detectChanges();
    return fixture;
  }

  function statusRequest(): TestRequest {
    return http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/Pipeline/status'));
  }

  function loaded(body: PipelineStatus = status()) {
    const fixture = create();
    statusRequest().flush(body);
    fixture.detectChanges();
    return fixture;
  }

  const byId = (el: HTMLElement, id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  afterEach(() => http.verify());

  it('shows the headline numbers', () => {
    const el = loaded().nativeElement as HTMLElement;

    expect(byId(el, 'pipeline-metric-recipes-approved')?.textContent?.trim()).toBe('22,842');
    expect(byId(el, 'pipeline-metric-lines-counted')?.textContent?.trim()).toBe('409,011');
    expect(byId(el, 'pipeline-metric-lines-approved-counted')?.textContent?.trim()).toBe('89,320');
    expect(byId(el, 'pipeline-metric-blocked-none')?.textContent?.trim()).toBe('1,238');
    expect(byId(el, 'pipeline-metric-recipes-prose-withheld')?.textContent?.trim()).toBe('1,710 (1,389)');
    expect(byId(el, 'pipeline-metric-recipes-prose-gave-up')?.textContent?.trim()).toBe('12');
    expect(Array.from(el.querySelectorAll('[data-testid="pipeline-prose-gave-up-class"]')).map(d => d.parentElement?.textContent?.trim()))
      .toEqual(['copied-text9', 'judge:missing3']);
    expect(byId(el, 'pipeline-generated-at')).not.toBeNull();
    expect(el.querySelectorAll('[data-testid="pipeline-journal-item"]').length).toBe(1);
    expect(byId(el, 'pipeline-metric-foods-wordings-in-use')?.parentElement?.textContent).toContain('Uncurated foods on a line');
  });

  it('shows a lane with its switch off, its cap and its state from the lane gate', () => {
    const el = loaded().nativeElement as HTMLElement;

    expect(byId(el, 'pipeline-lane-ingredient-link')?.textContent).toContain('qwen2.5:14b-instruct');
    expect(byId(el, 'pipeline-lane-ingredient-link-switch-AutoApplyVerifiedAi')?.textContent).toContain('Off');
    expect(byId(el, 'pipeline-lane-ingredient-link-switch-AutoApplyCertain')?.textContent).toContain('On');
    expect(byId(el, 'pipeline-metric-verified-cap')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('0 / 500');
    expect(byId(el, 'pipeline-lane-ingredient-link-state')?.textContent?.trim()).toBe('idle');
    expect(byId(el, 'pipeline-lane-intake-pause-toggle')).toBeNull();
  });

  it('lists the stuck foods in the order given and shows an empty state for an empty list', () => {
    const el = loaded().nativeElement as HTMLElement;

    const items = Array.from(byId(el, 'pipeline-stuck-wordings-list')!.querySelectorAll('[data-testid="pipeline-stuck-item"]'));
    expect(items.map((i) => i.textContent)).toEqual([expect.stringContaining('Vanilla'), expect.stringContaining('white wine')]);
    expect(byId(el, 'pipeline-stuck-density-empty')).not.toBeNull();
    expect(byId(el, 'pipeline-stuck-density-list')).toBeNull();
  });

  it('asks the server to recompute on refresh, and not on the first load', () => {
    const fixture = create();
    const first = statusRequest();
    expect(first.request.params.has('refresh')).toBe(false);
    first.flush(status());
    fixture.detectChanges();

    byId(fixture.nativeElement, 'pipeline-refresh-btn')!.click();

    const second = statusRequest();
    expect(second.request.params.get('refresh')).toBe('true');
    second.flush(status());
  });

  it.each([500, 403])('shows the error and no sections when the load fails with %s', (code) => {
    const fixture = create();
    statusRequest().flush({}, { status: code, statusText: 'nope' });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(byId(el, 'pipeline-error')?.textContent).toContain('Unable to load the pipeline status.');
    expect(byId(el, 'pipeline-recipes')).toBeNull();
    expect(byId(el, 'pipeline-lanes')).toBeNull();
  });

  it('shows the quiet-hours banner only while the quiet hours last', () => {
    expect(byId(loaded(status(null)).nativeElement, 'pipeline-quiet-hours')).toBeNull();
    TestBed.resetTestingModule();
    http.verify();

    const banner = byId(loaded(status(42)).nativeElement, 'pipeline-quiet-hours');
    expect(banner?.textContent).toContain('42 more minutes');
  });

  it('pauses one lane through the pipeline endpoint and shows the new state', () => {
    const fixture = loaded();
    const el = fixture.nativeElement as HTMLElement;

    byId(el, 'pipeline-lane-ingredient-link-pause-toggle')!.querySelector<HTMLButtonElement>('button')!.click();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Pipeline/pause/link'));
    expect(put.request.body).toEqual({ isEnabled: true });
    put.flush(gate(true));
    fixture.detectChanges();

    expect(byId(el, 'pipeline-lane-ingredient-link-state')?.textContent?.trim()).toBe('paused');
  });

  it('pauses every lane with the pause-all switch', () => {
    const fixture = loaded();

    byId(fixture.nativeElement, 'pipeline-pause-all-toggle')!.querySelector<HTMLButtonElement>('button')!.click();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Pipeline/pause/all'));
    expect(put.request.body).toEqual({ isEnabled: true });
    put.flush({ ...gate(), pauseAll: true });
  });

  it('says only a user-roles admin can turn a pause off and puts the switch back', () => {
    const body = status();
    body.gate = gate(true);
    const fixture = loaded(body);
    const el = fixture.nativeElement as HTMLElement;
    const button = () => byId(el, 'pipeline-lane-ingredient-link-pause-toggle')!.querySelector<HTMLButtonElement>('button')!;

    button().click();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Pipeline/pause/link'));
    expect(put.request.body).toEqual({ isEnabled: false });
    put.flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(byId(el, 'pipeline-pause-error')?.textContent).toContain('Only a user-roles admin can turn a pause off.');
    expect(button().getAttribute('aria-checked')).toBe('true');
  });

  it('runs the unfold and re-read dry runs and shows the counts the last round added', () => {
    const fixture = loaded();
    const el = fixture.nativeElement as HTMLElement;

    byId(el, 'pipeline-unfold-preview-btn')!.click();
    const unfold = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/FoodCatalog/relink/unfold'));
    expect(unfold.request.params.get('apply')).toBe('false');
    unfold.flush({
      applied: false, rowsExamined: 19817, folds: 16484, linesAdded: 16870, collapsed: 0, duplicatesDropped: 0, quantitiesReassigned: 0,
      unmeasuredAdded: 2893, unnamedUnmeasured: 41, skippedSingleSourceLine: 0, skippedAmbiguous: 3, skippedOtherKind: 2257, remaining: 16484,
    });
    byId(el, 'pipeline-reread-preview-btn')!.click();
    const reread = http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/FoodCatalog/reread/preview'));
    reread.flush({ linesExamined: 133000, changeCount: 7255, touchCount: 818, stopCounting: 1407, startCounting: 956, drainedAsContainers: 12, missingMeasurements: [] });
    fixture.detectChanges();

    expect(byId(el, 'pipeline-metric-unfold-unnamed-unmeasured')?.textContent?.trim()).toBe('41');
    expect(byId(el, 'pipeline-metric-reread-drained-as-containers')?.textContent?.trim()).toBe('12');
    expect(byId(el, 'pipeline-metric-reread-missing-measurements')?.textContent?.trim()).toBe('none');
  });

  it('runs the link undo dry run from a chosen time and shows the skipped and moved counts', () => {
    const fixture = loaded();
    const el = fixture.nativeElement as HTMLElement;

    byId(el, 'pipeline-link-undo-preview-btn')!.click();
    fixture.detectChanges();
    expect(byId(el, 'pipeline-link-undo-error')?.textContent).toContain('Choose the time');
    http.expectNone((r) => r.url.includes('undo-applied'));

    const since = byId(el, 'pipeline-link-undo-since-input') as HTMLInputElement;
    since.value = '2026-11-18T09:30';
    since.dispatchEvent(new Event('input'));
    const leave = byId(el, 'pipeline-link-undo-leave-moved-checkbox') as HTMLInputElement;
    leave.checked = true;
    leave.dispatchEvent(new Event('change'));
    byId(el, 'pipeline-link-undo-preview-btn')!.click();

    const undo = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/FoodCatalog/proposals/undo-applied'));
    expect(undo.request.params.get('apply')).toBe('false');
    expect(undo.request.params.get('leaveMovedLines')).toBe('true');
    expect(undo.request.params.get('sinceUtc')).toBe(new Date('2026-11-18T09:30').toISOString());
    undo.flush({ applied: false, proposals: 480, proposalsSkipped: 20, linesRestored: 3100, linesSkipped: 14, foodsRestored: 300, statusesRestored: 200, statusesKept: 3, remaining: 0 });
    fixture.detectChanges();

    expect(byId(el, 'pipeline-metric-link-undo-proposals-skipped')?.textContent?.trim()).toBe('20');
    expect(byId(el, 'pipeline-metric-link-undo-lines-skipped')?.textContent?.trim()).toBe('14');
    expect(byId(el, 'pipeline-link-undo-preview')?.textContent).toContain('Moved lines left in place');
  });
  it('lists each frame lane with its mode, reason, corrected figures, bounds and the roster', () => {
    const body = status();
    body.frame = frame([frameLane(), frameLane({ tier: 't2', mode: 'shadow', reason: 'shadow: 120 of 189 audited', canUndo: false })]);
    const el = loaded(body).nativeElement as HTMLElement;

    expect(byId(el, 'pipeline-frame-wording-read-all-mode')?.textContent?.trim()).toBe('paused');
    expect(byId(el, 'pipeline-frame-wording-read-all-reason')?.textContent).toContain('above the pause bar');
    expect(byId(el, 'pipeline-frame-wording-read-all-qualify')?.textContent).toContain('210 needed');
    expect(byId(el, 'pipeline-frame-wording-read-all-qualify')?.textContent).toContain('4 flags not confirmed');
    expect(byId(el, 'pipeline-frame-wording-read-all-window')?.textContent).toContain('3.0%');
    expect(byId(el, 'pipeline-frame-wording-read-all-window')?.textContent).toContain('observed error 5.0%');
    expect(byId(el, 'pipeline-frame-wording-read-all-auditor')?.textContent).toContain('1,000 known-good rows');
    expect(byId(el, 'pipeline-frame-wording-read-t2-mode')?.textContent?.trim()).toBe('shadow');
    expect(byId(el, 'pipeline-frame-wording-read-t2-undo-preview-btn')).toBeNull();
    expect(byId(el, 'pipeline-frame-roster')?.textContent).toContain('mistral-nemo (mistral)');
    expect(byId(el, 'pipeline-frame')?.textContent).toContain('agreement with the reviewer model');
  });

  it('shows an empty frame, and says so when the frame did not report', () => {
    const body = status();
    body.frame = frame([]);
    expect(byId(loaded(body).nativeElement, 'pipeline-frame-empty')).not.toBeNull();
    TestBed.resetTestingModule();
    http.verify();

    expect(byId(loaded(status()).nativeElement, 'pipeline-frame-error')).not.toBeNull();
  });

  it('shows the rebuild lane: waiting by tier, reasons, the last batch with its faults and the line judge', () => {
    const body = status();
    body.reprocess = {
      enabled: true,
      ruleVersion: 'line-builder/2',
      read: 120000,
      readWithCurrentRules: 119500,
      waiting: 4200,
      waitingByTier: { rebuild: 3000, order: 1200 },
      byReason: [
        { code: 'D1', label: 'order lost', recipes: 105775, waiting: 1200 },
        { code: 'D3', label: 'method fill fails its checks', recipes: 900, waiting: 850 },
      ],
      byOutcome: { rebuilt: 40, held: 3, shadow: 500 },
      heldByReason: { 'person-edit': 3 },
      lastDetectedAt: '2026-11-20T02:00:00Z',
      judgeModel: 'mistral-nemo',
      judgeProblem: 'the line judge has no golden score record',
      lastBatches: [
        { tier: 'rebuild', mode: 'shadow', stamp: '2026-11-20T10:00:00Z', taken: 100, rebuilt: 90, held: 4, unchanged: 6, failed: 0, linesChecked: 300, linesFailing: 9,
          faults: ['the rule checks passed on 97.0% of 300 changed lines, under 98%'] },
      ],
    };
    const el = loaded(body).nativeElement as HTMLElement;

    expect(byId(el, 'pipeline-metric-reprocess-waiting')?.textContent?.trim()).toBe('4,200');
    expect(byId(el, 'pipeline-metric-reprocess-waiting-order')?.textContent?.trim()).toBe('1,200');
    expect(byId(el, 'pipeline-metric-reprocess-rebuilt')?.textContent?.trim()).toBe('40');
    expect(byId(el, 'pipeline-metric-reprocess-undone')?.textContent?.trim()).toBe('0');
    expect(byId(el, 'pipeline-reprocess-reason-D3')?.textContent).toContain('850 waiting');
    expect(byId(el, 'pipeline-reprocess-batch-rebuild')?.textContent).toContain('291 of 300 changed lines');
    expect(byId(el, 'pipeline-reprocess-batch-rebuild')?.textContent).toContain('under 98%');
    expect(byId(el, 'pipeline-reprocess-judge')?.textContent).toContain('no golden score record');
    expect(byId(el, 'pipeline-reprocess-state')?.textContent).toContain('119,500 of 120,000');
  });

  it('says so when the rebuild lane did not report', () => {
    expect(byId(loaded(status()).nativeElement, 'pipeline-reprocess-empty')).not.toBeNull();
  });

  it('shows the link re-check: waiting picks, the withdrawal and the newest run of each set', () => {
    const body = status();
    const run = (set: string, extra: Partial<LinkRecheckRun> = {}): LinkRecheckRun => ({
      set, stamp: '2026-11-20T12:00:00Z', lane: false, mode: 'live', signature: 'abc', undone: false, groups: 0, kept: 0, held: 0,
      linesMoved: 0, linesUnlinked: 0, aliasesChanged: 0, withdrawn: 0, renamed: 0, ...extra,
    });
    body.linkRecheck = {
      enabled: false,
      version: 'link-recheck/1',
      signature: 'abc',
      sets: ['alcohol', 'applied'],
      lastRuns: [run('alcohol', { groups: 320, linesMoved: 1450, linesUnlinked: 120, held: 30 }), run('names', { renamed: 1, undone: true })],
      lastWithdraw: run('withdraw', { withdrawn: 66151 }),
      waitingPicks: 0,
    };
    const el = loaded(body).nativeElement as HTMLElement;

    expect(byId(el, 'pipeline-link-recheck-state')?.textContent).toContain('Off (LinkRecheck:Enabled)');
    expect(byId(el, 'pipeline-metric-link-recheck-waiting-picks')?.textContent?.trim()).toBe('0');
    expect(byId(el, 'pipeline-link-recheck-withdraw')?.textContent).toContain('66,151');
    expect(byId(el, 'pipeline-link-recheck-run-alcohol')?.textContent).toContain('1,450 lines moved');
    expect(byId(el, 'pipeline-link-recheck-run-alcohol')?.textContent).toContain('120 taken back');
    expect(byId(el, 'pipeline-link-recheck-run-names')?.textContent).toContain('1 renamed');
    expect(byId(el, 'pipeline-link-recheck-run-names')?.textContent).toContain('undone');
  });

  it('says so when the link re-check did not report, or has not run', () => {
    expect(byId(loaded(status()).nativeElement, 'pipeline-link-recheck-empty')).not.toBeNull();
    TestBed.resetTestingModule();
    http.verify();

    const body = status();
    body.linkRecheck = { enabled: true, version: 'link-recheck/1', signature: 'abc', sets: [], lastRuns: [], lastWithdraw: null, waitingPicks: 66151 };
    const el = loaded(body).nativeElement as HTMLElement;
    expect(byId(el, 'pipeline-link-recheck-no-runs')).not.toBeNull();
    expect(byId(el, 'pipeline-link-recheck-withdraw')?.textContent).toContain('have not been withdrawn');
  });

  it('runs a frame lane undo as a dry run and shows its counts', () => {
    const body = status();
    body.frame = frame([frameLane()]);
    const fixture = loaded(body);
    const el = fixture.nativeElement as HTMLElement;

    byId(el, 'pipeline-frame-wording-read-all-undo-preview-btn')!.click();
    const post = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Pipeline/frame/wording-read/*/undo'));
    expect(post.request.params.get('apply')).toBe('false');
    post.flush({ lane: 'wording-read', tier: '*', sinceUtc: '2026-11-20T08:00:00Z', applied: false, result: { undone: 37, skipped: 2, remaining: 0, note: null } });
    fixture.detectChanges();

    expect(byId(el, 'pipeline-metric-frame-wording-read-all-undo-undone')?.textContent?.trim()).toBe('37');
    expect(byId(el, 'pipeline-metric-frame-wording-read-all-undo-skipped')?.textContent?.trim()).toBe('2');
  });

  it('shows an error when the frame undo dry run fails', () => {
    const body = status();
    body.frame = frame([frameLane()]);
    const fixture = loaded(body);
    const el = fixture.nativeElement as HTMLElement;

    byId(el, 'pipeline-frame-wording-read-all-undo-preview-btn')!.click();
    http.expectOne((r) => r.url.endsWith('/Pipeline/frame/wording-read/*/undo')).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(byId(el, 'pipeline-frame-wording-read-all-undo-error')?.textContent).toContain('Unable to run the undo dry run.');
  });
});
