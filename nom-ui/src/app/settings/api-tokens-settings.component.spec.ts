import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { ApiTokensSettings } from './api-tokens-settings.component';
import { ApiToken, CreatedApiToken } from '../core/models/api-token.model';

const unused: ApiToken = {
  id: '3',
  name: 'ci-bot',
  description: 'Nightly export',
  createdDate: '2026-10-01T12:00:00Z',
  lastUsedDate: null,
  isActive: true,
};

const used: ApiToken = { ...unused, id: '4', name: 'grafana', description: null, lastUsedDate: '2026-10-10T08:30:00Z' };

const created: CreatedApiToken = { ...unused, id: '9', name: 'new-bot', description: null, token: 'nom_0123456789abcdef' };

describe('ApiTokensSettings', () => {
  let http: HttpTestingController;

  function create(list: ApiToken[]) {
    TestBed.configureTestingModule({
      imports: [ApiTokensSettings],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ApiTokensSettings);
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/User/api-tokens')).flush(list);
    fixture.detectChanges();
    return fixture;
  }

  const q = (root: HTMLElement, id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  afterEach(() => http.verify());

  it('lists tokens with description, created date and last use', () => {
    const fixture = create([unused, used]);
    const el = fixture.nativeElement as HTMLElement;

    const items = el.querySelectorAll<HTMLElement>('[data-testid="api-token-item"]');
    expect(items.length).toBe(2);
    expect(q(items[0], 'api-token-description')!.textContent).toContain('Nightly export');
    expect(q(items[0], 'api-token-last-used')!.textContent).toContain('never');
    expect(q(items[0], 'api-token-status')!.textContent).toContain('Active');
    expect(q(items[1], 'api-token-description')).toBeNull();
    expect(q(items[1], 'api-token-last-used')!.textContent).not.toContain('never');
    expect(q(el, 'api-tokens-empty')).toBeNull();
  });

  it('shows the empty state', () => {
    const fixture = create([]);
    expect(q(fixture.nativeElement, 'api-tokens-empty')).toBeTruthy();
    expect(q(fixture.nativeElement, 'api-tokens-list')).toBeNull();
  });

  it('shows an error when the list fails to load', () => {
    TestBed.configureTestingModule({
      imports: [ApiTokensSettings],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ApiTokensSettings);
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/User/api-tokens')).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(q(fixture.nativeElement, 'api-tokens-error')!.textContent).toContain('Failed to load');
  });

  it('does not submit a name shorter than three characters', () => {
    const fixture = create([]);
    fixture.componentInstance.form.setValue({ name: ' ab ', description: '' });
    q(fixture.nativeElement, 'api-tokens-create-btn')!.click();
    fixture.detectChanges();

    http.expectNone((r) => r.method === 'POST');
    expect(q(fixture.nativeElement, 'api-tokens-name-error')).toBeTruthy();
  });

  it('creates a token, shows the secret once, and forgets it on dismiss', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const fixture = create([]);
    const el = fixture.nativeElement as HTMLElement;

    fixture.componentInstance.form.setValue({ name: ' new-bot ', description: '  ' });
    q(el, 'api-tokens-create-btn')!.click();
    const post = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/User/api-tokens'));
    expect(post.request.body).toEqual({ name: 'new-bot', description: null });
    post.flush(created);
    http.expectOne((r) => r.method === 'GET').flush([{ ...created, token: undefined }]);
    fixture.detectChanges();

    expect(q(el, 'api-token-secret')!.textContent).toContain('nom_0123456789abcdef');
    expect(q(el, 'api-token-secret-panel')!.textContent).toContain("won't be shown again");
    expect(q(el, 'api-token-secret-panel')!.textContent).toContain("your account's");
    expect(q(el, 'api-token-item')!.textContent).not.toContain('nom_0123456789abcdef');

    q(el, 'api-token-copy-btn')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(writeText).toHaveBeenCalledWith('nom_0123456789abcdef');
    expect(q(el, 'api-token-copied')).toBeTruthy();

    q(el, 'api-token-secret-dismiss-btn')!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.createdToken()).toBeNull();
    expect(q(el, 'api-token-secret')).toBeNull();
    expect(el.textContent).not.toContain('nom_0123456789abcdef');
  });

  it('asks for confirmation in the page before revoking, then refreshes', () => {
    const confirmSpy = vi.spyOn(window, 'confirm');
    const fixture = create([unused]);
    const el = fixture.nativeElement as HTMLElement;

    q(el, 'api-token-revoke-btn')!.click();
    fixture.detectChanges();
    http.expectNone((r) => r.method === 'DELETE');
    expect(confirmSpy).not.toHaveBeenCalled();

    q(el, 'api-token-revoke-cancel-btn')!.click();
    fixture.detectChanges();
    expect(q(el, 'api-token-revoke-confirm-btn')).toBeNull();

    q(el, 'api-token-revoke-btn')!.click();
    fixture.detectChanges();
    q(el, 'api-token-revoke-confirm-btn')!.click();
    http.expectOne((r) => r.method === 'DELETE' && r.url.endsWith('/User/api-tokens/3')).flush(null);
    http.expectOne((r) => r.method === 'GET').flush([]);
    fixture.detectChanges();
    expect(q(el, 'api-tokens-empty')).toBeTruthy();
  });

  it('reports a failed revoke and keeps the token listed', () => {
    const fixture = create([unused]);
    const el = fixture.nativeElement as HTMLElement;
    fixture.componentInstance.onStartRevoke(unused);
    fixture.componentInstance.onConfirmRevoke(unused);
    http.expectOne((r) => r.method === 'DELETE').flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(q(el, 'api-tokens-error')!.textContent).toContain('ci-bot');
    expect(q(el, 'api-token-item')).toBeTruthy();
  });
});
