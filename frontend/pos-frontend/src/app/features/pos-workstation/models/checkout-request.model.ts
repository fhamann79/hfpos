import { SaleDocumentType } from './sale-document.model';
import { SalePaymentMethod } from './sale-payment-method.model';

export interface CheckoutItemRequest {
  productId: number;
  quantity: number;
  unitPrice: number;
  discountAmount?: number;
}

export interface CheckoutRequest {
  customerId?: number | null;
  documentType?: SaleDocumentType;
  paymentMethod?: SalePaymentMethod;
  discountAmount?: number;
  notes?: string;
  items: CheckoutItemRequest[];
}
