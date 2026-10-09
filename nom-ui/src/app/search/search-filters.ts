import { Params } from '@angular/router';
import { RecipeSearchFilterOptions } from '../core/models/recipe-search-filter-options.model';

export type SearchSort = 'relevance' | 'rating' | 'newest' | 'quickest' | 'ingredients' | 'name';

export interface SearchFilters {
  mealTypeIds: number[];
  courseIds: number[];
  toolIds: number[];
  maxTime: number | null;
  minRating: number | null;
  kitchen: boolean;
  sort: SearchSort;
}

export type SearchFilterKind = 'meal' | 'course' | 'tool' | 'maxTime' | 'minRating' | 'kitchen';

export interface SearchFilterPill {
  kind: SearchFilterKind;
  id?: number;
  label: string;
}

export const SORT_OPTIONS: readonly { value: SearchSort; label: string; sortBy: string; sortDirection?: 'asc' | 'desc' }[] = [
  { value: 'relevance', label: 'Best match', sortBy: 'relevance' },
  { value: 'rating', label: 'Top rated', sortBy: 'rating', sortDirection: 'desc' },
  { value: 'newest', label: 'Newest', sortBy: 'newest' },
  { value: 'quickest', label: 'Quickest', sortBy: 'quickest' },
  { value: 'ingredients', label: 'Fewest ingredients', sortBy: 'ingredients' },
  { value: 'name', label: 'Name A–Z', sortBy: 'name', sortDirection: 'asc' },
];

export const MAX_TIME_OPTIONS: readonly number[] = [15, 30, 45, 60];
export const MIN_RATING_OPTIONS: readonly number[] = [3, 4];

export const EMPTY_FILTERS: SearchFilters = {
  mealTypeIds: [],
  courseIds: [],
  toolIds: [],
  maxTime: null,
  minRating: null,
  kitchen: false,
  sort: 'relevance',
};

const FILTER_PARAM_KEYS = ['meal', 'course', 'tool', 'maxTime', 'minRating', 'kitchen', 'sort'] as const;

function parseIds(value: unknown): number[] {
  if (typeof value !== 'string' || !value) return [];
  return [...new Set(value.split(',').map(Number).filter((n) => Number.isInteger(n) && n > 0))];
}

function parsePositive(value: unknown): number | null {
  const n = Number(value);
  return Number.isFinite(n) && n > 0 ? n : null;
}

export function parseSearchFilters(params: Params | undefined): SearchFilters {
  const sort = params?.['sort'];
  return {
    mealTypeIds: parseIds(params?.['meal']),
    courseIds: parseIds(params?.['course']),
    toolIds: parseIds(params?.['tool']),
    maxTime: parsePositive(params?.['maxTime']),
    minRating: parsePositive(params?.['minRating']),
    kitchen: params?.['kitchen'] === '1' || params?.['kitchen'] === 'true',
    sort: SORT_OPTIONS.some((o) => o.value === sort) ? (sort as SearchSort) : 'relevance',
  };
}

export function filtersToParams(filters: SearchFilters): Params {
  const ids = (list: number[]) => (list.length ? list.join(',') : null);
  return {
    meal: ids(filters.mealTypeIds),
    course: ids(filters.courseIds),
    tool: ids(filters.toolIds),
    maxTime: filters.maxTime,
    minRating: filters.minRating,
    kitchen: filters.kitchen ? 1 : null,
    sort: filters.sort === 'relevance' ? null : filters.sort,
  };
}

export function withoutFilterParams(params: Params): Params {
  const next = { ...params };
  for (const key of FILTER_PARAM_KEYS) delete next[key];
  return next;
}

export function hasActiveFilters(filters: SearchFilters): boolean {
  return (
    filters.mealTypeIds.length > 0 ||
    filters.courseIds.length > 0 ||
    filters.toolIds.length > 0 ||
    filters.maxTime !== null ||
    filters.minRating !== null ||
    filters.kitchen
  );
}

export function removeFilter(filters: SearchFilters, pill: SearchFilterPill): SearchFilters {
  const drop = (list: number[]) => list.filter((id) => id !== pill.id);
  switch (pill.kind) {
    case 'meal':
      return { ...filters, mealTypeIds: drop(filters.mealTypeIds) };
    case 'course':
      return { ...filters, courseIds: drop(filters.courseIds) };
    case 'tool':
      return { ...filters, toolIds: drop(filters.toolIds) };
    case 'maxTime':
      return { ...filters, maxTime: null };
    case 'minRating':
      return { ...filters, minRating: null };
    case 'kitchen':
      return { ...filters, kitchen: false };
  }
}

export function toggleId(list: number[], id: number): number[] {
  return list.includes(id) ? list.filter((x) => x !== id) : [...list, id];
}

export function filterPills(filters: SearchFilters, options: RecipeSearchFilterOptions | null): SearchFilterPill[] {
  const name = (list: { id: number; name: string }[] | undefined, id: number, fallback: string) =>
    list?.find((o) => o.id === id)?.name ?? fallback;

  return [
    ...filters.mealTypeIds.map((id) => ({ kind: 'meal' as const, id, label: name(options?.mealTypes, id, 'Meal') })),
    ...filters.courseIds.map((id) => ({ kind: 'course' as const, id, label: name(options?.courses, id, 'Course') })),
    ...filters.toolIds.map((id) => ({ kind: 'tool' as const, id, label: name(options?.kitchenTools, id, 'Kitchen tool') })),
    ...(filters.maxTime !== null ? [{ kind: 'maxTime' as const, label: `${filters.maxTime} min or less` }] : []),
    ...(filters.minRating !== null ? [{ kind: 'minRating' as const, label: `${filters.minRating}+ stars` }] : []),
    ...(filters.kitchen ? [{ kind: 'kitchen' as const, label: 'My kitchen can make' }] : []),
  ];
}
