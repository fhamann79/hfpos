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
