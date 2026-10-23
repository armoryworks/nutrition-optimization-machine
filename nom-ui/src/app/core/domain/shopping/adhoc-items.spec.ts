import { ADDED_BY_YOU, adhocDepartment, mergeAdhocItems, toShoppingItem } from './adhoc-items';
import { ShoppingDepartment } from './shopping-types';
import { HouseholdShoppingItem } from '../../models/household-shopping.model';

function item(partial: Partial<HouseholdShoppingItem> & { id: number; name: string }): HouseholdShoppingItem {
  return {
    householdId: 7,
    ingredientId: null,
    quantity: null,
    unit: null,
    checked: false,
    createdDate: '2026-10-22T00:00:00Z',
    ingredientNeedsVerification: false,
    ...partial,
  };
}

const planned: ShoppingDepartment[] = [
  {
    name: 'Meat & Seafood',
    icon: 'set_meal',
    items: [
      {
        ingredientId: 11,
        name: 'Chicken Breast',
        portions: [{ quantity: 1, unit: 'lb' }],
        department: 'Meat & Seafood',
        checkKey: '11',
        baseMassG: 450,
        baseVolumeMl: 0,
        baseCount: 0,
        retailPackage: null,
        retailPackageCount: 0,
        sourceRecipes: [],
        source: 'plan',
      },
    ],
  },
];

describe('mergeAdhocItems', () => {
  it('returns the planned departments untouched when nothing was added', () => {
    expect(mergeAdhocItems(planned, [])).toBe(planned);
  });

  it('puts a name nothing recognises under "Added by you" at the end', () => {
    const result = mergeAdhocItems(planned, [item({ id: 1, name: 'Paper towels' })]);
    expect(result.map((d) => d.name)).toEqual(['Meat & Seafood', ADDED_BY_YOU]);
    expect(result[1].items[0]).toMatchObject({ checkKey: 'item-1', source: 'adhoc', adhocId: 1, ingredientId: null });
    expect(result[0].items).toHaveLength(1);
  });

  it('files a recognised food in its aisle, created in store order when absent', () => {
    const result = mergeAdhocItems(planned, [item({ id: 2, name: 'Bananas', ingredientId: 12 })]);
    expect(result.map((d) => d.name)).toEqual(['Produce', 'Meat & Seafood']);
    expect(result[0].items[0]).toMatchObject({ name: 'Bananas', ingredientId: 12, checkKey: 'item-2' });
  });

  it('uses the linked food group when the name alone says nothing', () => {
    expect(adhocDepartment(item({ id: 3, name: 'Skyr', department: 'Dairy' }))).toBe('Dairy & Eggs');
  });

  it('shows a typed quantity as the line amount and keeps it for the pantry hand-off', () => {
    const line = toShoppingItem(item({ id: 4, name: 'Eggs', quantity: 12, unit: '' }));
    expect(line.portions).toEqual([{ quantity: 12, unit: '' }]);
    expect(line.baseCount).toBe(12);
    expect(toShoppingItem(item({ id: 5, name: 'Salt' })).portions).toEqual([]);
  });
});
