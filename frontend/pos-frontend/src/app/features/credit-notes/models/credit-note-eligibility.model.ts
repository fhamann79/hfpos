import { ProductVatCategory } from '../../../core/utils/vat-category';

export interface CreditNoteEligibility {
  originalSaleId: number;
  originalSaleNumber: string | null;
  originalSaleBusinessDate: string;
  originalSaleDocumentIssuedAt: string | null;
  originalSaleAccessKey: string | null;
  originalSaleAuthorizationNumber: string | null;
  originalSaleAuthorizedAt: string | null;
  buyerName: string;
  buyerIdentificationType: string | null;
  buyerIdentification: string | null;
  buyerAddress: string | null;
  buyerEmail: string | null;
  originalTotal: number;
  isEligible: boolean;
  blockingCode: string | null;
  blockingMessage: string | null;
  items: CreditNoteEligibilityItem[];
}

export interface CreditNoteEligibilityItem {
  saleItemId: number;
  productId: number;
  productName: string;
  soldQuantity: number;
  creditedQuantity: number;
  availableQuantity: number;
  unitPrice: number;
  discountAmount: number;
  vatCategory: ProductVatCategory;
  vatRate: number;
  taxableSubtotal: number;
  taxAmount: number;
  lineTotal: number;
}
