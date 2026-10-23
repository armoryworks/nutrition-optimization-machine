import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { SKIPPED_MESSAGE, VerificationFlowService } from './verification-flow.service';
import { VerificationSheet } from './verification-sheet.component';
import { LabelConfirmSheet } from './label-confirm-sheet.component';
import { MeasurementsSheet } from './measurements-sheet.component';
import { LabelScanResult } from '../../core/models/household-shopping.model';

const scan: LabelScanResult = {
  verificationId: 501,
  ocrText: '',
  imageStored: true,
  parsed: {
    parsed: true,
    basis: 'serving',
    servingsPerContainerApprox: false,
    dualColumn: false,
    nutrients: [],
    notSignificantSource: [],
    warnings: [],
    fieldsRecognised: 3,
    confidence: 0.8,
    matchedLines: [],
  },
};

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('VerificationFlowService', () => {
  let http: HttpTestingController;
  let opened: unknown[];
  let results: unknown[];

  function create(sequence: unknown[]) {
    opened = [];
    results = [...sequence];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        VerificationFlowService,
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
        {
          provide: MatBottomSheet,
          useValue: {
            open: (component: unknown) => {
              opened.push(component);
              return { afterDismissed: () => of(results.shift()) };
            },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(VerificationFlowService);
  }

  const skipPost = () => http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/verification/11/skip'));

  afterEach(() => http.verify());

  it('records a skip only when the Skip button is pressed, and says so', async () => {
    const service = create([{ action: 'skip' }]);
    const run = service.run({ ingredientId: 11, name: 'Chicken Breast' }, 7);
    await settle();
    const req = skipPost();
    expect(req.request.body).toEqual({ householdId: 7 });
    req.flush(null);
    expect(await run).toBe('skipped');
    expect(opened).toEqual([VerificationSheet]);
    expect(TestBed.inject(MatSnackBar).open).toHaveBeenCalledWith(SKIPPED_MESSAGE, 'OK', expect.anything());
  });

  it('leaves nothing behind when the sheet is closed any other way', async () => {
    const outcome = await create([undefined]).run({ ingredientId: 11, name: 'Chicken Breast' }, 7);
    expect(outcome).toBe('dismissed');
    expect(opened).toEqual([VerificationSheet]);
    http.expectNone(() => true);
  });

  it('reports a non-food so the page can unlink the line', async () => {
    const result = { ingredientId: 11, itemsUnlinked: 1, ingredientRetired: true };
    const outcome = await create([{ action: 'not-food', result }]).run({ ingredientId: 11, name: 'Paper towels', adhoc: true }, 7);
    expect(outcome).toBe('not-food');
    expect(opened).toEqual([VerificationSheet]);
    http.expectNone(() => true);
  });

  it('follows a label scan with the confirm sheet and then measurements', async () => {
    const outcome = await create([{ action: 'label', scan }, { saved: true }, undefined]).run({ ingredientId: 11, name: 'Chicken Breast' }, 7);
    expect(outcome).toBe('recorded');
    expect(opened).toEqual([VerificationSheet, LabelConfirmSheet, MeasurementsSheet]);
    http.expectNone((r) => r.url.endsWith('/skip'));
  });

  it('goes back to the camera when the user asks to retake', async () => {
    const run = create([{ action: 'label', scan }, { retake: true }, { action: 'skip' }]).run({ ingredientId: 11, name: 'Chicken Breast' }, 7);
    await settle();
    skipPost().flush(null);
    await run;
    expect(opened).toEqual([VerificationSheet, LabelConfirmSheet, VerificationSheet]);
  });

  it('stops after a cancelled confirmation, leaving the photo pending', async () => {
    await create([{ action: 'label', scan }, undefined]).run({ ingredientId: 11, name: 'Chicken Breast' }, 7);
    expect(opened).toEqual([VerificationSheet, LabelConfirmSheet]);
    http.expectNone(() => true);
  });

  it('asks for measurements straight after a barcode', async () => {
    const result = { verificationId: 502, upc: '012345678905', decoded: true, source: 'client', appliedToIngredient: true };
    await create([{ action: 'barcode', result }, { saved: true }]).run({ ingredientId: 11, name: 'Chicken Breast' }, 7);
    expect(opened).toEqual([VerificationSheet, MeasurementsSheet]);
    http.expectNone(() => true);
  });

  it('tells the user when the skip could not be saved instead of failing silently', async () => {
    const service = create([{ action: 'skip' }]);
    const run = service.run({ ingredientId: 11, name: 'Chicken Breast' }, 7);
    await settle();
    skipPost().flush(null, { status: 500, statusText: 'Server Error' });
    await run;
    expect(TestBed.inject(MatSnackBar).open).toHaveBeenCalled();
  });
});
