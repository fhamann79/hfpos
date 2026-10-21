import { SaleDocumentType } from './sale-document.model';
import { SalePaymentMethod } from './sale-payment-method.model';
import { TaxSummary, ProductVatCategory } from '../../../core/utils/vat-category';

export interface CheckoutItemRequest {
  productId: number;
  quantity: number;
  unitPrice: number;
  discountAmount?: number;
}

export interface CheckoutRequest {
  requestId: string;
  fiscalFingerprint?: string;
  cashReceived?: number;
  customerId?: number | null;
  documentType?: SaleDocumentType;
  paymentMethod?: SalePaymentMethod;
  discountAmount?: number;
  notes?: string;
  items: CheckoutItemRequest[];
}

export type SalePreviewRequest = Omit<CheckoutRequest, 'requestId' | 'cashReceived' | 'fiscalFingerprint'>;
export interface SalePreview extends TaxSummary {
  fingerprint: string;
  fiscalDate: string;
  profileRevision: number;
  items: { productId: number; quantity: number; unitPrice: number; discountAmount: number;
    vatCategory: ProductVatCategory; vatRate: number; taxableSubtotal: number; taxAmount: number; lineTotal: number }[];
}
