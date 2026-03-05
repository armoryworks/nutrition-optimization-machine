import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'nom-admin',
  imports: [RouterLink, MatIconModule],
  template: `
    <div class="nom-settings">
      <div class="nom-settings__header">
        <h1 class="nom-settings__title">Administration</h1>
        <p class="nom-settings__subtitle">Manage users, content, and system settings</p>
      </div>

      <div class="nom-settings__grid">
        <a routerLink="/admin/curation" class="nom-settings__card">
          <mat-icon class="nom-settings__card-icon">fact_check</mat-icon>
          <div class="nom-settings__card-content">
            <h3 class="nom-settings__card-title">Curation Queue</h3>
            <p class="nom-settings__card-desc">Review and approve submitted recipes and ingredients</p>
          </div>
          <mat-icon class="nom-settings__card-arrow">chevron_right</mat-icon>
        </a>

        <a routerLink="/admin/webhooks" class="nom-settings__card">
          <mat-icon class="nom-settings__card-icon">webhook</mat-icon>
          <div class="nom-settings__card-content">
            <h3 class="nom-settings__card-title">Webhooks</h3>
            <p class="nom-settings__card-desc">Configure event notifications for your household</p>
          </div>
          <mat-icon class="nom-settings__card-arrow">chevron_right</mat-icon>
        </a>
      </div>
    </div>
  `,
  styleUrl: '../settings/settings.component.scss',
})
export class Admin {}
