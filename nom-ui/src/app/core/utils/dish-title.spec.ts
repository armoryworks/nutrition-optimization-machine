import { DishTitlePipe, formatDishTitle } from './dish-title';

describe('formatDishTitle', () => {
  it('keeps joining words lowercase', () => {
    expect(formatDishTitle('spinach and feta omelette')).toBe('Spinach and Feta Omelette');
    expect(formatDishTitle('baked salmon with roasted asparagus')).toBe('Baked Salmon with Roasted Asparagus');
    expect(formatDishTitle('steak au poivre')).toBe('Steak au Poivre');
    expect(formatDishTitle('pasta alla norma')).toBe('Pasta alla Norma');
    expect(formatDishTitle('rice and beans in a bowl')).toBe('Rice and Beans in a Bowl');
  });

  it('capitalizes a small word when it starts the name', () => {
    expect(formatDishTitle('the best chili')).toBe('The Best Chili');
    expect(formatDishTitle('a simple salad')).toBe('A Simple Salad');
  });

  it('capitalizes each part of a hyphenated word but not after an apostrophe', () => {
    expect(formatDishTitle('chicken stir-fry')).toBe('Chicken Stir-Fry');
    expect(formatDishTitle("shepherd's pie")).toBe("Shepherd's Pie");
  });

  it('leaves words that already carry capitals alone', () => {
    expect(formatDishTitle('BBQ pulled pork')).toBe('BBQ Pulled Pork');
    expect(formatDishTitle('Spinach and Feta Omelette')).toBe('Spinach and Feta Omelette');
  });

  it('returns an empty string for a missing name', () => {
    expect(formatDishTitle(null)).toBe('');
    expect(formatDishTitle(undefined)).toBe('');
    expect(formatDishTitle('')).toBe('');
  });

  it('is exposed as the dishTitle pipe', () => {
    expect(new DishTitlePipe().transform('fish and chips')).toBe('Fish and Chips');
  });
});
