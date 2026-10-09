import { Pipe, PipeTransform } from '@angular/core';

export type UnitSystem = 'metric' | 'imperial';

const GRAMS_PER_OUNCE = 28.3495;
const METRIC_MASS = new Set(['gram', 'grams', 'g', 'kilogram', 'kilograms', 'kg', 'milligram', 'milligrams', 'mg']);
const IMPERIAL_MASS = new Set(['ounce', 'ounces', 'oz', 'pound', 'pounds', 'lb', 'lbs']);

function trim(value: number, decimals: number): string {
  return String(Number(value.toFixed(decimals)));
}

export function formatMass(grams: number | null | undefined, system: UnitSystem): string {
  if (grams == null || !(grams > 0)) return '';
  if (system === 'imperial') {
    const ounces = grams / GRAMS_PER_OUNCE;
    if (ounces >= 16) return `${trim(ounces / 16, 2)} lb`;
    if (ounces < 0.05) return '< 0.1 oz';
    return `${trim(ounces, ounces < 10 ? 1 : 0)} oz`;
  }
  if (grams >= 1000) return `${trim(grams / 1000, 2)} kg`;
  if (grams < 0.05) return '< 0.1 g';
  return `${trim(grams, grams < 10 ? 1 : 0)} g`;
}

export function massAside(grams: number | null | undefined, measurement: string | null | undefined, system: UnitSystem): string {
  const unit = (measurement ?? '').trim().toLowerCase();
  if ((system === 'metric' ? METRIC_MASS : IMPERIAL_MASS).has(unit)) return '';
  return formatMass(grams, system);
}

@Pipe({ name: 'massAside' })
export class MassAsidePipe implements PipeTransform {
  transform(grams: number | null | undefined, measurement: string | null | undefined, system: UnitSystem): string {
    return massAside(grams, measurement, system);
  }
}
