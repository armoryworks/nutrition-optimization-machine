import { Pipe, PipeTransform, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';

@Pipe({
  name: 'formatMass',
  standalone: true,
})
export class FormatMassPipe implements PipeTransform {
  private decimalPipe = inject(DecimalPipe);





  transform(value: number, unitName: string): string {
    const massUnits = ['kg', 'g', 'mg', 'µg', 'mcg'];

    // If the unit is not a mass unit we handle, return it as is.
    if (!massUnits.includes(unitName)) {
      return `${this.decimalPipe.transform(value, '1.0-2')} ${unitName}`;
    }

    // 1. Convert the incoming value to a base unit of grams.
    let grams: number;
    switch (unitName) {
      case 'kg':
        grams = value * 1000;
        break;
      case 'mg':
        grams = value / 1000;
        break;
      case 'µg':
      case 'mcg':
        grams = value / 1_000_000;
        break;
      default: // 'g'
        grams = value;
        break;
    }

    // 2. Determine the best display unit based on the gram value.
    if (grams >= 1000) {
      return `${this.decimalPipe.transform(grams / 1000, '1.0-2')} kg`;
    }
    if (grams >= 1) {
      return `${this.decimalPipe.transform(grams, '1.0-2')} g`;
    }
    if (grams >= 0.001) {
      return `${this.decimalPipe.transform(grams * 1000, '1.0-2')} mg`;
    }
    if (grams > 0) {
      // Use the proper 'µg' symbol for microgram output
      return `${this.decimalPipe.transform(grams * 1_000_000, '1.0-2')} µg`;
    }

    return `0 g`;
  }
}