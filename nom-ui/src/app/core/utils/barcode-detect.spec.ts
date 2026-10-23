import { BarcodeDecoder } from './barcode-detect';

type Globals = {
  BarcodeDetector?: unknown;
  createImageBitmap?: unknown;
};

describe('BarcodeDecoder', () => {
  const g = globalThis as Globals;
  const file = new File(['x'], 'code.jpg', { type: 'image/jpeg' });
  let savedDetector: unknown;
  let savedBitmap: unknown;

  beforeEach(() => {
    savedDetector = g.BarcodeDetector;
    savedBitmap = g.createImageBitmap;
    g.createImageBitmap = async () => ({ close: () => undefined });
  });

  afterEach(() => {
    g.BarcodeDetector = savedDetector;
    g.createImageBitmap = savedBitmap;
  });

  it('is unsupported and resolves null when the browser has no BarcodeDetector', async () => {
    delete g.BarcodeDetector;
    const decoder = new BarcodeDecoder();
    expect(decoder.supported()).toBe(false);
    expect(await decoder.decode(file)).toBeNull();
  });

  it('returns the first code the browser reads', async () => {
    g.BarcodeDetector = class {
      async detect() {
        return [{ rawValue: '012345678905' }];
      }
    };
    const decoder = new BarcodeDecoder();
    expect(decoder.supported()).toBe(true);
    expect(await decoder.decode(file)).toBe('012345678905');
  });

  it('resolves null when nothing was found or detection throws', async () => {
    g.BarcodeDetector = class {
      async detect() {
        return [];
      }
    };
    expect(await new BarcodeDecoder().decode(file)).toBeNull();

    g.BarcodeDetector = class {
      async detect(): Promise<never> {
        throw new Error('unsupported image');
      }
    };
    expect(await new BarcodeDecoder().decode(file)).toBeNull();
  });
});
