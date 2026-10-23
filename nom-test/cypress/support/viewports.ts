export type ViewportName = 'desktop' | 'phone';

export interface ViewportCase {
  name: ViewportName;
  width: number;
  height: number;
  phone: boolean;
}

export const DESKTOP_VIEWPORT = { width: 1280, height: 720 };
export const PHONE_VIEWPORT = { width: 412, height: 915 };

export const ALL_VIEWPORTS: ViewportCase[] = [
  { name: 'desktop', ...DESKTOP_VIEWPORT, phone: false },
  { name: 'phone', ...PHONE_VIEWPORT, phone: true },
];

export const selectedViewports = (): ViewportCase[] => {
  const requested = String(Cypress.env('VIEWPORT') ?? 'all').trim().toLowerCase();
  if (requested === '' || requested === 'all') {
    return ALL_VIEWPORTS;
  }
  const selected = ALL_VIEWPORTS.filter((viewport) => viewport.name === requested);
  if (selected.length === 0) {
    throw new Error(`CYPRESS_VIEWPORT must be "desktop", "phone" or "all" — got "${requested}"`);
  }
  return selected;
};
