// File: nom-ui/src/app/communication/communication.routes.ts

import { Routes } from '@angular/router';
import { MessagingInboxComponent } from './components/messaging-inbox/messaging-inbox.component';

export const COMMUNICATION_ROUTES: Routes = [
  {
    path: '',
    component: MessagingInboxComponent,
    title: 'Inbox'
  }
];