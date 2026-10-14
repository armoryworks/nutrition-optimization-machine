import { buildPrintWeek, printEntryLabel } from './plan-print';
import { MealPlanEntry } from '../core/models/meal-plan-entry.model';
import { MealPlanDay } from '../core/models/meal-plan-day.model';
import { MealPlanWeekResponse } from '../core/models/meal-plan-week-response.model';
import { HouseholdMemberResponseModel } from '../core/models/household-member-response.model';

function entry(overrides: Partial<MealPlanEntry>): MealPlanEntry {
  return {
    id: 1,
    recipeId: null,
    recipeName: null,
    recipeImage: null,
    title: null,
    notes: null,
    calories: null,
    proteinGrams: null,
    carbGrams: null,
    fatGrams: null,
    completedDate: null,
    shoppingCompletedAt: null,
    ...overrides,
  };
}

function member(personId: number, personName: string): HouseholdMemberResponseModel {
  return {
    id: personId,
    householdId: 1,
    personId,
    personName,
    personEmail: null,
    role: 'Member',
    joinedDate: '2026-01-01',
    isActive: true,
    hasProfile: true,
    hasRestrictions: false,
  };
}

function day(date: string, overrides: Partial<MealPlanDay> = {}): MealPlanDay {
  return {
    date,
    dayOfWeek: '',
    exclusions: [],
    cells: [
      { mealTypeId: 1, mealType: 'Breakfast', entries: [], totalCalories: null, totalProteinGrams: null, totalCarbGrams: null, totalFatGrams: null },
      { mealTypeId: 3, mealType: 'Dinner', entries: [], totalCalories: null, totalProteinGrams: null, totalCarbGrams: null, totalFatGrams: null },
    ],
    ...overrides,
  };
}

function week(): MealPlanWeekResponse {
  const days = ['2026-10-12', '2026-10-13', '2026-10-14', '2026-10-15', '2026-10-16', '2026-10-17', '2026-10-18'].map((d) => day(d));
  days[0].cells[1].entries = [entry({ recipeId: 7, recipeName: 'Spinach and Feta Omelette' })];
  days[0].cells[0].entries = [entry({ ingredientId: 9, ingredientName: 'Banana', quantity: 2 })];
  days[6].cells[1].entries = [entry({ recipeId: 8, recipeName: 'Baked Salmon' })];
  days[6].exclusions = [
    { id: 1, householdId: 1, personId: 2, personName: 'Sam', date: '2026-10-18', mealTypeId: 3, mealType: 'Dinner' },
  ];
  days[1].exclusions = [
    { id: 2, householdId: 1, personId: 1, personName: 'Liz', date: '2026-10-13', mealTypeId: null, mealType: null },
  ];
  return { weekStart: '2026-10-12', weekEnd: '2026-10-18', days };
}

describe('buildPrintWeek', () => {
  const members = [member(1, 'Liz'), member(2, 'Sam')];

  it('lists all seven days with every meal, not just the day a phone is showing', () => {
    const printed = buildPrintWeek(week(), members, 'en-US');

    expect(printed.map((d) => d.label)).toEqual([
      'Monday, Oct 12',
      'Tuesday, Oct 13',
      'Wednesday, Oct 14',
      'Thursday, Oct 15',
      'Friday, Oct 16',
      'Saturday, Oct 17',
      'Sunday, Oct 18',
    ]);
    expect(printed[0].meals.map((m) => m.mealType)).toEqual(['Breakfast', 'Dinner']);
    expect(printed[0].meals[1].items).toEqual(['Spinach and Feta Omelette']);
    expect(printed[6].meals[1].items).toEqual(['Baked Salmon']);
    expect(printed[3].meals[0].items).toEqual([]);
  });

  it('shows how many people each meal serves after exclusions', () => {
    const printed = buildPrintWeek(week(), members, 'en-US');

    expect(printed[0].meals[1].servings).toBe(2);
    expect(printed[6].meals[1].servings).toBe(1);
    expect(printed[6].meals[0].servings).toBe(2);
    expect(printed[1].meals[0].servings).toBe(1);
    expect(printed[1].meals[1].servings).toBe(1);
  });

  it('leaves servings blank when no members are known', () => {
    expect(buildPrintWeek(week(), [], 'en-US')[0].meals[1].servings).toBeNull();
  });

  it('prints nothing before a week has loaded', () => {
    expect(buildPrintWeek(null, members)).toEqual([]);
  });
});

describe('printEntryLabel', () => {
  it('names a recipe, a whole food with its count, or a free-text title', () => {
    expect(printEntryLabel(entry({ recipeId: 1, recipeName: 'Herb Roasted Potatoes' }))).toBe('Herb Roasted Potatoes');
    expect(printEntryLabel(entry({ ingredientId: 9, ingredientName: 'Banana', quantity: 2 }))).toBe('Banana ×2');
    expect(printEntryLabel(entry({ ingredientId: 9, ingredientName: 'Apple', quantity: 1 }))).toBe('Apple');
    expect(printEntryLabel(entry({ title: 'Leftovers' }))).toBe('Leftovers');
  });
});
