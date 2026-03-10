import { Component, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'nom-footer',
  imports: [],
  templateUrl: './footer.component.html',
  styleUrl: './footer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Footer {
  readonly currentYear = new Date().getFullYear();
}
