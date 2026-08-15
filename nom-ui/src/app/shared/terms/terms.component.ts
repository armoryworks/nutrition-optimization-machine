import { Component, ChangeDetectionStrategy } from '@angular/core';

/**
 * Generic terms of use for a self-hosted NOM instance: liability release and
 * user-conduct boilerplate that holds for any operator. Hosted deployments
 * with their own richer terms point NOM_UI_CONFIG.termsUrl at them and this
 * page is bypassed entirely (see the footer link logic).
 */
@Component({
  selector: 'nom-terms',
  templateUrl: './terms.component.html',
  styleUrl: './terms.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Terms {}
