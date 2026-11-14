/** yyyy-MM-dd in the user's local time zone (toISOString() would give the UTC date). */
export function localDate(date: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Server timestamps are UTC; older responses may lack the trailing Z. */
export function asUtc(value: string | null | undefined): string {
  if (!value) return '';
  return /Z$|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z';
}
