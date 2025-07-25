// File: nom-ui/src/app/curation/curation.routes.ts

import { Routes } from '@angular/router';
import { CurationQueueComponent } from './components/curation-queue/curation-queue.component';

export const CURATION_ROUTES: Routes = [
  {
    path: '',
    component: CurationQueueComponent,
    title: 'Curation Queue'
  }
];