import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'formatQuantity', standalone: true })
export class FormatQuantityPipe implements PipeTransform {
  transform(qty: number): string {
    return qty % 1 === 0 ? qty.toString() : qty.toFixed(2).replace(/0+$/, '');
  }
}
