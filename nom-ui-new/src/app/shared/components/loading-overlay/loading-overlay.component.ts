import { Component, computed, inject, input } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { LoadingService } from '../../../core/services/loading.service';

@Component({
  selector: 'nom-loading-overlay',
  imports: [MatProgressSpinnerModule],
  templateUrl: './loading-overlay.component.html',
  styleUrl: './loading-overlay.component.scss',
  host: {
    '[class.nom-loading-overlay--visible]': 'visible()',
  },
})
export class LoadingOverlay {
  private loadingService = inject(LoadingService);

  visible = input(false);
  messages = computed(() => this.loadingService.messages());
}
