import { Injectable } from '@angular/core';

type BarcodeDetectorCtor = new (options?: { formats: string[] }) => {
  detect(source: ImageBitmap): Promise<{ rawValue: string }[]>;
};

const RETAIL_FORMATS = ['ean_13', 'ean_8', 'upc_a', 'upc_e'];

function detectorCtor(): BarcodeDetectorCtor | undefined {
  return (globalThis as { BarcodeDetector?: BarcodeDetectorCtor }).BarcodeDetector;
}

/**
 * Reads a retail barcode from a photo with the browser's own BarcodeDetector.
 * Resolves null wherever the browser cannot help — no API, a failed decode or
 * no code in the picture — so the caller falls back to the server.
 */
@Injectable({ providedIn: 'root' })
export class BarcodeDecoder {
  supported(): boolean {
    return typeof detectorCtor() === 'function';
  }

  async decode(file: File): Promise<string | null> {
    const Ctor = detectorCtor();
    if (!Ctor) return null;
    try {
      const bitmap = await createImageBitmap(file);
      try {
        const codes = await new Ctor({ formats: RETAIL_FORMATS }).detect(bitmap);
        return codes[0]?.rawValue ?? null;
      } finally {
        bitmap.close();
      }
    } catch {
      return null;
    }
  }
}
