import { Pipe, PipeTransform } from '@angular/core';

const NO_PLURAL = new Set(['dozen', 'each']);

export function formatUnit(name: string | null | undefined, quantity: number | null | undefined): string {
  if (!name) return '';
  const lower = name.length > 3 ? name.toLowerCase() : name;
  if (quantity == null || quantity === 1 || NO_PLURAL.has(lower) || lower.endsWith('s')) {
    return lower;
  }
  return `${lower}s`;
}

@Pipe({ name: 'unit' })
export class UnitPipe implements PipeTransform {
  transform(name: string | null | undefined, quantity: number | null | undefined): string {
    return formatUnit(name, quantity);
  }
}
