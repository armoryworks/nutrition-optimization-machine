import { Component, Input } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';

// Import the pipe
import { FormatMassPipe } from '../../../common/pipes/format-mass.pipe';
import { NutritionLabelData } from '../../models/nutrition-label-data';

@Component({
  selector: 'app-nutrition-label',
  standalone: true,
  // Add the pipe to the imports array
  imports: [CommonModule, FormatMassPipe],
  // Provide DecimalPipe for the custom pipe to use
  providers: [DecimalPipe],
  templateUrl: './nutrition-label.component.html',
  styleUrls: ['./nutrition-label.component.scss'],
})
export class NutritionLabelComponent {
  @Input() data: NutritionLabelData | null = null;
}