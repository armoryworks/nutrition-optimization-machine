import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'nom-admin',
  imports: [RouterLink, MatIconModule],
  templateUrl: './admin.component.html',
  styleUrl: '../settings/settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Admin {}
