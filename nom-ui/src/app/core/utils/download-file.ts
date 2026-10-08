import { toLocalDateString } from './local-date';

/** Saves `text` as a file through a temporary object-URL link. */
export function downloadTextFile(fileName: string, text: string, type: string): void {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}

/** e.g. `shopping-list-2026-10-08.csv`, dated in the user's local timezone. */
export function shoppingListFileName(extension: string, date = new Date()): string {
  return `shopping-list-${toLocalDateString(date)}.${extension}`;
}
