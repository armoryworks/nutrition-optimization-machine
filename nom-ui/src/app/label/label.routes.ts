import { Routes } from '@angular/router';
import { AuthGuard } from '../guards/auth.guard';

export const LABEL_ROUTES: Routes = [
    {
        path: '',
        canActivate: [AuthGuard],
        children: [
            {
                path: '',
                loadComponent: () => import('./components/label-dashboard/label-dashboard.component').then(m => m.LabelDashboardComponent),
                title: 'Labels'
            }
        ]
    }
];
