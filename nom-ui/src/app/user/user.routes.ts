// File: nom-ui/src/app/user/user.routes.ts

import { Routes } from '@angular/router';
import { PrivacySettingsComponent } from './components/privacy-settings/privacy-settings.component';
import { RecipeAuthorDashboardComponent } from './components/recipe-author-dashboard/recipe-author-dashboard.component';

export const USER_ROUTES: Routes = [
  {
    path: 'dashboard',
    component: RecipeAuthorDashboardComponent,
    title: 'My Dashboard'
  },
  {
    path: 'privacy-settings',
    component: PrivacySettingsComponent,
    title: 'Privacy Settings'
  },
  {
    path: '', // Default route for the /user path
    redirectTo: 'dashboard',
    pathMatch: 'full'
  }
];