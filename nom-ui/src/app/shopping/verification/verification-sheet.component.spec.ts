import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import {
  LABEL_UNREADABLE,
  MAX_PHOTO_BYTES,
  NO_BARCODE_FOUND,
  PHOTO_TOO_LARGE,
  UPLOAD_TOO_LARGE,
  VerificationSheet,
  uploadErrorMessage,
} from './verification-sheet.component';
import { HttpErrorResponse } from '@angular/common/http';
import { BarcodeDecoder } from '../../core/utils/barcode-detect';

describe('VerificationSheet', () => {
  let http: HttpTestingController;
  let dismiss: ReturnType<typeof vi.fn>;
  let decoded: string | null;

  function create(adhoc = false) {
    dismiss = vi.fn();
    TestBed.configureTestingModule({
      imports: [VerificationSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: MAT_BOTTOM_SHEET_DATA, useValue: { ingredientId: 11, name: 'Chicken Breast', householdId: 7, adhoc } },
        { provide: MatBottomSheetRef, useValue: { dismiss } },
        { provide: BarcodeDecoder, useValue: { supported: () => decoded !== null, decode: async () => decoded } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(VerificationSheet);
    fixture.detectChanges();
    return fixture;
  }

  function choose(el: HTMLElement, testid: string, file: File) {
    const input = el.querySelector<HTMLInputElement>(`[data-testid="${testid}"]`)!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
  }

  const photo = () => new File(['jpeg'], 'label.jpg', { type: 'image/jpeg' });
  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  beforeEach(() => (decoded = null));
  afterEach(() => http.verify());

  it('offers the camera for the label and barcode plus a skip', () => {
    const el = create().nativeElement as HTMLElement;
    const label = el.querySelector<HTMLInputElement>('[data-testid="verification-sheet-label-input"]')!;
    expect(label.getAttribute('accept')).toBe('image/*');
    expect(label.getAttribute('capture')).toBe('environment');
    expect(el.querySelector('[data-testid="verification-sheet-scan-barcode-btn"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="verification-sheet-not-food-btn"]')).toBeNull();
    el.querySelector<HTMLButtonElement>('[data-testid="verification-sheet-skip-btn"]')!.click();
    expect(dismiss).toHaveBeenCalledWith({ action: 'skip' });
  });

  it('lets a household-added line be marked as not a food', () => {
    const el = create(true).nativeElement as HTMLElement;
    el.querySelector<HTMLButtonElement>('[data-testid="verification-sheet-not-food-btn"]')!.click();

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/verification/11/not-food'));
    expect(req.request.body).toEqual({ householdId: 7 });
    const result = { ingredientId: 11, itemsUnlinked: 1, ingredientRetired: true };
    req.flush(result);
    expect(dismiss).toHaveBeenCalledWith({ action: 'not-food', result });
  });

  it('passes the server\'s reason through when it refuses the photo, and keeps retake wording otherwise', () => {
    const fixture = create();
    choose(fixture.nativeElement, 'verification-sheet-label-input', photo());
    http.expectOne((r) => r.url.endsWith('/Shopping/verification/11/label'))
      .flush({ message: 'Too many label scans this hour; try again later.' }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="verification-sheet-error"]')?.textContent).toContain('Too many label scans this hour');
    expect(dismiss).not.toHaveBeenCalled();

    choose(fixture.nativeElement, 'verification-sheet-label-input', photo());
    http.expectOne((r) => r.url.endsWith('/Shopping/verification/11/label')).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="verification-sheet-error"]')?.textContent).toContain(LABEL_UNREADABLE);

    expect(uploadErrorMessage(new HttpErrorResponse({ status: 413 }), LABEL_UNREADABLE)).toBe(UPLOAD_TOO_LARGE);
    expect(uploadErrorMessage(new HttpErrorResponse({ status: 400, error: { message: 'Only JPEG, PNG and WebP images are allowed' } }), LABEL_UNREADABLE))
      .toBe('Only JPEG, PNG and WebP images are allowed');
    expect(uploadErrorMessage(new Error('offline'), LABEL_UNREADABLE)).toBe(LABEL_UNREADABLE);
  });

  it('uploads the label photo and hands the parsed result back', () => {
    const fixture = create();
    choose(fixture.nativeElement, 'verification-sheet-label-input', photo());
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Reading label');

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/verification/11/label'));
    const form = req.request.body as FormData;
    expect(form.get('householdId')).toBe('7');
    expect((form.get('image') as File).name).toBe('label.jpg');
    const scan = { verificationId: 501, parsed: { parsed: true }, ocrText: '', imageStored: true };
    req.flush(scan);
    expect(dismiss).toHaveBeenCalledWith({ action: 'label', scan });
  });

  it('sends the code the browser read instead of the photo', async () => {
    decoded = '012345678905';
    const fixture = create();
    choose(fixture.nativeElement, 'verification-sheet-barcode-input', photo());
    await settle();

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/verification/11/barcode'));
    expect(req.request.body).toEqual({ householdId: 7, upc: '012345678905' });
    const result = { verificationId: 502, upc: '012345678905', decoded: true, source: 'client', appliedToIngredient: true };
    req.flush(result);
    expect(dismiss).toHaveBeenCalledWith({ action: 'barcode', result });
  });

  it('lets the server decode the photo when the browser cannot, and says so when nothing is found', async () => {
    const fixture = create();
    choose(fixture.nativeElement, 'verification-sheet-barcode-input', photo());
    await settle();

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/verification/11/barcode/image'));
    expect(req.request.body).toBeInstanceOf(FormData);
    req.flush({ verificationId: 503, upc: null, decoded: false, source: 'server', appliedToIngredient: false });
    fixture.detectChanges();

    expect(dismiss).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="verification-sheet-error"]')?.textContent).toContain(NO_BARCODE_FOUND);
  });

  it('refuses a photo over 10 MB before uploading anything', () => {
    const fixture = create();
    const big = photo();
    Object.defineProperty(big, 'size', { value: MAX_PHOTO_BYTES + 1 });
    choose(fixture.nativeElement, 'verification-sheet-label-input', big);
    fixture.detectChanges();
    http.expectNone(() => true);
    expect(fixture.nativeElement.querySelector('[data-testid="verification-sheet-error"]')?.textContent).toContain(PHOTO_TOO_LARGE);
  });
});
