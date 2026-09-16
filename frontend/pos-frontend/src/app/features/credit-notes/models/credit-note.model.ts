import { ProductVatCategory } from '../../../core/utils/vat-category';
import { SaleDocumentStatus } from '../../pos-workstation/models/sale-document.model';
import { SalePaymentMethod } from '../../pos-workstation/models/sale-payment-method.model';

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

export interface ReturnCreditNoteInventoryRequest {
  notes: string | null;
}

export interface RefundCreditNoteRequest {
  method: SalePaymentMethod;
  reference: string | null;
  notes: string | null;
}

export interface CreditNoteRefund {
  id: number;
  creditNoteId: number;
  method: SalePaymentMethod;
  amount: number;
  refundedAt: string;
  businessDate: string;
  timeZoneIdSnapshot: string;
  refundedByUserId: number;
  refundedByUsername: string;
  cashSessionId: number | null;
  cashMovementId: number | null;
  reference: string | null;
  notes: string | null;
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
  originalSalePaymentMethod: SalePaymentMethod;
  hasFinancialRefund: boolean;
  financialRefund: CreditNoteRefund | null;
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
  accessKey: string | null;
  authorizationNumber: string | null;
  authorizedAt: string | null;
  sriEnvironment: number | null;
  sriEmissionType: number | null;
  sriNumericCode: string | null;
  hasSriXmlDraft: boolean;
  sriXmlGeneratedAt: string | null;
  sriSignedAt: string | null;
  hasSriSignedXml: boolean;
  sriSignatureHash: string | null;
  sriSigningCertificateThumbprint: string | null;
  sriSigningCertificateSubject: string | null;
  sriSigningCertificateSerialNumber: string | null;
  sriSubmittedAt: string | null;
  sriReceptionStatus: string | null;
  sriAuthorizationStatus: string | null;
  sriLastSubmissionError: string | null;
  sriLastCheckedAt: string | null;
  hasInventoryReturn: boolean;
  inventoryReturnedAt: string | null;
  inventoryReturnedByUserId: number | null;
  inventoryReturnedByUsername: string | null;
  inventoryReturnNotes: string | null;
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
  productMainCode: string;
  productAuxiliaryCode: string | null;
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

export function isCreditNoteRefundEligible(note: CreditNote): boolean {
  return !note.hasFinancialRefund
    && !note.financialRefund
    && note.voidedAt === null
    && note.documentStatus !== SaleDocumentStatus.Cancelled
    && note.documentStatus !== SaleDocumentStatus.Rejected
    && (note.documentStatus === SaleDocumentStatus.Authorized
      || note.sriAuthorizationStatus?.trim().toUpperCase() === 'AUTORIZADO')
    && !!note.authorizationNumber?.trim()
    && !!note.accessKey?.trim();
}
