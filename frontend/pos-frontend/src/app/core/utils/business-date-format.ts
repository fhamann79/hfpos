const DEFAULT_BUSINESS_TIME_ZONE = 'America/Guayaquil';
const BUSINESS_LOCALE = 'es-EC';

export function formatBusinessDateTime(value: string | Date | null | undefined, timeZoneId: string): string {
  const date = parseInstant(value);

  if (!date) {
    return '';
  }

  return `${formatBusinessDate(date, timeZoneId)} ${formatBusinessTime(date, timeZoneId)}`.trim();
}

export function formatBusinessDate(value: string | Date | null | undefined, timeZoneId: string): string {
  const date = parseInstant(value);

  if (!date) {
    return '';
  }

  return createFormatter(timeZoneId, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(date);
}

export function formatBusinessTime(value: string | Date | null | undefined, timeZoneId: string): string {
  const date = parseInstant(value);

  if (!date) {
    return '';
  }

  return createFormatter(timeZoneId, {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(date);
}

export function formatBusinessDateInput(value: string | Date | null | undefined, timeZoneId: string): string {
  const date = parseInstant(value);

  if (!date) {
    return '';
  }

  const parts = createFormatter(timeZoneId, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).formatToParts(date);

  const year = parts.find((part) => part.type === 'year')?.value ?? '';
  const month = parts.find((part) => part.type === 'month')?.value ?? '';
  const day = parts.find((part) => part.type === 'day')?.value ?? '';

  return year && month && day ? `${year}-${month}-${day}` : '';
}

function parseInstant(value: string | Date | null | undefined): Date | null {
  if (!value) {
    return null;
  }

  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

function createFormatter(timeZoneId: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  try {
    return new Intl.DateTimeFormat(BUSINESS_LOCALE, {
      ...options,
      timeZone: timeZoneId || DEFAULT_BUSINESS_TIME_ZONE,
    });
  } catch {
    return new Intl.DateTimeFormat(BUSINESS_LOCALE, {
      ...options,
      timeZone: DEFAULT_BUSINESS_TIME_ZONE,
    });
  }
}
