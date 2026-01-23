// File: nom-ui/src/app/communication/communication.routes.ts

import { Routes } from '@angular/router';
import { MessagingInboxComponent } from './components/messaging-inbox/messaging-inbox.component';
import { MessageThreadDetailComponent } from './components/message-thread-detail/message-thread-detail.component';
import { MessageComposeComponent } from './components/message-compose/message-compose.component';

export const COMMUNICATION_ROUTES: Routes = [
  {
    path: '',
    component: MessagingInboxComponent,
    title: 'Inbox'
  },
  {
    path: 'new',
    component: MessageComposeComponent,
    title: 'New Conversation'
  },
  {
    path: 'thread/:id',
    component: MessageThreadDetailComponent,
    title: 'Conversation'
  }
];