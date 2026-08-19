import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'nom-footer',
  imports: [RouterLink],
  templateUrl: './footer.component.html',
  styleUrl: './footer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Footer {
  readonly currentYear = new Date().getFullYear();

  /** Operator marketing site (NOM_UI_CONFIG.marketingSite); empty = plain brand text. */
  readonly marketingSite: string =
    (typeof window !== 'undefined' &&
      (window as unknown as { NOM_UI_CONFIG?: { marketingSite?: string } }).NOM_UI_CONFIG?.marketingSite) ||
    '';

  /** Instance-specific terms page (NOM_UI_CONFIG.termsUrl); empty = the built-in generic /terms. */
  readonly termsUrl: string =
    (typeof window !== 'undefined' &&
      (window as unknown as { NOM_UI_CONFIG?: { termsUrl?: string } }).NOM_UI_CONFIG?.termsUrl) ||
    '';
}
