import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ScrapingSources } from './scraping-sources.component';
import { ScrapingSourceModel, SourceCandidate, SourceCheck, SourceReview } from '../core/models/scraping-source.model';

const clearCheck: SourceCheck = {
  id: 11, domain: 'www.open.example', purpose: 'candidate', status: 'completed', startedUtc: '2026-11-20T10:00:00Z', finishedUtc: '2026-11-20T10:05:00Z',
  verdict: 'clear', conclusive: true, ruleVersion: 'source-check/2', policyVersion: 'source-admission/1',
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
  enabled: true, policyVersion: 'source-admission/1', scraperRuleVersion: 'source-check/2',
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

  function flush(body: SourceReview) {
    http.expectOne((r) => r.url.endsWith('/ScrapingSource/review')).flush(body);
    http.expectOne((r) => r.url.endsWith('/ScrapingSource') && r.params.get('status') === 'Pending').flush([]);
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
});
