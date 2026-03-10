import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'formatDayHeader', standalone: true })
export class FormatDayHeaderPipe implements PipeTransform {
  transform(dateStr: string): string {
    const date = new Date(dateStr + 'T00:00:00');
    return date.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' });
  }
}

@Pipe({ name: 'formatDayShort', standalone: true })
export class FormatDayShortPipe implements PipeTransform {
  transform(dateStr: string): string {
    const date = new Date(dateStr + 'T00:00:00');
    return date.toLocaleDateString(undefined, { weekday: 'short' });
  }
}

@Pipe({ name: 'formatDayNumber', standalone: true })
export class FormatDayNumberPipe implements PipeTransform {
  transform(dateStr: string): string {
    const date = new Date(dateStr + 'T00:00:00');
    return date.toLocaleDateString(undefined, { day: 'numeric' });
  }
}
