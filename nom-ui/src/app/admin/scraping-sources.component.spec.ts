import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ScrapingSources } from './scraping-sources.component';
import { ScrapingSourceModel, SourceCandidate, SourceCheck, SourceReview, SourceTaken } from '../core/models/scraping-source.model';

const clearCheck: SourceCheck = {
  id: 11, domain: 'www.open.example', purpose: 'candidate', status: 'completed', startedUtc: '2026-11-20T10:00:00Z', finishedUtc: '2026-11-20T10:05:00Z',
  verdict: 'clear', conclusive: true, ruleVersion: 'source-check/4', policyVersion: 'source-admission/2',
  reasons: [{ code: 'licence', outcome: 'pass', detail: '20 of 20 pages carry cc0' }],
  facts: {
    licenceKind: 'cc0', licenceBasis: 'per-page',
    licenceStatements: [{ kind: 'cc0', text: 'Recipes on this site are CC0.', foundAt: 'https://open.example/terms', where: 'page-text' }],
    sampleLicences: { cc0: 20 }, termsPages: [{ url: 'https://open.example/terms', outcome: 'read' }], prohibitions: [],
    robotsUrl: 'https://open.example/robots.txt', robotsStatus: 'found', generalAllowsHomepage: true, generalSamplePathsDisallowed: 0,
    nomBotAllowsHomepage: true, nomBotSamplePathsDisallowed: 0, aiCrawlersBlocked: [], aiCrawlersAllowed: ['GPTBot'], crawlDelaySeconds: 20,
    recipeUrlCount: 640, sampleRequested: 20, sampleFetched: 20, sampleWithRecipe: 20, sampleComplete: 20, sampleEnglish: 20, ingredientLines: 180, linesWithQuantity: 171,
  },
};

const candidate = (overrides: Partial<SourceCandidate> = {}): SourceCandidate => ({
  id: 5, domain: 'open.example', evidenceUrl: 'https://www.open.example/soup/', foundVia: 'seed.example', signal: 'RecipeJsonLd',
  firstSeenUtc: '2026-11-19T00:00:00Z', lastSeenUtc: '2026-11-20T00:00:00Z', timesSeen: 3, state: 'clear', check: clearCheck, canApprove: true,
  ...overrides,
});

const blog: ScrapingSourceModel = {
  id: 9, domain: 'www.blog.example', status: 'Approved', kind: 'web', createdDate: '2026-09-01T00:00:00Z', reviewedDate: '2026-09-02T00:00:00Z',
  checkVerdict: 'not-clear', checkedAtUtc: '2026-11-20T09:00:00Z',
  check: { ...clearCheck, id: 12, verdict: 'not-clear', reasons: [{ code: 'robots-ai', outcome: 'fail', detail: 'GPTBot blocked' }, { code: 'https', outcome: 'pass' }], facts: null },
};

const review = (overrides: Partial<SourceReview> = {}): SourceReview => ({
  enabled: true, policyVersion: 'source-admission/2', scraperRuleVersion: 'source-check/4',
  queue: [candidate()],
  notClear: [candidate({ id: 6, domain: 'closed.example', state: 'not-clear', canApprove: false, check: { ...clearCheck, verdict: 'not-clear', reasons: [{ code: 'terms', outcome: 'fail', detail: 'scraping prohibited' }] } })],
  waiting: [], leads: 4, approved: [blog], hand: [], rejected: [],
  runs: [{ id: 1, startedUtc: '2026-11-20T10:00:00Z', status: 'failed', error: 'lost', seeds: 'a.example', candidates: 1, leads: 0, pagesFetched: 3 }],
  failedChecks: [],
  journal: [{ id: 77, sourceId: 9, domain: 'www.blog.example', changeType: 'SourceHarvestPause', field: 'Harvest', oldValue: 'running', newValue: 'paused | x', timestamp: '2026-11-20T11:00:00Z', changedByPersonId: 7, canUndo: true }],
  ...overrides,
});

