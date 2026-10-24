import { ActivatedRouteSnapshot } from '@angular/router';

export const NO_CONTEXT_RAIL = { contextRail: false } as const;

/**
 * False when any route on the active branch opts out with `data: { contextRail: false }`,
 * so focused pages (a recipe, the plan grid, admin, settings) get the full width
 * instead of sharing it with the Upcoming Meals / Shopping panel.
 */
export function contextRailEnabled(root: ActivatedRouteSnapshot | null): boolean {
  for (let route = root; route; route = route.firstChild) {
    if (route.data?.['contextRail'] === false) return false;
  }
  return true;
}
