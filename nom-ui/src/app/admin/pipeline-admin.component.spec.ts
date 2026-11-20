import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { PipelineAdmin } from './pipeline-admin.component';
import { PipelineGate, PipelineLineFunnel, PipelineStatus } from '../core/models/pipeline.model';

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

function status(quietMinutesLeft: number | null = null): PipelineStatus {
  return {
    status: {
      generatedAt: '2026-11-20T12:00:00Z',
      computeMs: 2400,
      fromCache: false,
      recipes: {
        approved: 22842, waiting: 71185, needsRevision: 27754, pendingReview: 0, rejected: 724,
        waitingBlockedBy: { none: 1238, one: 29568, two: 20445, threeOrMore: 19934 },
        waitingPublicDomain: 53027, waitingScrapedWithoutOwnImage: 18155, waitingWithSourceProse: 567,
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
});
