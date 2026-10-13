import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { KitchenToolsForm } from './kitchen-tools-form.component';
import { KitchenSettings } from '../../../core/models/kitchen-tools.model';

const settings: KitchenSettings = {
  mode: 'warn',
  tools: [
    { id: 61102, key: 'oven', name: 'Oven', category: 'Stove & oven', ownedByDefault: true, owned: true, answered: false },
    { id: 61110, key: 'sous-vide', name: 'Sous vide', category: 'Appliances', ownedByDefault: false, owned: false, answered: false },
    { id: 61107, key: 'instant-pot', name: 'Instant Pot / pressure cooker', category: 'Appliances', ownedByDefault: false, owned: true, answered: true },
  ],
};

describe('KitchenToolsForm', () => {
  let http: HttpTestingController;

  function create() {
    TestBed.configureTestingModule({
      imports: [KitchenToolsForm],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(KitchenToolsForm);
    fixture.componentRef.setInput('householdId', 9);
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/Household/9/kitchen-tools')).flush(settings);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => http.verify());

  it('groups tools by category in catalog order', () => {
    const form = create().componentInstance;
    expect(form.categories().map((c) => c.name)).toEqual(['Stove & oven', 'Appliances']);
    expect(form.categories()[1].tools.map((t) => t.key)).toEqual(['sous-vide', 'instant-pot']);
  });

  it('saves only departures from the defaults and the chosen mode', () => {
    const fixture = create();
    const form = fixture.componentInstance;
    form.toggle(settings.tools[0], false);
    form.setMode('hide');
    form.onSave();

    const req = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Household/9/kitchen-tools'));
    expect(req.request.body).toEqual({
      mode: 'hide',
      tools: [
        { toolId: 61102, owned: false },
        { toolId: 61110, owned: null },
        { toolId: 61107, owned: true },
      ],
    });
    req.flush({ ...settings, mode: 'hide' });
    expect(form.successMessage()).toBe('Kitchen saved.');
  });

  it('explains a permission failure instead of a generic error', () => {
    const form = create().componentInstance;
    form.onSave();
    http.expectOne((r) => r.method === 'PUT').flush(null, { status: 403, statusText: 'Forbidden' });
    expect(form.errorMessage()).toContain("don't have permission");
  });
});
