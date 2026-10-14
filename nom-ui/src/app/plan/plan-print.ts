import { MealPlanWeekResponse } from '../core/models/meal-plan-week-response.model';
import { MealPlanEntry } from '../core/models/meal-plan-entry.model';
import { HouseholdMemberResponseModel } from '../core/models/household-member-response.model';
import { parseLocalDateString } from '../core/utils/local-date';

export interface PlanPrintMeal {
  mealType: string;
  items: string[];
  servings: number | null;
}

export interface PlanPrintDay {
  date: string;
  label: string;
  meals: PlanPrintMeal[];
}

/** One planned item as the printed week names it; a whole food shows its count when more than one. */
export function printEntryLabel(entry: MealPlanEntry): string {
  const name = entry.recipeName ?? entry.ingredientName ?? entry.title ?? '';
  return entry.ingredientId && entry.quantity && entry.quantity !== 1 ? `${name} ×${entry.quantity}` : name;
}

/**
 * Every day and meal of the week for the printed page. Servings count the
 * household members eating that meal (day- and meal-level exclusions removed),
 * or are null when no members are known.
 */
export function buildPrintWeek(
  week: MealPlanWeekResponse | null,
  members: HouseholdMemberResponseModel[],
  locale?: string,
): PlanPrintDay[] {
  if (!week) return [];
  return week.days.map((day) => ({
    date: day.date,
    label: parseLocalDateString(day.date).toLocaleDateString(locale, {
      weekday: 'long',
      month: 'short',
      day: 'numeric',
    }),
    meals: day.cells.map((cell) => ({
      mealType: cell.mealType,
      items: cell.entries.map(printEntryLabel),
      servings:
        members.length === 0
          ? null
          : members.filter(
              (m) =>
                !day.exclusions.some(
                  (e) =>
                    e.personId === m.personId &&
                    (e.mealTypeId === null || e.mealTypeId === cell.mealTypeId),
                ),
            ).length,
    })),
  }));
}
