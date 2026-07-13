import { ProductVatCategory } from '../../../core/utils/vat-category';
import { SaleDocumentStatus } from '../../pos-workstation/models/sale-document.model';

export interface CreateCreditNoteDraftRequest {
  originalSaleId: number;
  reason: string;
  notes: string | null;
  items: CreateCreditNoteDraftItemRequest[];
}

export interface CreateCreditNoteDraftItemRequest {
  saleItemId: number;
  quantity: number;
}

export interface CancelCreditNoteDraftRequest {
  reason: string;
}

export interface CreditNoteListItem {
  id: number;
  originalSaleId: number;
  number: string | null;
  establishmentCodeSnapshot: string | null;
  emissionPointCodeSnapshot: string | null;
  documentStatus: SaleDocumentStatus;
  documentIssuedAt: string | null;
  businessDate: string;
  createdAt: string;
  reason: string;
  total: number;
  voidedAt: string | null;
  cancellationReason: string | null;
  cancelledByUserId: number | null;
  cancelledByUsername: string | null;
  canCancelDraft: boolean;
}

export interface CreditNote {
  id: number;
  originalSaleId: number;
  originalSaleNumberSnapshot: string | null;
  originalSaleAccessKeySnapshot: string | null;
  originalSaleAuthorizationNumberSnapshot: string | null;
  originalSaleAuthorizedAtSnapshot: string | null;
  originalSaleDocumentIssuedAtSnapshot: string | null;
  buyerNameSnapshot: string;
  buyerIdentificationTypeSnapshot: string | null;
  buyerIdentificationSnapshot: string | null;
  buyerAddressSnapshot: string | null;
  buyerEmailSnapshot: string | null;
  documentStatus: SaleDocumentStatus;
  number: string | null;
  establishmentCodeSnapshot: string | null;
  emissionPointCodeSnapshot: string | null;
  sequential: number | null;
  documentIssuedAt: string | null;
  reason: string;
  notes: string | null;
  grossSubtotal: number;
  discountAmount: number;
  subtotal: number;
  taxAmount: number;
  vat15Subtotal: number;
  vat5Subtotal: number;
  vat0Subtotal: number;
  vatExemptSubtotal: number;
  vatNotSubjectSubtotal: number;
  total: number;
  businessDate: string;
  timeZoneIdSnapshot: string;
  createdAt: string;
  updatedAt: string | null;
  voidedAt: string | null;
  cancellationReason: string | null;
  cancelledByUserId: number | null;
  cancelledByUsername: string | null;
  items: CreditNoteItem[];
}

export interface CreditNoteItem {
  id: number;
  saleItemId: number | null;
  productId: number;
  productName: string;
  quantity: number;
  unitPrice: number;
  grossSubtotal: number;
  discountAmount: number;
  netSubtotal: number;
  lineSubtotal: number;
  vatCategory: ProductVatCategory;
  vatRate: number;
  taxableSubtotal: number;
  taxAmount: number;
  lineTotal: number;
}
