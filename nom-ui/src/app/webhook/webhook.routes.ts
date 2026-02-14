import { Routes } from '@angular/router';
import { AuthGuard } from '../guards/auth.guard';

export const WEBHOOK_ROUTES: Routes = [
    {
        path: '',
        canActivate: [AuthGuard],
        children: [
            {
                path: '',
                loadComponent: () => import('./components/webhook-dashboard/webhook-dashboard.component').then(m => m.WebhookDashboardComponent),
                title: 'Webhooks'
            }
        ]
    }
];
