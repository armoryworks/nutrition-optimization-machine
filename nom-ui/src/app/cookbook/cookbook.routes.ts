import { Routes } from '@angular/router';
import { AuthGuard } from '../guards/auth.guard';

export const COOKBOOK_ROUTES: Routes = [
    {
        path: '',
        canActivate: [AuthGuard],
        children: [
            {
                path: '',
                loadComponent: () => import('./components/cookbook-dashboard/cookbook-dashboard.component').then(m => m.CookbookDashboardComponent),
                title: 'Cookbooks'
            },
            {
                path: 'create',
                loadComponent: () => import('./components/cookbook-create/cookbook-create.component').then(m => m.CookbookCreateComponent),
                title: 'Create Cookbook'
            },
            {
                path: ':id',
                loadComponent: () => import('./components/cookbook-detail/cookbook-detail.component').then(m => m.CookbookDetailComponent),
                title: 'Cookbook Details'
            },
            {
                path: ':id/edit',
                loadComponent: () => import('./components/cookbook-edit/cookbook-edit.component').then(m => m.CookbookEditComponent),
                title: 'Edit Cookbook'
            }
        ]
    }
];
