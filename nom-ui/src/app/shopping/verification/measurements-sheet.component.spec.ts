import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { signal } from '@angular/core';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { MeasurementsSheet, defaultPackageUnit, measurementsBody } from './measurements-sheet.component';
import { UnitPreferenceService } from '../../core/services/unit-preference.service';
import { UnitSystem } from '../../core/utils/mass-display';

describe('measurementsBody', () => {
  it('is null until something is filled in, and drops the unit when there is no package size', () => {
    expect(measurementsBody(7, { packageAmount: null, packageUnit: 'oz', piecesPerPackage: null, cupGrams: null })).toBeNull();
    expect(measurementsBody(7, { packageAmount: null, packageUnit: 'oz', piecesPerPackage: 12, cupGrams: null })).toEqual({
      householdId: 7,
      packageAmount: null,
      packageUnit: null,
      piecesPerPackage: 12,
      cupGrams: null,
    });
  });

  it('defaults the unit from the person\'s measurement system', () => {
    expect(defaultPackageUnit('imperial')).toBe('oz');
    expect(defaultPackageUnit('metric')).toBe('g');
  });
});

describe('MeasurementsSheet', () => {
  let http: HttpTestingController;
  let dismiss: ReturnType<typeof vi.fn>;

  function create(system: UnitSystem = 'imperial') {
    dismiss = vi.fn();
    TestBed.configureTestingModule({
      imports: [MeasurementsSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: MAT_BOTTOM_SHEET_DATA, useValue: { ingredientId: 11, householdId: 7, name: 'Granola' } },
        { provide: MatBottomSheetRef, useValue: { dismiss } },
        { provide: UnitPreferenceService, useValue: { system: signal(system) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MeasurementsSheet);
    fixture.detectChanges();
    return fixture;
  }

  const save = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('[data-testid="measurements-save-btn"]')!;

  afterEach(() => http.verify());

  it('starts in ounces for an imperial household with Save disabled', () => {
    const fixture = create();
    expect(fixture.componentInstance.form.controls.packageUnit.value).toBe('oz');
    expect(save(fixture.nativeElement).disabled).toBe(true);
  });

  it('sends the package size in the chosen unit and leaves conversion to the server', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;
    const size = el.querySelector<HTMLInputElement>('[data-testid="measurements-package-size-input"]')!;
    size.value = '16';
    size.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(save(el).disabled).toBe(false);
    save(el).click();

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/verification/11/measurements'));
    expect(req.request.body).toEqual({ householdId: 7, packageAmount: 16, packageUnit: 'oz', piecesPerPackage: null, cupGrams: null });
    req.flush({ id: 504 });
    expect(dismiss).toHaveBeenCalledWith({ saved: true });
  });

  it('skips without a request', () => {
    const el = create('metric').nativeElement as HTMLElement;
    el.querySelector<HTMLButtonElement>('[data-testid="measurements-skip-btn"]')!.click();
    http.expectNone(() => true);
    expect(dismiss).toHaveBeenCalledWith();
  });
});
