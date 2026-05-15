export enum SaleDocumentType {
  Ticket = 0,
  Invoice = 1,
}

export enum SaleDocumentStatus {
  NotRequired = 0,
  Draft = 1,
  PendingAuthorization = 2,
  Authorized = 3,
  Rejected = 4,
  Cancelled = 5,
}

export type DocumentTagSeverity = 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast';

export interface SaleDocumentTypeOption {
  label: string;
  value: SaleDocumentType;
}

export const SALE_DOCUMENT_TYPE_OPTIONS: SaleDocumentTypeOption[] = [
  { label: 'Ticket', value: SaleDocumentType.Ticket },
  { label: 'Factura', value: SaleDocumentType.Invoice },
];

export function normalizeSaleDocumentType(value: unknown): SaleDocumentType {
  if (typeof value === 'number') {
    return value === SaleDocumentType.Invoice ? SaleDocumentType.Invoice : SaleDocumentType.Ticket;
  }

  if (typeof value === 'string') {
    const normalized = value.trim().toUpperCase();
    return normalized === '1' || normalized === 'INVOICE' || normalized === 'FACTURA'
      ? SaleDocumentType.Invoice
      : SaleDocumentType.Ticket;
  }

  return SaleDocumentType.Ticket;
}

export function normalizeSaleDocumentStatus(value: unknown): SaleDocumentStatus {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return Object.values(SaleDocumentStatus).includes(value)
      ? (value as SaleDocumentStatus)
      : SaleDocumentStatus.NotRequired;
  }

  if (typeof value === 'string') {
    const normalized = value.trim().toUpperCase();
    const byName: Record<string, SaleDocumentStatus> = {
      '0': SaleDocumentStatus.NotRequired,
      NOTREQUIRED: SaleDocumentStatus.NotRequired,
      NOT_REQUIRED: SaleDocumentStatus.NotRequired,
      '1': SaleDocumentStatus.Draft,
      DRAFT: SaleDocumentStatus.Draft,
      BORRADOR: SaleDocumentStatus.Draft,
      '2': SaleDocumentStatus.PendingAuthorization,
      PENDINGAUTHORIZATION: SaleDocumentStatus.PendingAuthorization,
      PENDING_AUTHORIZATION: SaleDocumentStatus.PendingAuthorization,
      '3': SaleDocumentStatus.Authorized,
      AUTHORIZED: SaleDocumentStatus.Authorized,
      AUTORIZADO: SaleDocumentStatus.Authorized,
      '4': SaleDocumentStatus.Rejected,
      REJECTED: SaleDocumentStatus.Rejected,
      RECHAZADO: SaleDocumentStatus.Rejected,
      '5': SaleDocumentStatus.Cancelled,
      CANCELLED: SaleDocumentStatus.Cancelled,
      CANCELED: SaleDocumentStatus.Cancelled,
      CANCELADO: SaleDocumentStatus.Cancelled,
    };

    return byName[normalized] ?? SaleDocumentStatus.NotRequired;
  }

  return SaleDocumentStatus.NotRequired;
}

export function saleDocumentTypeLabel(type: SaleDocumentType | number | null | undefined): string {
  return type === SaleDocumentType.Invoice ? 'Factura' : 'Ticket';
}

export function saleDocumentStatusLabel(status: SaleDocumentStatus | number | null | undefined): string {
  switch (status) {
    case SaleDocumentStatus.Draft:
      return 'Borrador';
    case SaleDocumentStatus.PendingAuthorization:
      return 'Pendiente autorización';
    case SaleDocumentStatus.Authorized:
      return 'Autorizado';
    case SaleDocumentStatus.Rejected:
      return 'Rechazado';
    case SaleDocumentStatus.Cancelled:
      return 'Cancelado';
    case SaleDocumentStatus.NotRequired:
    default:
      return 'No requerido';
  }
}

export function saleDocumentStatusSeverity(status: SaleDocumentStatus | number | null | undefined): DocumentTagSeverity {
  switch (status) {
    case SaleDocumentStatus.Draft:
      return 'info';
    case SaleDocumentStatus.PendingAuthorization:
      return 'warn';
    case SaleDocumentStatus.Authorized:
      return 'success';
    case SaleDocumentStatus.Rejected:
      return 'danger';
    case SaleDocumentStatus.Cancelled:
      return 'secondary';
    case SaleDocumentStatus.NotRequired:
    default:
      return 'secondary';
  }
}

export function sriEnvironmentLabel(environment: number | null | undefined): string {
  if (environment === 2) {
    return 'Producción';
  }

  if (environment === 1) {
    return 'Pruebas';
  }

  return 'No aplica';
}

export function sriSignatureStatusLabel(hasSignedXml: boolean, known = true): string {
  if (hasSignedXml) {
    return 'XML firmado';
  }

  return known ? 'Sin firma' : 'Firma en detalle';
}

export function sriSignatureStatusSeverity(hasSignedXml: boolean, known = true): DocumentTagSeverity {
  if (hasSignedXml) {
    return 'success';
  }

  return known ? 'warn' : 'secondary';
}
