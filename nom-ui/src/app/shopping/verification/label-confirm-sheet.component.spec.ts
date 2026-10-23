import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { LabelConfirmSheet, labelConfirmBody, labelFormValue } from './label-confirm-sheet.component';
import { LabelScanResult, NutritionFactsParseResult } from '../../core/models/household-shopping.model';

const parsed: NutritionFactsParseResult = {
  parsed: true,
  basis: 'serving',
  servingText: '2/3 cup',
  servingGrams: 55,
  servingsPerContainer: 8,
  servingsPerContainerApprox: true,
  dualColumn: false,
  calories: 230,
  nutrients: [
    { key: 'fat', amount: 8, unit: 'g', percentDailyValue: 10, raw: 'Total Fat 8g 10%' },
    { key: 'sodium', amount: 160, unit: 'mg', percentDailyValue: 7, raw: 'Sodium 160mg 7%' },
    { key: 'trans_fat', amount: null, lessThan: 1, unit: 'g', raw: 'Trans Fat <1g' },
  ],
  notSignificantSource: [],
  warnings: ['Saturated and trans fat exceed total fat'],
  fieldsRecognised: 4,
  confidence: 0.7,
  matchedLines: [],
};

const scan: LabelScanResult = { verificationId: 501, parsed, ocrText: 'Nutrition Facts', imageStored: true };

describe('labelFormValue / labelConfirmBody', () => {
  it('prefills what the parser read and leaves the rest blank', () => {
    const value = labelFormValue(parsed);
    expect(value.servingGrams).toBe(55);
    expect(value.calories).toBe(230);
    expect(value.nutrients['fat']).toBe(8);
    expect(value.nutrients['trans_fat']).toBeNull();
    expect(value.nutrients['protein']).toBeNull();
  });

  it('sends only the rows with a number, keeping the printed %DV, and allows a blank serving weight', () => {
    const value = labelFormValue(parsed);
    value.servingGrams = null;
    value.nutrients['fat'] = 9;
    value.nutrients['protein'] = 3;
    const body = labelConfirmBody(value, parsed);
    expect(body.servingGrams).toBeNull();
    expect(body.servingText).toBe('2/3 cup');
    expect(body.nutrients).toEqual([
      { key: 'fat', amount: 9, unit: 'g', percentDailyValue: 10 },
      { key: 'sodium', amount: 160, unit: 'mg', percentDailyValue: 7 },
      { key: 'protein', amount: 3, unit: 'g', percentDailyValue: null },
    ]);
  });
});

describe('LabelConfirmSheet', () => {
  let http: HttpTestingController;
  let dismiss: ReturnType<typeof vi.fn>;

  function create() {
    dismiss = vi.fn();
    TestBed.configureTestingModule({
      imports: [LabelConfirmSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: MAT_BOTTOM_SHEET_DATA, useValue: { ingredientId: 11, householdId: 7, name: 'Granola', scan } },
        { provide: MatBottomSheetRef, useValue: { dismiss } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(LabelConfirmSheet);
    fixture.detectChanges();
    return fixture;
  }

  const field = (el: HTMLElement, testid: string) => el.querySelector<HTMLInputElement>(`[data-testid="${testid}"]`)!;

  afterEach(() => http.verify());

  it('shows the parsed values per serving, with the parser warnings, for correction', () => {
    const el = create().nativeElement as HTMLElement;
    expect(field(el, 'label-confirm-nutrient-fat').value).toBe('8');
    expect(field(el, 'label-confirm-nutrient-sodium').value).toBe('160');
    expect(field(el, 'label-confirm-nutrient-trans_fat').value).toBe('');
    expect(field(el, 'label-confirm-serving-grams-input').value).toBe('55');
    expect(el.textContent).toContain('per serving, exactly as printed');
    expect(el.textContent).toContain('Saturated and trans fat exceed total fat');
  });

  it('saves the corrected values against the pending verification', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;
    const fat = field(el, 'label-confirm-nutrient-fat');
    fat.value = '9';
    fat.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-testid="label-confirm-save-btn"]')!.click();

    const req = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Shopping/verification/501'));
    expect(req.request.body).toMatchObject({
      servingText: '2/3 cup',
      servingGrams: 55,
      servingsPerContainer: 8,
      calories: 230,
      nutrients: [
        { key: 'fat', amount: 9, unit: 'g' },
        { key: 'sodium', amount: 160, unit: 'mg' },
      ],
    });
    req.flush({ id: 501 });
    expect(dismiss).toHaveBeenCalledWith({ saved: true });
  });

  it('asks the flow to reopen the camera on Retake and sends nothing on Cancel', () => {
    const el = create().nativeElement as HTMLElement;
    el.querySelector<HTMLButtonElement>('[data-testid="label-confirm-retake-btn"]')!.click();
    expect(dismiss).toHaveBeenCalledWith({ retake: true });
    el.querySelector<HTMLButtonElement>('[data-testid="label-confirm-cancel-btn"]')!.click();
    expect(dismiss).toHaveBeenLastCalledWith();
    http.expectNone(() => true);
  });
});
