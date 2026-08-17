import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { PlatformFeatureModel } from '../core/models/platform-feature.model';

export interface PlatformFeatureConfirmDialogData {
  feature: PlatformFeatureModel;
  /** The state being switched TO. */
  enabling: boolean;
}

/** True when the admin confirmed the switch. */
export type PlatformFeatureConfirmDialogResult = boolean | undefined;

/**
 * Confirmation for a platform-wide switch: flipping one changes what every
 * user of this instance can reach, so it is never a silent instant toggle.
 */
@Component({
  selector: 'nom-platform-feature-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  templateUrl: './platform-feature-confirm-dialog.component.html',
  styleUrl: './platform-feature-confirm-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformFeatureConfirmDialog {
  data = inject<PlatformFeatureConfirmDialogData>(MAT_DIALOG_DATA);

  /** Human label for the switch, e.g. "brigade" → "Brigade". */
  get featureLabel(): string {
    const key = this.data.feature.key;
    return key.charAt(0).toUpperCase() + key.slice(1);
  }

  /** Plain statement of what changes for real people, not just a flag flip. */
  get consequence(): string {
    return this.data.enabling
      ? `${this.featureLabel} will become available to all providers and clients on this instance.`
      : `${this.featureLabel} will stop being served to everyone except platform admins.`;
  }
}
