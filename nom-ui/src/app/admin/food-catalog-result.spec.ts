import { curationResultMessage } from './food-catalog.component';

describe('curationResultMessage', () => {
  it('says when nothing changed because the foods were already approved', () => {
    expect(curationResultMessage({ updated: 0, unchanged: 50 }, true)).toBe('All 50 foods were already approved for planning.');
    expect(curationResultMessage({ updated: 0, unchanged: 1 }, true)).toBe('That food was already approved for planning.');
  });

  it('reports approvals and the ones that were already approved', () => {
    expect(curationResultMessage({ updated: 3, unchanged: 0 }, true)).toBe('3 foods approved — meal planning can now use them.');
    expect(curationResultMessage({ updated: 1, unchanged: 2 }, true)).toBe(
      '1 food approved — meal planning can now use them. 2 foods were already approved for planning.',
    );
  });

  it('reports rejections', () => {
    expect(curationResultMessage({ updated: 2, unchanged: 1 }, false)).toBe('2 foods rejected. 1 food was already rejected.');
  });
});
