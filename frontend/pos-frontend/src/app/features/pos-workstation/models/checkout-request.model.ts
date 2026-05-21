import { SaleDocumentType } from './sale-document.model';

export interface CheckoutItemRequest {
  productId: number;
  quantity: number;
  unitPrice: number;
  discountAmount?: number;
}

export interface CheckoutRequest {
  customerId?: number | null;
  documentType?: SaleDocumentType;
  discountAmount?: number;
  notes?: string;
  items: CheckoutItemRequest[];
}
