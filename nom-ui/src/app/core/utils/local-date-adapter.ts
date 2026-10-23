import { Injectable } from '@angular/core';
import { NativeDateAdapter } from '@angular/material/core';
import { parseLocalDateString, toLocalDateString } from './local-date';

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * NativeDateAdapter that reads a typed YYYY-MM-DD as a calendar day in the
 * user's local timezone. (`Date.parse` treats that shape as UTC midnight,
 * which is the previous day in western timezones, so a birthday typed as
 * 1980-10-22 was saved as 1980-10-21.)
 */
@Injectable()
export class LocalDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value === 'string' && ISO_DATE.test(value.trim())) {
      const iso = value.trim();
      const date = parseLocalDateString(iso);
      return toLocalDateString(date) === iso ? date : this.invalid();
    }
    return super.parse(value, parseFormat);
  }
}
