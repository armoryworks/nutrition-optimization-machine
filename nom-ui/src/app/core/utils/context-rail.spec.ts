import { ActivatedRouteSnapshot, Data, Route } from '@angular/router';
import { contextRailEnabled } from './context-rail';
import { routes } from '../../app.routes';

const branch = (...data: Data[]): ActivatedRouteSnapshot => {
  let child: ActivatedRouteSnapshot | null = null;
  for (const d of [...data].reverse()) {
    child = { data: d, firstChild: child } as unknown as ActivatedRouteSnapshot;
  }
  return child as ActivatedRouteSnapshot;
};

const routeFor = (path: string): Route => {
  const route = routes.find((r) => r.path === path);
  if (!route) throw new Error(`no route ${path}`);
  return route;
};

describe('contextRailEnabled', () => {
  it('keeps the rail when no route on the branch opts out', () => {
    expect(contextRailEnabled(branch({}, { title: 'Home' }))).toBe(true);
  });

  it('hides the rail when the leaf route opts out', () => {
    expect(contextRailEnabled(branch({}, { contextRail: false }))).toBe(false);
  });

  it('hides the rail when a parent route opts out for its lazy children', () => {
    expect(contextRailEnabled(branch({}, { contextRail: false }, { title: 'Shopping List' }))).toBe(false);
  });

  it('only treats an explicit false as opting out', () => {
    expect(contextRailEnabled(branch({ contextRail: true }, { contextRail: undefined }))).toBe(true);
  });

  it('keeps the rail when there is no snapshot yet', () => {
    expect(contextRailEnabled(null)).toBe(true);
  });
});

describe('context rail route flags', () => {
  const focused = [
    'recipe/new',
    'recipe/import',
    'recipe/:id/edit',
    'recipe/:id',
    'ingredient/new',
    'ingredient/:id/edit',
    'onboarding',
    'profile',
    'restrictions',
    'household',
    'household/:id/consent',
    'plan',
    'plan/rules',
    'settings',
    'settings/security',
    'settings/privacy',
    'settings/api-tokens',
    'shopping',
    'messages',
    'messages/new',
    'messages/:id',
    'admin',
    'admin/:tab',
  ];
  const withRail = ['home', 'search', 'cookbooks', 'cookbook/:id', 'pantry', 'recipes/mine', 'ingredients/mine', 'dishes', 'plan/curated'];

  it.each(focused)('turns the rail off on %s', (path) => {
    expect(routeFor(path).data?.['contextRail']).toBe(false);
  });

  it.each(withRail)('keeps the rail on %s', (path) => {
    expect(routeFor(path).data?.['contextRail']).toBeUndefined();
  });
});
