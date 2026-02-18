import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { AmwIconComponent } from 'angular-material-wrap';

interface QuickAction {
  icon: string;
  label: string;
  route: string;
}

@Component({
  selector: 'nom-sidebar-quick-actions',
  standalone: true,
  imports: [RouterModule, AmwIconComponent],
  templateUrl: './sidebar-quick-actions.component.html',
  styleUrls: ['./sidebar-quick-actions.component.scss'],
})
export class SidebarQuickActionsComponent {
  actions: QuickAction[] = [
    { icon: 'add', label: 'New Meal Plan', route: '/meal-plan/create' },
    { icon: 'menu_book', label: 'New Recipe', route: '/recipes/create' },
    { icon: 'add_shopping_cart', label: 'Shopping List', route: '/shopping/create' },
    { icon: 'search', label: 'Search Recipes', route: '/recipes' },
    { icon: 'groups', label: 'Household', route: '/household' },
    { icon: 'tune', label: 'Restrictions', route: '/person' },
  ];
}
