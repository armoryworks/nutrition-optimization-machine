import { Pipe, PipeTransform } from '@angular/core';

/**
 * A common pipe to parse JSON strings directly within Angular templates.
 * Useful for handling stringified JSON data from backend.
 */
@Pipe({
  name: 'jsonParseCommon', // Renamed pipe selector
  standalone: true,
})
export class JsonParseCommonPipe implements PipeTransform {
  // Renamed class
  transform(value: string | null | undefined): any {
    if (typeof value === 'string' && value.trim() !== '') {
      try {
        return JSON.parse(value);
      } catch (e) {
        // console.error('Error parsing JSON string in jsonParseCommonPipe:', value, e); // Log for debugging if needed
        return null;
      }
    }
    return null;
  }
}
