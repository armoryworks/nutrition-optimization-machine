// File: nom-ui/src/app/admin/admin.routes.ts

import { Routes } from '@angular/router';
import { UserManagementComponent } from './components/user-management/user-management.component';

export const ADMIN_ROUTES: Routes = [
  {
    path: 'user-management', // e.g., /admin/user-management
    component: UserManagementComponent,
    title: 'User Management'
  },
  {
    path: '',
    redirectTo: 'user-management', // Default admin view
    pathMatch: 'full'
  }
];