describe('ScrapingSources', () => {
  let http: HttpTestingController;

  function create(body: SourceReview = review()) {
    TestBed.configureTestingModule({
      imports: [ScrapingSources],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ScrapingSources);
    fixture.detectChanges();
    flush(body);
    fixture.detectChanges();
    return fixture;
  }

  function flush(body: SourceReview, taken: SourceTaken[] = []) {
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/review')).flush(body);
    http.expectOne((r) => r.url.endsWith('/ScrapingSource') && r.params.get('status') === 'Pending').flush([]);
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/taken')).flush(taken);
  }

  const byId = (el: HTMLElement, id: string) => el.querySelector(`[data-testid="${id}"]`);

  afterEach(() => http.verify());

  it('queues only clear candidates, each with the facts that make it plain and one approve button', () => {
    const el = create().nativeElement as HTMLElement;

    const cards = el.querySelectorAll('[data-testid="scraping-source-candidate-card"]');
    expect(cards.length).toBe(1);
    expect(cards[0].textContent).toContain('open.example');
    expect(byId(el, 'scraping-source-fact-statement')?.textContent).toContain('Recipes on this site are CC0.');
    expect(byId(el, 'scraping-source-fact-statement-link')?.getAttribute('href')).toBe('https://open.example/terms');
    expect(byId(el, 'scraping-source-fact-crawl-delay')?.textContent).toContain('20 s');
    expect(byId(el, 'scraping-source-fact-robots')?.textContent).toContain('AI crawlers blocked: none');
    expect(byId(el, 'scraping-source-fact-yield')?.textContent).toContain('171 of 180 lines');
    expect(el.querySelectorAll('[data-testid="scraping-source-candidate-approve-btn"]').length).toBe(1);

    const notClear = el.querySelectorAll('[data-testid="scraping-source-not-clear-item"]');
    expect(notClear.length).toBe(1);
    expect(notClear[0].textContent).toContain('terms (fail): scraping prohibited');
    expect(notClear[0].querySelector('button')).toBeNull();
  });

  it('approves a queued candidate with one click and reloads', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;

    (byId(el, 'scraping-source-candidate-approve-btn') as HTMLButtonElement).click();
    const call = http.expectOne((r) => r.url.endsWith('/ScrapingSource/candidates/5/approve'));
    expect(call.request.method).toBe('POST');
    call.flush({ ...blog, id: 10, domain: 'www.open.example', checkVerdict: 'clear' });
    flush(review({ queue: [] }));
    fixture.detectChanges();

    expect(byId(el, 'scraping-sources-queue-empty')).not.toBeNull();
  });

  it('shows each approved source with its re-check verdict and reasons, and withholds its recipes only after a dry run', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;

    expect(byId(el, 'scraping-source-verdict')?.textContent?.trim()).toBe('not-clear');
    const reasons = el.querySelectorAll('[data-testid="scraping-source-reason"]');
    expect(reasons.length).toBe(1);
    expect(reasons[0].textContent).toContain('robots-ai (fail): GPTBot blocked');

    (byId(el, 'scraping-source-withhold-btn') as HTMLButtonElement).click();
    const dry = http.expectOne((r) => r.url.endsWith('/ScrapingSource/9/withhold-recipes'));
    expect(dry.request.params.get('apply')).toBe('false');
    dry.flush({ applied: false, recipes: 2209 });
    fixture.detectChanges();
    expect(byId(el, 'scraping-source-action-prompt')?.textContent).toContain('2,209 recipes');

    (byId(el, 'scraping-source-action-confirm-btn') as HTMLButtonElement).click();
    const apply = http.expectOne((r) => r.url.endsWith('/ScrapingSource/9/withhold-recipes'));
    expect(apply.request.params.get('apply')).toBe('true');
    apply.flush({ applied: true, recipes: 2209 });
    flush(review());
  });

  it('pauses harvesting after a confirmation and undoes a journal entry', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;

    (byId(el, 'scraping-source-pause-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    (byId(el, 'scraping-source-action-confirm-btn') as HTMLButtonElement).click();
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/9/pause-harvest')).flush(blog);
    flush(review());
    fixture.detectChanges();

    (byId(el, 'scraping-sources-journal-undo-btn') as HTMLButtonElement).click();
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/journal/77/undo')).flush(blog);
    flush(review());
  });

  it('shows a failed discovery run as failed and says when nothing is queued', () => {
    const el = create(review({ queue: [], runs: [{ id: 1, startedUtc: '2026-11-20T10:00:00Z', status: 'failed', error: 'lost: gone', seeds: 'a.example', candidates: 0, leads: 0, pagesFetched: 0 }] })).nativeElement as HTMLElement;

    expect(byId(el, 'scraping-sources-queue-empty')).not.toBeNull();
    const run = byId(el, 'scraping-sources-run-item')!;
    expect(run.classList).toContain('nom-scraping-sources__warn');
    expect(run.textContent).toContain('lost: gone');
  });

  it('records a hand source with its licence facts', () => {
    const fixture = create(review({ hand: [] }));
    const el = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance;

    component.handForm.setValue({
      domain: 'www.cancer.gov', title: 'NCI Down Home Healthy Cooking', licenceKind: 'public-domain',
      licenceStatement: 'all text within NCI products is free of copyright', licenceFoundAt: 'https://www.cancer.gov/policies/copyright-reuse',
    });
    (byId(el, 'scraping-sources-hand-submit-btn') as HTMLButtonElement).click();
    const call = http.expectOne((r) => r.url.endsWith('/ScrapingSource/hand'));
    expect(call.request.body.licenceKind).toBe('public-domain');
    call.flush({ ...blog, id: 20, kind: 'hand' });
    flush(review());
  });

  it('shows the rights class of each source with the deciding quote and records a written permission', () => {
    const forbidden: ScrapingSourceModel = {
      ...blog, rightsClass: 'forbidden', effectiveRightsClass: 'forbidden', rightsQuote: 'You agree not to use any robot.', rightsQuoteUrl: 'https://www.blog.example/terms',
      aiReserved: true, aiReservedQuote: 'robots.txt blocks named AI crawlers: GPTBot', aiReservedQuoteUrl: 'https://www.blog.example/robots.txt',
      mayFetch: false, fetchRefusal: 'www.blog.example is forbidden: nothing is fetched from it without a recorded written permission.',
    };
    const fixture = create(review({ approved: [forbidden] }));
    const el = fixture.nativeElement as HTMLElement;

    expect(byId(el, 'scraping-source-rights-class')?.textContent?.trim()).toBe('forbidden');
    expect(byId(el, 'scraping-source-rights-quote')?.textContent).toContain('You agree not to use any robot.');
    expect(byId(el, 'scraping-source-rights-quote-link')?.getAttribute('href')).toBe('https://www.blog.example/terms');
    expect(byId(el, 'scraping-source-ai-reserved')?.textContent).toContain('No model reads its method or description');
    expect(byId(el, 'scraping-source-fetch-refusal')?.textContent).toContain('nothing is fetched');
    expect(byId(el, 'scraping-source-facts-only-approve-btn')).toBeNull();

    (byId(el, 'scraping-source-permission-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const set = (id: string, value: string) => {
      const input = byId(el, id) as HTMLInputElement | HTMLTextAreaElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    set('scraping-source-permission-by', 'The editor');
    set('scraping-source-permission-on', '2026-11-19');
    set('scraping-source-permission-text', 'You may use our recipes in NOM.');
    fixture.detectChanges();
    (byId(el, 'scraping-source-permission-submit-btn') as HTMLButtonElement).click();

    const call = http.expectOne((r) => r.url.endsWith('/ScrapingSource/9/permission'));
    expect(call.request.body).toEqual({ grantedBy: 'The editor', grantedOn: '2026-11-19', text: 'You may use our recipes in NOM.' });
    call.flush({ ...forbidden, effectiveRightsClass: 'clear', permissionRecordedAtUtc: '2026-11-21T00:00:00Z' });
    flush(review({ approved: [{ ...forbidden, effectiveRightsClass: 'clear', permissionGrantedBy: 'The editor', permissionGrantedOn: '2026-11-19', permissionText: 'You may use our recipes in NOM.', permissionRecordedAtUtc: '2026-11-21T00:00:00Z', fetchRefusal: null }] }));
    fixture.detectChanges();

    expect(byId(el, 'scraping-source-permission')?.textContent).toContain('The editor');
    expect(byId(el, 'scraping-source-permission-btn')).toBeNull();
  });

  it('approves facts-only use of a facts-only source with one click', () => {
    const facts: ScrapingSourceModel = { ...blog, rightsClass: 'facts-only', effectiveRightsClass: 'facts-only', rightsQuote: 'All rights reserved.', rightsQuoteUrl: 'https://www.blog.example/' };
    const fixture = create(review({ approved: [facts] }));
    const el = fixture.nativeElement as HTMLElement;

    (byId(el, 'scraping-source-facts-only-approve-btn') as HTMLButtonElement).click();
    const call = http.expectOne((r) => r.url.endsWith('/ScrapingSource/9/facts-only-use'));
    expect(call.request.params.get('approve')).toBe('true');
    call.flush({ ...facts, factsOnlyApprovedAt: '2026-11-21T00:00:00Z' });
    flush(review({ approved: [{ ...facts, factsOnlyApprovedAt: '2026-11-21T00:00:00Z' }] }));
    fixture.detectChanges();

    expect(byId(el, 'scraping-source-facts-only-approved')).not.toBeNull();
    expect(byId(el, 'scraping-source-facts-only-withdraw-btn')).not.toBeNull();
  });

  it('says the weekly re-check skips a forbidden source, shows what refused NomBot, and starts a terms-only check by hand', () => {
    const forbidden: ScrapingSourceModel = {
      ...blog, rightsClass: 'forbidden', effectiveRightsClass: 'forbidden', rightsQuote: 'You agree not to use any robot.', rightsQuoteUrl: 'https://www.blog.example/terms',
      recheckSkipped: 'The weekly re-check skips www.blog.example: it is forbidden, so no automatic check fetches it.',
      check: { ...blog.check!, facts: { ...clearCheck.facts!, refusedPages: ['https://www.blog.example/sitemap.xml'], samplesRefused: 2, homepageLinks: 0 } },
    };
    const fixture = create(review({ approved: [forbidden] }));
    const el = fixture.nativeElement as HTMLElement;

    expect(byId(el, 'scraping-source-recheck-skipped')?.textContent).toContain('no automatic check fetches it');
    const access = byId(el, 'scraping-source-access')?.textContent ?? '';
    expect(access).toContain('refused NomBot: https://www.blog.example/sitemap.xml');
    expect(access).toContain('2 sampled recipe page(s) refused NomBot');
    expect(access).toContain('carries no links');

    (byId(el, 'scraping-source-terms-check-btn') as HTMLButtonElement).click();
    const call = http.expectOne((r) => r.url.endsWith('/ScrapingSource/9/terms-check'));
    expect(call.request.method).toBe('POST');
    const waiting = { ...forbidden, termsCheck: { ...clearCheck, id: 30, purpose: 'terms-only', status: 'queued', verdict: null, facts: null } };
    call.flush(waiting);
    flush(review({ approved: [waiting] }));
    fixture.detectChanges();

    expect(byId(el, 'scraping-source-terms-check')?.textContent).toContain('waiting for the source lane');
    expect((byId(el, 'scraping-source-terms-check-btn') as HTMLButtonElement).disabled).toBe(true);
  });

  it('offers no terms-only check for a source the weekly re-check still reads', () => {
    const facts: ScrapingSourceModel = { ...blog, rightsClass: 'facts-only', effectiveRightsClass: 'facts-only', recheckSkipped: null };
    const el = create(review({ approved: [facts] })).nativeElement as HTMLElement;

    expect(byId(el, 'scraping-source-recheck-skipped')).toBeNull();
    expect(byId(el, 'scraping-source-terms-check-btn')).toBeNull();
  });

  it('derives the classes in a dry run first, runs a recipe step and undoes it by its stamp, and counts recipes taken from barred sources', () => {
    const taken: SourceTaken[] = [{ sourceId: 9, domain: 'www.blog.example', rightsClass: 'forbidden', rightsQuote: 'No robots.', recipes: 1159, recipesShown: 1159, recipesWithheld: false }];
    TestBed.configureTestingModule({
      imports: [ScrapingSources],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ScrapingSources);
    fixture.detectChanges();
    flush(review(), taken);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(byId(el, 'scraping-sources-taken-item')?.textContent).toContain('1,159 recipe(s)');

    (byId(el, 'scraping-sources-rights-derive-dry-btn') as HTMLButtonElement).click();
    const dry = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/derive'));
    expect(dry.request.params.get('apply')).toBe('false');
    dry.flush({
      applied: false, ruleVersion: 'source-rights/1', stamp: '2026-11-21T00:00:00Z', sources: 1, changed: 1, inconclusive: 0,
      results: [{ sourceId: 9, domain: 'www.blog.example', oldClass: null, newClass: 'forbidden', oldAiReserved: false, newAiReserved: true, reason: 'the site\'s words forbid automated access', quote: 'No robots.', conclusive: true, changed: true, recipesRestamped: 0, recipesWithheld: 0, recipesReleased: 0 }],
    });
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-rights-run-item')?.textContent).toContain('none → forbidden');
    expect(byId(el, 'scraping-sources-rights-derive-apply-btn')).not.toBeNull();

    (byId(el, 'scraping-sources-titles-apply-btn') as HTMLButtonElement).click();
    const titles = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/titles'));
    expect(titles.request.params.get('apply')).toBe('true');
    titles.flush({ step: 'titles', applied: true, stamp: '2026-11-21T01:02:03Z', examined: 3, changed: 1, skipped: 1, skippedByReason: { 'a person edited the recipe': 1 }, changes: [{ recipeId: 4, old: 'The Ultimate Grilled Cheese', new: 'Grilled Cheese' }] });
    flush(review(), taken);
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-step-summary')?.textContent).toContain('1 a person edited the recipe');
    expect(byId(el, 'scraping-sources-step-item')?.textContent).toContain('Grilled Cheese');

    (byId(el, 'scraping-sources-step-undo-btn') as HTMLButtonElement).click();
    const undo = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/undo'));
    expect(undo.request.params.get('step')).toBe('titles');
    expect(undo.request.params.get('stamp')).toBe('2026-11-21T01:02:03Z');
    undo.flush({ step: 'titles-undo', applied: true, stamp: '2026-11-21T02:00:00Z', examined: 1, changed: 1, skipped: 0, skippedByReason: {}, changes: [] });
    flush(review(), taken);
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-step-undo-btn')).toBeNull();
  });
});
