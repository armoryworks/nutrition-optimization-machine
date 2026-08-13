import { Component, inject, input, computed, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { EnrollmentService } from '../../../core/services/enrollment.service';
import { HouseholdResponseModel } from '../../../core/models/household-response.model';

/**
 * Prominent per-adult consent prompt for externally managed households:
 * shown until the member accepts (or dismisses it) and only when a Brigade
 * API is configured.
 */
@Component({
  selector: 'nom-enrollment-consent-banner',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './enrollment-consent-banner.component.html',
  styleUrl: './enrollment-consent-banner.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EnrollmentConsentBanner {
  household = input<HouseholdResponseModel | null>(null);

  private enrollmentService = inject(EnrollmentService);

  visible = computed(() => {
    const household = this.household();
    return !!household && this.enrollmentService.needsConsentBanner(household);
  });

  dismiss(): void {
    const household = this.household();
    if (household) {
      this.enrollmentService.dismissBanner(household.id);
    }
  }
}
