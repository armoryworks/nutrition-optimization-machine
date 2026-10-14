import { shoppingListShareData, shoppingListText } from './list-text';

const items = [
  { name: 'Carrots', quantity: 2, unit: 'lb', category: 'Produce' },
  { name: 'Spinach', category: 'Produce' },
  { name: 'Eggs', quantity: 12, category: 'Dairy & Eggs' },
];
const line = (item: { name: string }) => item.name;

describe('shoppingListText', () => {
  it('heads the list with the title and groups lines under their department', () => {
    expect(shoppingListText('Shopping List — Oct 13 – Oct 19', items, line)).toBe(
      [
        'Shopping List — Oct 13 – Oct 19',
        '',
        'Produce:',
        '- Carrots',
        '- Spinach',
        '',
        'Dairy & Eggs:',
        '- Eggs',
      ].join('\n'),
    );
  });
});

describe('shoppingListShareData', () => {
  it('sends the header once, inside the text, and no separate title', () => {
    const data = shoppingListShareData('Shopping List — Oct 13 – Oct 19', items, line);

    expect(data.title).toBeUndefined();
    expect(data.text?.match(/Shopping List —/g)).toHaveLength(1);
    expect(data.text?.startsWith('Shopping List — Oct 13 – Oct 19\n')).toBe(true);
  });
});
