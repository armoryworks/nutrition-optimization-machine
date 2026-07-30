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
