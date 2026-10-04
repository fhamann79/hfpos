import { CartItem } from './cart-item.model';
import { CheckoutRequest } from './checkout-request.model';
import { PosCustomer } from './pos-customer.model';
import { SaleDocumentType } from './sale-document.model';
import { SalePaymentMethod } from './sale-payment-method.model';

export interface CheckoutDraft {
  cart: CartItem[];
  customer: PosCustomer | null;
  discountAmount: number;
  notes: string;
  documentType: SaleDocumentType;
  paymentMethod: SalePaymentMethod;
  cashReceived: number | null;
}

export interface CheckoutIntent {
  scope: string;
  request: CheckoutRequest;
  draft: CheckoutDraft;
}
