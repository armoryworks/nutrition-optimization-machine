import { Routes } from '@angular/router';
import { AuthGuard } from '../guards/auth.guard';

export const HOUSEHOLD_ROUTES: Routes = [
    {
        path: '',
        canActivate: [AuthGuard],
        children: [
            {
                path: '',
                loadComponent: () => import('./components/household-dashboard/household-dashboard.component').then(m => m.HouseholdDashboardComponent),
                title: 'Households'
            },
            {
                path: 'create',
                loadComponent: () => import('./components/household-create/household-create.component').then(m => m.HouseholdCreateComponent),
                title: 'Create Household'
            },
            {
                path: ':id',
                loadComponent: () => import('./components/household-detail/household-detail.component').then(m => m.HouseholdDetailComponent),
                title: 'Household Details'
            },
            {
                path: ':id/edit',
                loadComponent: () => import('./components/household-edit/household-edit.component').then(m => m.HouseholdEditComponent),
                title: 'Edit Household'
            },
            {
                path: ':id/invite',
                loadComponent: () => import('./components/household-invite/household-invite.component').then(m => m.HouseholdInviteComponent),
                title: 'Invite Members'
            }
        ]
    }
]; 