import {
  EMPTY_FILTERS,
  filterPills,
  filtersToParams,
  hasActiveFilters,
  parseSearchFilters,
  removeFilter,
  SORT_OPTIONS,
  toggleId,
  withoutFilterParams,
} from './search-filters';

describe('search filters', () => {
  const options = {
    mealTypes: [{ id: 1100, name: 'Breakfast' }],
    courses: [{ id: 3105, name: 'Dessert' }],
    kitchenTools: [{ id: 61109, name: 'Air fryer', group: 'Appliances' }],
  };

  it('reads filters and sort from query params', () => {
    const filters = parseSearchFilters({
      q: 'wings',
      meal: '1100',
      course: '3101,3105,3105',
      tool: '61109,abc',
      maxTime: '30',
      minRating: '4',
      kitchen: '1',
      mine: '1',
      sort: 'quickest',
    });
    expect(filters).toEqual({
      mealTypeIds: [1100],
      courseIds: [3101, 3105],
      toolIds: [61109],
      maxTime: 30,
      minRating: 4,
      kitchen: true,
      mine: true,
      hideConflicts: null,
      sort: 'quickest',
    });
  });

  it('falls back to no filters and best-match sort', () => {
    expect(parseSearchFilters({ sort: 'bogus', maxTime: '-5' })).toEqual(EMPTY_FILTERS);
    expect(parseSearchFilters(undefined)).toEqual(EMPTY_FILTERS);
  });

  it('round-trips through query params, leaving defaults out of the URL', () => {
    const filters = { ...EMPTY_FILTERS, toolIds: [61109, 61110], kitchen: true, sort: 'rating' as const };
    const params = filtersToParams(filters);
    expect(params).toEqual({
      meal: null,
      course: null,
      tool: '61109,61110',
      maxTime: null,
      minRating: null,
      kitchen: 1,
      mine: null,
      conflicts: null,
      sort: 'rating',
    });
    expect(parseSearchFilters({ tool: params['tool'], kitchen: '1', sort: 'rating' })).toEqual(filters);
    expect(filtersToParams(EMPTY_FILTERS)['sort']).toBeNull();
  });

  it('strips only filter params', () => {
    expect(withoutFilterParams({ q: 'x', ingredientId: '3', tool: '1', mine: '1', sort: 'newest' })).toEqual({ q: 'x', ingredientId: '3' });
  });

  it('labels a pill for every active criterion and removes them one at a time', () => {
    const filters = { ...EMPTY_FILTERS, mealTypeIds: [1100], toolIds: [61109, 999], maxTime: 30, kitchen: true };
    const pills = filterPills(filters, options);
    expect(pills.map((p) => p.label)).toEqual(['Breakfast', 'Air fryer', 'Kitchen tool', '30 min or less', 'My kitchen can make']);

    const afterTool = removeFilter(filters, pills[1]);
    expect(afterTool.toolIds).toEqual([999]);
    expect(removeFilter(filters, pills[4]).kitchen).toBe(false);
    expect(removeFilter(filters, pills[3]).maxTime).toBeNull();
  });

  it('labels and removes the my-recipes filter', () => {
    const filters = { ...EMPTY_FILTERS, mine: true };
    const pills = filterPills(filters, options);
    expect(pills.map((p) => p.label)).toEqual(['My recipes']);
    expect(hasActiveFilters(filters)).toBe(true);
    expect(removeFilter(filters, pills[0]).mine).toBe(false);
  });

  it('reads the restriction filter as on, off or left to the default', () => {
    expect(parseSearchFilters({ conflicts: 'hide' }).hideConflicts).toBe(true);
    expect(parseSearchFilters({ conflicts: 'show' }).hideConflicts).toBe(false);
    expect(parseSearchFilters({ conflicts: 'maybe' }).hideConflicts).toBeNull();
    expect(filtersToParams({ ...EMPTY_FILTERS, hideConflicts: true })['conflicts']).toBe('hide');
    expect(filtersToParams({ ...EMPTY_FILTERS, hideConflicts: false })['conflicts']).toBe('show');
    expect(withoutFilterParams({ q: 'x', conflicts: 'show' })).toEqual({ q: 'x' });
  });

  it('labels the restriction filter and turns it off when its pill is removed', () => {
    const filters = { ...EMPTY_FILTERS, hideConflicts: true };
    const pills = filterPills(filters, options);
    expect(pills.map((p) => p.label)).toEqual(['Hiding conflicts with my restrictions']);
    expect(hasActiveFilters(filters)).toBe(true);
    expect(removeFilter(filters, pills[0]).hideConflicts).toBe(false);
    expect(filterPills({ ...EMPTY_FILTERS, hideConflicts: false }, options)).toEqual([]);
  });

  it('offers protein and calorie sorts', () => {
    expect(SORT_OPTIONS.find((o) => o.value === 'protein')).toEqual({ value: 'protein', label: 'Most protein', sortBy: 'protein' });
    expect(SORT_OPTIONS.find((o) => o.value === 'calories')).toEqual({ value: 'calories', label: 'Fewest calories', sortBy: 'calories' });
    expect(parseSearchFilters({ sort: 'calories' }).sort).toBe('calories');
  });

  it('sort alone is not an active filter', () => {
    expect(hasActiveFilters({ ...EMPTY_FILTERS, sort: 'newest' })).toBe(false);
    expect(hasActiveFilters({ ...EMPTY_FILTERS, courseIds: [3105] })).toBe(true);
  });

  it('toggles ids in and out of a selection', () => {
    expect(toggleId([1, 2], 2)).toEqual([1]);
    expect(toggleId([1], 2)).toEqual([1, 2]);
  });
});
