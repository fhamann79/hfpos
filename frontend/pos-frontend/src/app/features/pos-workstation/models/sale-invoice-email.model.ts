import { DocumentTagSeverity } from './sale-document.model';

export interface SendSaleInvoiceEmailRequest {
  toEmail: string;
  ccEmail?: string | null;
  subject?: string | null;
  message?: string | null;
}

export interface SendSaleInvoiceEmailResult {
  success: boolean;
  message: string;
  sentAt: string;
  toEmail: string;
  ccEmail: string | null;
  documentNumber: string | null;
  authorizationNumber: string | null;
}

export interface SendCreditNoteEmailRequest {
  toEmail: string;
  ccEmail?: string | null;
  subject?: string | null;
  message?: string | null;
}

export interface SendCreditNoteEmailResult {
  success: boolean;
  message: string;
  sentAt: string;
  toEmail: string;
  ccEmail: string | null;
  documentNumber: string | null;
  authorizationNumber: string | null;
}

export interface SaleInvoiceEmailDelivery {
  id: number;
  saleId: number | null;
  creditNoteId: number | null;
  toEmail: string;
  ccEmail: string | null;
  subject: string;
  status: string;
  sentAt: string | null;
  createdAt: string;
  createdByUserId: number;
  documentNumberSnapshot: string | null;
  authorizationNumberSnapshot: string | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export function saleInvoiceEmailDeliveryStatusLabel(status: string | null | undefined): string {
  switch (normalizeDeliveryStatus(status)) {
    case 'SUCCEEDED':
      return 'Enviado';
    case 'FAILED':
      return 'Fallido';
    default:
      return status?.trim() || 'Desconocido';
  }
}

export function saleInvoiceEmailDeliveryStatusSeverity(status: string | null | undefined): DocumentTagSeverity {
  switch (normalizeDeliveryStatus(status)) {
    case 'SUCCEEDED':
      return 'success';
    case 'FAILED':
      return 'danger';
    default:
      return 'secondary';
  }
}

function normalizeDeliveryStatus(status: string | null | undefined): string {
  return status?.trim().toUpperCase() ?? '';
}
