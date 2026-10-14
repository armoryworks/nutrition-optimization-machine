import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { KitchenToolsAdmin } from './kitchen-tools-admin.component';
import { KitchenToolOption, KitchenToolSuggestion } from '../core/models/kitchen-tools.model';

const tools: KitchenToolOption[] = [
  { id: 61129, name: 'Loaf pan', category: 'Cookware & bakeware', builtIn: true },
  { id: 61737, name: 'Mould', category: 'Cookware & bakeware', builtIn: false },
  { id: 61740, name: 'Mold', category: 'Cookware & bakeware', builtIn: false },
];

const breadTin: KitchenToolSuggestion = {
  id: 5,
  name: 'bread tin',
  displayName: 'Bread tin',
  suggestedCategory: 'Cookware & bakeware',
  seenCount: 12,
  status: 'pending',
  toolId: null,
  toolName: null,
  variants: ['Plain bread tin'],
  createdDate: '2026-10-14T00:00:00Z',
  examples: [],
};

describe('KitchenToolsAdmin', () => {
  let http: HttpTestingController;

  function create() {
    TestBed.configureTestingModule({
      imports: [KitchenToolsAdmin],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(KitchenToolsAdmin);
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/KitchenTools/status')).flush({ enabled: true, model: 'm', recipesTagged: 1, recipesRemaining: 0, aiToolLinks: 0, pendingSuggestions: 1 });
    http.expectOne((r) => r.url.endsWith('/KitchenTools/tools')).flush(tools);
    http.expectOne((r) => r.url.endsWith('/KitchenTools/suggestions')).flush([breadTin]);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => http.verify());

  it('links a pending name to an existing tool instead of creating one', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="kitchen-tools-admin-variants"]')?.textContent).toContain('Plain bread tin');

    const select = el.querySelector<HTMLSelectElement>('[data-testid="kitchen-tools-admin-same-as-select"]')!;
    select.value = '61129';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="kitchen-tools-admin-approve-btn"]')).toBeNull();
    el.querySelector<HTMLButtonElement>('[data-testid="kitchen-tools-admin-link-btn"]')!.click();

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/KitchenTools/suggestions/5/approve'));
    expect(req.request.body).toEqual({ displayName: 'Bread tin', category: 'Cookware & bakeware', toolId: 61129 });
  });

  it('merges an approved tool into another from the Tools view', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;
    el.querySelector<HTMLButtonElement>('[data-testid="kitchen-tools-admin-filter-tools"]')!.click();
    http.expectOne((r) => r.url.endsWith('/KitchenTools/status')).flush({ enabled: true, model: 'm', recipesTagged: 1, recipesRemaining: 0, aiToolLinks: 0, pendingSuggestions: 1 });
    http.expectOne((r) => r.url.endsWith('/KitchenTools/tools')).flush(tools);
    fixture.detectChanges();

    const items = el.querySelectorAll('[data-testid="kitchen-tools-admin-tool-item"]');
    expect(items.length).toBe(2);
    const mold = Array.from(items).find((i) => i.querySelector<HTMLInputElement>('input')!.value === 'Mold')!;
    const select = mold.querySelector<HTMLSelectElement>('[data-testid="kitchen-tools-admin-merge-select"]')!;
    select.value = '61737';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    mold.querySelector<HTMLButtonElement>('[data-testid="kitchen-tools-admin-merge-btn"]')!.click();

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/KitchenTools/tools/61740/merge'));
    expect(req.request.body).toEqual({ intoToolId: 61737 });
  });
});
