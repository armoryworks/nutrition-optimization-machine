import { Component } from '@angular/core';
import { SidebarUpcomingMealsComponent } from '../sidebar-upcoming-meals/sidebar-upcoming-meals.component';
import { SidebarShoppingListsComponent } from '../sidebar-shopping-lists/sidebar-shopping-lists.component';
import { SidebarHouseholdContextComponent } from '../sidebar-household-context/sidebar-household-context.component';
import { SidebarQuickActionsComponent } from '../sidebar-quick-actions/sidebar-quick-actions.component';
import { SidebarRestrictionsComponent } from '../sidebar-restrictions/sidebar-restrictions.component';

@Component({
  selector: 'nom-context-sidebar',
  standalone: true,
  imports: [
    SidebarUpcomingMealsComponent,
    SidebarShoppingListsComponent,
    SidebarHouseholdContextComponent,
    SidebarQuickActionsComponent,
    SidebarRestrictionsComponent,
  ],
  templateUrl: './context-sidebar.component.html',
  styleUrls: ['./context-sidebar.component.scss'],
})
export class ContextSidebarComponent {}
