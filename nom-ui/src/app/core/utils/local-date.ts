/**
 * Formats a Date as YYYY-MM-DD in the user's local timezone.
 * (`toISOString()` converts to UTC, which makes "today" wrong in the
 * evening for western timezones.)
 */
export function toLocalDateString(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/**
 * Parses a YYYY-MM-DD string as midnight in the user's local timezone.
 * (`new Date('YYYY-MM-DD')` parses as UTC midnight, which is the previous
 * day in western timezones.)
 */
export function parseLocalDateString(value: string): Date {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) {
    return new Date(value);
  }
  return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
}

/**
 * Formats a YYYY-MM-DD string the way people say it ("Mon, Oct 12") in the
 * user's locale, on the same calendar day in every timezone.
 */
export function formatFriendlyDate(value: string, locale?: string): string {
  return parseLocalDateString(value).toLocaleDateString(locale, {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
  });
}
