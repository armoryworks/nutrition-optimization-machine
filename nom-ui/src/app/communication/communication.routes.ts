// File: nom-ui/src/app/communication/communication.routes.ts

import { Routes } from '@angular/router';
import { MessagingInboxComponent } from './components/messaging-inbox/messaging-inbox.component';

export const COMMUNICATION_ROUTES: Routes = [
  {
    path: '',
    component: MessagingInboxComponent,
    title: 'Inbox'
  },
  {
    path: 'new',
    component: MessagingInboxComponent, // For now, redirect to inbox - can be replaced with a create component later
    title: 'New Conversation'
  },
  {
    path: 'thread/:id',
    component: MessagingInboxComponent, // For now, redirect to inbox - can be replaced with a thread component later
    title: 'Conversation'
  }
];