import { Pipe, PipeTransform } from '@angular/core';
import { RecipeDietMatchModel } from '../models/recipe.model';

export function describeDietConflicts(matches: RecipeDietMatchModel[] | null | undefined): string {
  if (!matches?.length) return '';
  const byRestriction = new Map<string, string[]>();
  for (const match of matches) {
    const ingredients = byRestriction.get(match.restrictionName) ?? [];
    if (match.ingredientName && !ingredients.includes(match.ingredientName)) {
      ingredients.push(match.ingredientName);
    }
    byRestriction.set(match.restrictionName, ingredients);
  }
  return [...byRestriction]
    .map(([restriction, ingredients]) => ingredients.length
      ? `Contains ${ingredients.join(', ')} — conflicts with ${restriction}`
      : `Conflicts with ${restriction}`)
    .join('; ');
}

@Pipe({ name: 'dietConflicts' })
export class DietConflictsPipe implements PipeTransform {
  transform(matches: RecipeDietMatchModel[] | null | undefined): string {
    return describeDietConflicts(matches);
  }
}
