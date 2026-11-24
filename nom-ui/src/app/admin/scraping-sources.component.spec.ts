import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ScrapingSources } from './scraping-sources.component';
import { RecipeRemovalReport, RecipeRemovalStatus, ScrapingSourceModel, SourceCandidate, SourceCheck, SourceReview, SourceTaken } from '../core/models/scraping-source.model';

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

  function flush(body: SourceReview, taken: SourceTaken[] = [], removal: RecipeRemovalStatus = { retired: 0, purged: 0, bySource: [], runs: [] }) {
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/review')).flush(body);
    http.expectOne((r) => r.url.endsWith('/ScrapingSource') && r.params.get('status') === 'Pending').flush([]);
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/taken')).flush(taken);
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/removal')).flush(removal);
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

  it('shows what a discovery run excluded, a seed NOM also holds and the note of the exclusion list', () => {
    const el = create(review({
      runs: [{
        id: 2, startedUtc: '2026-11-22T10:00:00Z', status: 'completed', seeds: 'a.example', candidates: 1, leads: 0, pagesFetched: 9,
        excludeSent: 12, excludeLeftOut: 3, candidatesExcluded: 2,
        candidatesExcludedHosts: [
          { domain: 'forbidden.example', discoveredVia: 'a.example', excludedBy: 'forbidden.example' },
          { domain: 'blog.forbidden.example', discoveredVia: 'a.example', excludedBy: 'forbidden.example' },
        ],
        seedsExcluded: 'blog.split.example', excludeNote: 'NOM fault: approved seed(s) blog.split.example are also held as excluded',
      }],
    })).nativeElement as HTMLElement;

    const excluded = byId(el, 'scraping-sources-run-excluded')!;
    expect(excluded.textContent).toContain('excluded 12 held hosts');
    expect(excluded.textContent).toContain('3 left out over the limit');
    expect(excluded.textContent).toContain('forbidden.example, blog.forbidden.example');
    expect(byId(el, 'scraping-sources-run-seeds-excluded')?.textContent).toContain('blog.split.example');
    expect(byId(el, 'scraping-sources-run-exclude-note')?.textContent).toContain('NOM fault');
  });

  it('shows no exclusion line for a run that sent no list', () => {
    const el = create(review({ runs: [{ id: 1, startedUtc: '2026-11-20T10:00:00Z', status: 'skipped', seeds: '', candidates: 0, leads: 0, pagesFetched: 0 }] })).nativeElement as HTMLElement;

    expect(byId(el, 'scraping-sources-run-excluded')).toBeNull();
    expect(byId(el, 'scraping-sources-run-seeds-excluded')).toBeNull();
    expect(byId(el, 'scraping-sources-run-exclude-note')).toBeNull();
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

  it('shows which kind of check ran last, flags a newer check version, and offers a terms-only check of a source with no class', () => {
    const unclassed: ScrapingSourceModel = {
      ...blog, rightsClass: null, recheckSkipped: null,
      check: { ...blog.check!, mode: 'terms-only', conclusive: false, verdict: 'not-clear' },
    };
    const newer: ScrapingSourceModel = {
      ...blog, id: 10, domain: 'www.newer.example', rightsClass: 'facts-only', effectiveRightsClass: 'facts-only', recheckSkipped: null,
      check: { ...blog.check!, id: 13, mode: 'full', conclusive: false, ruleVersion: 'source-check/6', newerRuleVersion: true },
    };
    const el = create(review({ approved: [unclassed, newer] })).nativeElement as HTMLElement;
    const items = Array.from(el.querySelectorAll('[data-testid="scraping-source-item"]')) as HTMLElement[];

    expect(byId(items[0], 'scraping-source-last-check-kind')?.textContent).toContain('Last check: terms-only');
    expect(byId(items[0], 'scraping-source-inconclusive')).toBeNull();
    expect(byId(items[0], 'scraping-source-terms-check-btn')).not.toBeNull();
    expect(byId(items[1], 'scraping-source-last-check-kind')?.textContent).toContain('Last check: full');
    expect(byId(items[1], 'scraping-source-newer-check-version')?.textContent).toContain('source-check/6');
    expect(byId(items[1], 'scraping-source-terms-check-btn')).toBeNull();
  });

  it('shows before a class what a facts-only class would withhold, and after it what its class withholds and from which check', () => {
    const before: ScrapingSourceModel = {
      ...blog, rightsClass: null, recheckSkipped: null,
      prose: { sourceId: 9, domain: 'www.blog.example', rightsClass: null, classCheckId: null, classFromTermsOnly: false, recipes: 3274, approved: 600, withheld: 0, withheldApproved: 0, wouldWithhold: 3270, wouldWithholdApproved: 598 },
    };
    const after: ScrapingSourceModel = {
      ...blog, id: 10, domain: 'www.after.example', rightsClass: 'facts-only', effectiveRightsClass: 'facts-only', recheckSkipped: null,
      prose: { sourceId: 10, domain: 'www.after.example', rightsClass: 'facts-only', classCheckId: 12, classFromTermsOnly: true, recipes: 920, approved: 100, withheld: 920, withheldApproved: 100, wouldWithhold: 0, wouldWithholdApproved: 0 },
    };
    const el = create(review({ approved: [before, after] })).nativeElement as HTMLElement;
    const items = Array.from(el.querySelectorAll('[data-testid="scraping-source-item"]')) as HTMLElement[];

    expect(byId(items[0], 'scraping-source-prose-figures')?.textContent).toContain('3,274 recipes (600 approved)');
    expect(byId(items[0], 'scraping-source-prose-would-withhold')?.textContent).toContain('3,270 recipes (598 approved)');
    expect(byId(items[0], 'scraping-source-prose-withheld-by-class')).toBeNull();
    expect(byId(items[1], 'scraping-source-prose-figures')?.textContent).toContain('prose withheld 920 (100 approved)');
    expect(byId(items[1], 'scraping-source-prose-withheld-by-class')?.textContent).toContain('terms-only check 12');
    expect(byId(items[1], 'scraping-source-prose-would-withhold')).toBeNull();
  });

  it('lists a re-derived terms-only report that would withhold prose as such', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;

    (byId(el, 'scraping-sources-rights-derive-dry-btn') as HTMLButtonElement).click();
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/derive')).flush({
      applied: false, ruleVersion: 'source-rights/4', stamp: '2026-11-23T00:00:00Z', sources: 1, changed: 1, inconclusive: 0,
      results: [{ sourceId: 9, domain: 'www.blog.example', oldClass: null, newClass: 'facts-only', oldAiReserved: false, newAiReserved: false, reason: 'nothing grants reuse', quote: 'All rights reserved.', conclusive: true, changed: true, recipesRestamped: 0, recipesWithheld: 0, recipesReleased: 0, termsOnly: true, wouldWithhold: 1234, wouldWithholdApproved: 218 }],
    });
    fixture.detectChanges();

    expect(byId(el, 'scraping-sources-rights-run-withhold')?.textContent).toContain('a terms-only report that withholds the prose of 1,234 recipes (218 approved)');
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

  it('retires a forbidden source\'s recipes after a dry run, undoes the run by its stamp, and purges only after the backup is confirmed', () => {
    const taken: SourceTaken[] = [
      { sourceId: 9, domain: 'www.blog.example', rightsClass: 'forbidden', rightsQuote: 'No robots.', recipes: 1159, recipesShown: 1159, recipesWithheld: false, removable: true, retired: 0, purged: 0 },
      { sourceId: 10, domain: 'www.ask.example', rightsClass: 'permission-needed', rightsQuote: null, recipes: 875, recipesShown: 875, recipesWithheld: false, removable: false, retired: 0, purged: 0 },
    ];
    const report = (stage: string, applied: boolean, extra: Partial<RecipeRemovalReport> = {}): RecipeRemovalReport => ({
      stage, applied, stamp: '2026-11-24T12:00:00Z', sourceId: 9, runId: applied ? 4 : null, resumed: false, refused: null, recipes: 1159, skipped: 0, alreadyDone: 0,
      skippedByReason: {}, recipeIds: [],
      sources: [{ sourceId: 9, domain: 'www.blog.example', rightsClass: 'forbidden', rightsQuote: 'No robots.', recipes: 1159, alreadyDeleted: 0, approved: 106, approvedByPerson: 2, approvedUnrecorded: 1, editedByPerson: 3, inMealPlans: 4, favourited: 5, inCookbooks: 6, inShoppingLists: 7 }],
      targets: [], warning: null, ...extra,
    });
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

    const items = el.querySelectorAll('[data-testid="scraping-sources-taken-item"]');
    expect(items[0].querySelector('[data-testid="scraping-sources-taken-retire-btn"]')).not.toBeNull();
    expect(items[1].querySelector('[data-testid="scraping-sources-taken-retire-btn"]')).toBeNull();

    (items[0].querySelector('[data-testid="scraping-sources-taken-retire-dry-btn"]') as HTMLButtonElement).click();
    const dry = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/removal/retire'));
    expect(dry.request.params.get('apply')).toBe('false');
    expect(dry.request.params.get('sourceId')).toBe('9');
    dry.flush(report('retire', false));
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-removal-summary')?.textContent).toContain('retire dry run: 1,159 recipe(s)');
    expect(byId(el, 'scraping-sources-removal-source-item')?.textContent).toContain('in 4 meal plan(s), 5 favourite(s)');
    expect(byId(el, 'scraping-sources-removal-source-item')?.textContent).toContain('2 by a person, 1 approver not recorded');

    (items[0].querySelector('[data-testid="scraping-sources-taken-retire-btn"]') as HTMLButtonElement).click();
    const apply = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/removal/retire'));
    expect(apply.request.params.get('apply')).toBe('true');
    apply.flush(report('retire', true));
    const run = { id: 4, kind: 'retire', stamp: '2026-11-24T12:00:00Z', sourceId: 9, personId: 7, planned: 1159, done: 1159, skipped: 0, startedAtUtc: '2026-11-24T12:00:00Z', finishedAtUtc: '2026-11-24T12:01:00Z', undoable: 1159 };
    flush(review(), [{ ...taken[0], recipes: 0, retired: 1159 }, taken[1]], { retired: 1159, purged: 0, bySource: [], runs: [run] });
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-taken-retired')?.textContent).toContain('1,159 retired');

    (byId(el, 'scraping-sources-removal-undo-btn') as HTMLButtonElement).click();
    const undo = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/removal/undo'));
    expect(undo.request.params.get('stamp')).toBe('2026-11-24T12:00:00Z');
    expect(undo.request.params.get('apply')).toBe('true');
    undo.flush(report('retire-undo', true));
    flush(review(), taken);
    fixture.detectChanges();

    expect((byId(el, 'scraping-sources-removal-purge-btn') as HTMLButtonElement).disabled).toBe(true);
    (byId(el, 'scraping-sources-removal-purge-dry-btn') as HTMLButtonElement).click();
    const purgeDry = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/removal/purge'));
    expect(purgeDry.request.params.get('apply')).toBe('false');
    purgeDry.flush(report('purge', false, { warning: 'It cannot be undone.', targets: [{ table: 'recipe.RecipeStep', columns: 'Description, Summary (whole rows)', action: 'deleted', rows: 9000 }] }));
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-removal-warning')?.textContent).toContain('cannot be undone');
    expect(byId(el, 'scraping-sources-removal-target-item')?.textContent).toContain('recipe.RecipeStep');

    (byId(el, 'scraping-sources-removal-backup-check') as HTMLInputElement).click();
    fixture.detectChanges();
    (byId(el, 'scraping-sources-removal-purge-btn') as HTMLButtonElement).click();
    const purge = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/removal/purge'));
    expect(purge.request.params.get('apply')).toBe('true');
    expect(purge.request.params.get('backupTaken')).toBe('true');
    expect(purge.request.params.has('sourceId')).toBe(false);
    purge.flush(report('purge', true));
    flush(review(), taken);
  });

  it('resumes a cut-off retire run with its own scope', () => {
    TestBed.configureTestingModule({
      imports: [ScrapingSources],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ScrapingSources);
    fixture.detectChanges();
    const run = { id: 5, kind: 'retire', stamp: '2026-11-24T12:00:00Z', sourceId: 9, personId: 7, planned: 91, done: 40, skipped: 0, startedAtUtc: '2026-11-24T12:00:00Z', finishedAtUtc: null, undoable: 40 };
    flush(review(), [], { retired: 40, purged: 0, bySource: [], runs: [run], unfinished: run });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(byId(el, 'scraping-sources-removal-unfinished')?.textContent).toContain('a run of another scope is refused');
    (byId(el, 'scraping-sources-removal-resume-btn') as HTMLButtonElement).click();
    const resume = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/removal/retire'));
    expect(resume.request.params.get('sourceId')).toBe('9');
    expect(resume.request.params.get('apply')).toBe('true');
    resume.flush({ stage: 'retire', applied: true, stamp: run.stamp, sourceId: 9, runId: 5, resumed: true, refused: null, recipes: 51, skipped: 0, alreadyDone: 40, skippedByReason: {}, recipeIds: [], sources: [], targets: [], warning: null });
    flush(review(), []);
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-removal-summary')?.textContent).toContain('retire applied (resumed): 51 recipe(s)');
  });

  it('marks a class recorded by hand beside one derived by rule, offers no terms-only check for it, and withdraws it from its entry', () => {
    const record = {
      id: 3, host: 'budgetbytes.com', rightsClass: 'forbidden' as const, quote: 'you agree that you will not scrape any web pages',
      quoteUrl: 'https://www.budgetbytes.com/terms-conditions/', readOn: '2026-11-21T00:00:00Z', recordedByPersonId: 7, recordedAtUtc: '2026-11-24T10:00:00Z',
      journalEntryId: 90, sources: ['www.budgetbytes.com'], candidate: null,
    };
    const byHand = {
      ...blog, id: 21, domain: 'www.budgetbytes.com', rightsClass: 'forbidden' as const, effectiveRightsClass: 'forbidden' as const, rightsRecordId: 3, rightsRecord: record,
      rightsQuote: record.quote, recheckSkipped: 'The weekly re-check skips www.budgetbytes.com: it is forbidden by a class recorded by hand.',
    };
    const derived = { ...blog, rightsClass: 'facts-only' as const, effectiveRightsClass: 'facts-only' as const };
    const fixture = create(review({ approved: [derived, byHand], rightsRecords: [record] }));
    const el = fixture.nativeElement as HTMLElement;

    const items = el.querySelectorAll('[data-testid="scraping-source-item"]');
    expect(items[0].querySelector('[data-testid="scraping-source-rights-by-lane"]')).not.toBeNull();
    expect(items[0].querySelector('[data-testid="scraping-source-rights-by-hand"]')).toBeNull();
    expect(items[1].querySelector('[data-testid="scraping-source-rights-by-hand"]')?.textContent).toContain('Recorded by hand by person 7');
    expect(items[1].querySelector('[data-testid="scraping-source-rights-by-hand"]')?.textContent).toContain('nothing fetches the site');
    expect(items[1].querySelector('[data-testid="scraping-source-terms-check-btn"]')).toBeNull();
    expect(byId(el, 'scraping-sources-by-hand-item')?.textContent).toContain('budgetbytes.com (forbidden, by hand)');
    expect(byId(el, 'scraping-sources-by-hand-link')?.getAttribute('href')).toBe('https://www.budgetbytes.com/terms-conditions/');

    (byId(el, 'scraping-sources-by-hand-undo-btn') as HTMLButtonElement).click();
    const undo = http.expectOne((r) => r.url.endsWith('/ScrapingSource/journal/90/undo'));
    expect(undo.request.method).toBe('POST');
    undo.flush(blog);
    flush(review());
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-by-hand-empty')).not.toBeNull();
  });

  it('records a class by hand only after a dry run shows what it holds, and shows the rule refusing one', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance;
    component.byHandForm.setValue({
      host: 'www.eatingwell.com', rightsClass: 'forbidden', quote: 'Use of any crawler to scrape the content on this website is prohibited.',
      quoteUrl: 'https://www.eatingwell.com/robots.txt', readOn: '2026-11-21',
    });
    fixture.detectChanges();

    (byId(el, 'scraping-sources-by-hand-dry-btn') as HTMLButtonElement).click();
    const dry = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/by-hand'));
    expect(dry.request.params.get('apply')).toBe('false');
    expect(dry.request.body.host).toBe('www.eatingwell.com');
    dry.flush({ applied: false, host: 'eatingwell.com', rightsClass: 'forbidden', alreadyRecorded: false, excluded: true, candidate: 'eatingwell.com', candidateClass: null,
      sources: [{ sourceId: 4, domain: 'www.eatingwell.com', oldClass: 'permission-needed', newClass: 'forbidden', recipes: 0 }] });
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-by-hand-result')?.textContent).toContain('would be recorded');
    expect(byId(el, 'scraping-sources-by-hand-result')?.textContent).toContain('www.eatingwell.com permission-needed → forbidden');
    expect(byId(el, 'scraping-sources-by-hand-result')?.textContent).toContain('excluded from discovery, never fetched');

    (byId(el, 'scraping-sources-by-hand-submit-btn') as HTMLButtonElement).click();
    const apply = http.expectOne((r) => r.url.endsWith('/ScrapingSource/rights/by-hand'));
    expect(apply.request.params.get('apply')).toBe('true');
    apply.flush({ applied: false, host: 'eatingwell.com', rightsClass: 'forbidden', alreadyRecorded: false, excluded: false, sources: [],
      refused: 'a class recorded by hand may be as strict or stricter, never laxer' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-by-hand-error')?.textContent).toContain('never laxer');
  });

  it('shows the sites that refused a run on the run and in a list, and releases a held one after an admin looked', () => {
    const fixture = create(review({
      runs: [{ id: 2, startedUtc: '2026-11-24T00:19:52Z', status: 'completed', seeds: 'acozykitchen.com', candidates: 0, leads: 0, pagesFetched: 40, refusingHosts: 2,
        refusingHostsList: [{ domain: 'booksamillion.com', url: 'https://booksamillion.com/robots.txt', httpStatus: 403, botChallenge: false }, { domain: 'bookshop.org', url: 'https://bookshop.org/', httpStatus: 403, botChallenge: true }] }],
      refusingHosts: [
        { id: 1, host: 'booksamillion.com', firstRefusedUtc: '2026-11-24T00:20:00Z', lastRefusedUtc: '2026-11-24T00:20:00Z', timesRefused: 1, lastUrl: 'https://booksamillion.com/robots.txt', lastHttpStatus: 403, lastBotChallenge: false, held: true },
        { id: 2, host: 'link.example', firstRefusedUtc: '2026-11-20T00:20:00Z', lastRefusedUtc: '2026-11-20T00:20:00Z', timesRefused: 1, lastHttpStatus: 403, lastBotChallenge: false, held: false, releasedAtUtc: '2026-11-21T00:00:00Z', releasedNote: 'looked' },
      ],
    }));
    const el = fixture.nativeElement as HTMLElement;

    expect(byId(el, 'scraping-sources-run-refusing')?.textContent).toContain('booksamillion.com (HTTP 403), bookshop.org (bot challenge)');
    const items = el.querySelectorAll('[data-testid="scraping-sources-refusing-item"]');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('held');
    expect(items[1].querySelector('[data-testid="scraping-sources-refusing-release-btn"]')).toBeNull();

    (items[0].querySelector('[data-testid="scraping-sources-refusing-release-btn"]') as HTMLButtonElement).click();
    const release = http.expectOne((r) => r.url.endsWith('/ScrapingSource/refusing/1/release'));
    expect(release.request.method).toBe('POST');
    release.flush({ id: 1, host: 'booksamillion.com', held: false });
    flush(review());
    fixture.detectChanges();
    expect(byId(el, 'scraping-sources-refusing-empty')).not.toBeNull();
  });
});
