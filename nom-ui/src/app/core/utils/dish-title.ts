import { Pipe, PipeTransform } from '@angular/core';

const SMALL_WORDS = new Set([
  'a',
  'al',
  'alla',
  'an',
  'and',
  'as',
  'at',
  'au',
  'aux',
  'but',
  'by',
  'da',
  'de',
  'del',
  'di',
  'du',
  'en',
  'for',
  'from',
  'in',
  'into',
  'la',
  'le',
  'n',
  'nor',
  'of',
  'on',
  'or',
  'over',
  'per',
  'the',
  'to',
  'with',
  'y',
]);

function capitalize(word: string): string {
  return word.replace(/(^|-)([a-zà-ÿ])/g, (_, sep: string, ch: string) => sep + ch.toUpperCase());
}

/**
 * Title-cases a dish name for display ("spinach and feta omelette" →
 * "Spinach and Feta Omelette"): small joining words stay lowercase after the
 * first word, and words that already carry capitals ("BBQ") are left alone.
 */
export function formatDishTitle(name: string | null | undefined): string {
  if (!name) return '';
  return name
    .split(/(\s+)/)
    .map((part, i) => {
      if (/^\s*$/.test(part) || part !== part.toLowerCase()) return part;
      if (i > 0 && SMALL_WORDS.has(part)) return part;
      return capitalize(part);
    })
    .join('');
}

@Pipe({ name: 'dishTitle' })
export class DishTitlePipe implements PipeTransform {
  transform(name: string | null | undefined): string {
    return formatDishTitle(name);
  }
}
