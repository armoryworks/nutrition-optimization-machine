import { Component, ChangeDetectionStrategy, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { CurationQueue } from './curation-queue.component';
import { ScrapingSources } from './scraping-sources.component';
import { Webhooks } from './webhooks.component';
import { UsersAdmin } from './users-admin.component';
import { ClientsAdmin } from './clients-admin.component';
import { DietCategories } from './diet-categories.component';
import { FoodCatalog } from './food-catalog.component';
import { PlatformFeatures } from './platform-features.component';

// URL-driven tabbed shell (the forge-ui admin idiom): /admin/:tab selects the
// panel; unknown tabs fall back to the overview launchpad.
const VALID_TABS = ['overview', 'users', 'clients', 'curation', 'food-catalog', 'scraping-sources', 'diet-categories', 'platform-features', 'webhooks'] as const;
type AdminTab = (typeof VALID_TABS)[number];

interface AdminTabDef {
  id: AdminTab;
  label: string;
  icon: string;
  description: string;
  /** Overrides the default 'nav-admin-{id}' nav test id. */
  navTestId?: string;
}

@Component({
  selector: 'nom-admin',
  imports: [RouterLink, MatIconModule, CurationQueue, ScrapingSources, Webhooks, UsersAdmin, ClientsAdmin, DietCategories, FoodCatalog, PlatformFeatures],
  templateUrl: './admin.component.html',
  styleUrls: ['../settings/settings.component.scss', './admin.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Admin {
  private route = inject(ActivatedRoute);

  private tabParam = toSignal(this.route.paramMap.pipe(map((p) => p.get('tab'))), {
    initialValue: null as string | null,
  });

  activeTab = computed<AdminTab>(() => {
    const tab = this.tabParam();
    return (VALID_TABS as readonly string[]).includes(tab ?? '') ? (tab as AdminTab) : 'overview';
  });

  readonly tabs: AdminTabDef[] = [
    { id: 'overview', label: 'Overview', icon: 'dashboard', description: 'Admin launchpad' },
    { id: 'users', label: 'Users', icon: 'group', description: 'Accounts and admin claims' },
    {
      id: 'clients',
      label: 'Clients',
      icon: 'diversity_3',
      description: 'Households on this instance and who is in them',
    },
    {
      id: 'curation',
      label: 'Curation',
      icon: 'fact_check',
      description: 'Review submitted recipes and ingredients',
    },
    {
      id: 'food-catalog',
      label: 'Food Catalog',
      icon: 'nutrition',
      description: 'Review imported foods and approve them for meal planning',
      navTestId: 'nav-food-catalog',
    },
    {
      id: 'scraping-sources',
      label: 'Scraping Sources',
      icon: 'travel_explore',
      description: 'Approve domains recipes may be imported from',
      navTestId: 'nav-scraping-sources',
    },
    {
      id: 'diet-categories',
      label: 'Diet Categories',
      icon: 'health_and_safety',
      description: 'Health conditions, diets, and the filter criteria behind them',
      navTestId: 'nav-diet-categories',
    },
    {
      id: 'platform-features',
      label: 'Platform Features',
      icon: 'toggle_on',
      description: 'Turn whole subsystems on or off for this instance',
      navTestId: 'nav-platform-features',
    },
    {
      id: 'webhooks',
      label: 'Webhooks',
      icon: 'webhook',
      description: 'Event notifications for your household',
    },
  ];

  readonly overviewCards = this.tabs.filter((t) => t.id !== 'overview');
}
