import { Pipe, PipeTransform } from '@angular/core';

const NO_PLURAL = new Set(['dozen', 'each']);

export function formatUnit(name: string | null | undefined, quantity: number | null | undefined): string {
  if (!name) return '';
  const lower = /^[A-Z]?[a-z]+(?: [A-Z]?[a-z]+)*$/.test(name) ? name.toLowerCase() : name;
  if (quantity == null || quantity === 1 || NO_PLURAL.has(lower) || lower.endsWith('s')) {
    return lower;
  }
  if (/(?:ch|sh|x|z)$/.test(lower)) {
    return `${lower}es`;
  }
  return `${lower}s`;
}

@Pipe({ name: 'unit' })
export class UnitPipe implements PipeTransform {
  transform(name: string | null | undefined, quantity: number | null | undefined): string {
    return formatUnit(name, quantity);
  }
}
