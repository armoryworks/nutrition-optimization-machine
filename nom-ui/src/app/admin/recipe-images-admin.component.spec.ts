import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { RecipeImagesAdmin } from './recipe-images-admin.component';
import { summarizeImageChecks } from '../core/services/recipe-image.service';
import { RecipeImageCandidate } from '../core/models/recipe-image.model';

const checks = JSON.stringify({
  metadata: { passed: false, reasons: ['title/tags cover 33% of the dish name (need 60%)'] },
  caption: { passed: true, reasons: [] },
  vqa: { passed: true, reasons: [] },
  ranking: { passed: true, reasons: [] },
  content_ok: true,
});

const hotlinked: RecipeImageCandidate = {
  id: 7,
  recipeId: 109,
  recipeName: 'Creamy Tomato Basil Soup',
  status: 'pending',
  sourceKey: 'unsplash',
  sourceName: 'Unsplash',
  title: 'tomato soup in a white bowl',
  author: 'Sam Lens',
  authorUrl: 'https://unsplash.com/@sam?utm_source=nom&utm_medium=referral',
  license: 'Unsplash License',
  landingUrl: 'https://unsplash.com/photos/abc?utm_source=nom&utm_medium=referral',
  hotlink: true,
  previewUrl: 'https://images.unsplash.com/photo-1?w=1080',
  score: 0.71,
  checksJson: checks,
  createdDate: '2026-10-09T00:00:00Z',
};

const rehosted: RecipeImageCandidate = { ...hotlinked, id: 8, sourceKey: 'wikimedia', sourceName: 'Wikimedia Commons', hotlink: false, previewUrl: null };

describe('RecipeImagesAdmin', () => {
  let http: HttpTestingController;

  function create(list: RecipeImageCandidate[]) {
    TestBed.configureTestingModule({
      imports: [RecipeImagesAdmin],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RecipeImagesAdmin);
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/RecipeImages/candidates') && r.params.get('status') === 'pending').flush(list);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => http.verify());

  it('lists pending photos with their credit and failed checks', () => {
    const fixture = create([hotlinked]);
    http.expectNone((r) => r.url.includes('/image'));
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelectorAll('[data-testid="recipe-images-admin-item"]').length).toBe(1);
    expect(el.querySelector<HTMLImageElement>('[data-testid="recipe-images-admin-preview"]')!.src).toBe(hotlinked.previewUrl!);
    expect(el.querySelector('[data-testid="recipe-images-admin-credit"]')!.textContent).toContain('Sam Lens');
    const metadata = el.querySelector('[data-testid="recipe-images-admin-check-metadata"]')!;
    expect(metadata.textContent).toContain('33%');
    expect(el.querySelector('[data-testid="recipe-images-admin-check-caption"]')!.textContent).not.toContain('—');
  });

  it('loads rehosted previews through the authenticated API', () => {
    const createObjectURL = vi.fn(() => 'blob:preview-8');
    const revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    const fixture = create([rehosted]);
    const req = http.expectOne((r) => r.url.endsWith('/RecipeImages/candidates/8/image'));
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['x'], { type: 'image/jpeg' }));
    fixture.detectChanges();

    expect(fixture.componentInstance.previewFor(rehosted)).toBe('blob:preview-8');
    fixture.destroy();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-8');
  });

  it('approves a photo and refreshes the queue', () => {
    const fixture = create([hotlinked]);
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="recipe-images-admin-approve-btn"]')!.click();

    http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/RecipeImages/candidates/7/approve')).flush(null);
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/RecipeImages/candidates')).flush([]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="recipe-images-admin-empty"]')).toBeTruthy();
  });

  it('explains a conflict when the recipe got an image meanwhile', () => {
    const fixture = create([hotlinked]);
    fixture.componentInstance.approve(hotlinked);

    http.expectOne((r) => r.url.endsWith('/candidates/7/approve')).flush({ message: 'x' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="recipe-images-admin-error"]')!.textContent).toContain('already has an image');
  });

  it('offers removal, not approval, for attached photos', () => {
    const fixture = create([]);
    fixture.componentInstance.setFilter('attached');
    http.expectOne((r) => r.params.get('status') === 'attached').flush([{ ...hotlinked, status: 'attached' }]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="recipe-images-admin-approve-btn"]')).toBeNull();
    el.querySelector<HTMLButtonElement>('[data-testid="recipe-images-admin-remove-btn"]')!.click();
    http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/candidates/7/remove')).flush(null);
    http.expectOne((r) => r.params.get('status') === 'attached').flush([]);
  });
});

describe('summarizeImageChecks', () => {
  it('returns the four checks in order and ignores other evidence', () => {
    expect(summarizeImageChecks(checks).map((c) => [c.name, c.passed])).toEqual([
      ['metadata', false],
      ['caption', true],
      ['vqa', true],
      ['ranking', true],
    ]);
  });

  it('survives missing or malformed evidence', () => {
    expect(summarizeImageChecks(null)).toEqual([]);
    expect(summarizeImageChecks('not json')).toEqual([]);
    expect(summarizeImageChecks('{"metadata": {"passed": true, "reasons": [3, "ok"]}}')).toEqual([
      { name: 'metadata', passed: true, reasons: ['ok'] },
    ]);
  });
});
