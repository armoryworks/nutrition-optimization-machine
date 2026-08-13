import {
  Component,
  computed,
  inject,
  output,
  signal,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../../core/services/auth.service';
import { HouseholdStore } from '../../core/services/household-store';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  testId: string;
}

interface NavGroup {
  title: string;
  items: NavItem[];
}

@Component({
  selector: 'nom-nav',
  imports: [RouterLink, RouterLinkActive, MatIconModule, MatTooltipModule],
  templateUrl: './nav.component.html',
  styleUrl: './nav.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Nav {
  private authService = inject(AuthService);
  private householdStore = inject(HouseholdStore);
  private destroyRef = inject(DestroyRef);

  navigated = output<void>();

  isAdmin = this.authService.isAdmin;
  collapsed = signal(localStorage.getItem('nom-nav-collapsed') === 'true');

  private households = toSignal(
    this.householdStore.getHouseholds().pipe(catchError(() => of([]))),
    { initialValue: [] },
  );

  /** A solo user's personal kitchen is labeled "My Kitchen", not "Household". */
  private isPersonalKitchen = computed(() => !!this.households()[0]?.isPersonal);

  readonly home: NavItem = { label: 'Home', icon: 'home', route: '/home', testId: 'nav-home' };

  groups = computed<NavGroup[]>(() => {
    const householdItem: NavItem = this.isPersonalKitchen()
      ? { label: 'My Kitchen', icon: 'person', route: '/household', testId: 'nav-household' }
      : { label: 'Household', icon: 'group', route: '/household', testId: 'nav-household' };
    return this.staticGroups.map((group) =>
      group.title === 'People'
        ? { ...group, items: group.items.map((item) => (item.testId === 'nav-household' ? householdItem : item)) }
        : group,
    );
  });

  private readonly staticGroups: NavGroup[] = [
    {
      title: 'Plan',
      items: [
        { label: 'Meal Plan', icon: 'calendar_month', route: '/plan', testId: 'nav-meal-plan' },
        { label: 'Shopping', icon: 'shopping_cart', route: '/shopping', testId: 'nav-shopping' },
        { label: 'Pantry', icon: 'kitchen', route: '/pantry', testId: 'nav-pantry' },
      ],
    },
    {
      title: 'Cook',
      items: [
        {
          label: 'My Recipes',
          icon: 'menu_book',
          route: '/recipes/mine',
          testId: 'nav-my-recipes',
        },
        {
          label: 'Cookbooks',
          icon: 'collections_bookmark',
          route: '/cookbooks',
          testId: 'nav-cookbooks',
        },
        {
          label: 'Ingredients',
          icon: 'egg',
          route: '/ingredients/mine',
          testId: 'nav-ingredients',
        },
        { label: 'Search', icon: 'search', route: '/search', testId: 'nav-search' },
      ],
    },
    {
      title: 'People',
      items: [
        { label: 'Household', icon: 'group', route: '/household', testId: 'nav-household' },
        { label: 'Messages', icon: 'forum', route: '/messages', testId: 'nav-messages' },
      ],
    },
  ];

  readonly adminItem: NavItem = {
    label: 'Admin',
    icon: 'admin_panel_settings',
    route: '/admin',
    testId: 'nav-admin',
  };

  readonly settingsItem: NavItem = {
    label: 'Settings',
    icon: 'settings',
    route: '/settings',
    testId: 'nav-settings',
  };

  constructor() {
    this.authService.ensureAdminStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }

  toggleCollapsed(): void {
    this.collapsed.update((v) => !v);
    localStorage.setItem('nom-nav-collapsed', String(this.collapsed()));
  }
}
