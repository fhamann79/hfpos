import {
  DocumentTagSeverity,
  SaleDocumentStatus,
  SaleDocumentType,
  normalizeSaleDocumentStatus,
  normalizeSaleDocumentType,
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
  saleDocumentTypeLabel,
} from '../../pos-workstation/models/sale-document.model';
import { normalizeVatCategory, getVatCategoryOption } from '../../../core/utils/vat-category';

export enum SaleStatus {
  Draft = 0,
  Completed = 1,
  Voided = 2,
}

export interface SalesReportFilters {
  from?: string | null;
  to?: string | null;
  status?: SaleStatus | null;
  search?: string | null;
  documentType?: SaleDocumentType | null;
  documentStatus?: SaleDocumentStatus | null;
}

export interface SalesReportRow {
  id: number;
  businessDate: string | null;
  timeZoneIdSnapshot: string | null;
  createdAt: string;
  status: SaleStatus;
  number: string | null;
  customerName: string | null;
  customerIdentification: string | null;
  customerEmail: string | null;
  documentType: SaleDocumentType;
  documentStatus: SaleDocumentStatus;
  sriAuthorizationStatus: string | null;
  total: number;
  totalCost: number;
  grossProfit: number;
  grossMarginPercent: number;
  itemsCount: number;
  userId: number;
  username: string | null;
  notes: string | null;
}

export interface SalesReportDetailItem {
  id: number;
  productId: number;
  productName: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  taxableSubtotal: number;
  taxAmount: number;
  lineTotal: number;
  unitCost: number;
  lineCost: number;
  grossProfit: number;
  grossMarginPercent: number;
  vatCategory: number;
}

export interface SalesReportDetail extends SalesReportRow {
  buyerNameSnapshot: string | null;
  buyerIdentificationTypeSnapshot: string | null;
  buyerIdentificationSnapshot: string | null;
  buyerAddressSnapshot: string | null;
  buyerEmailSnapshot: string | null;
  paymentMethod: number;
  accessKey: string | null;
  authorizationNumber: string | null;
  authorizedAt: string | null;
  grossSubtotal: number;
  discountAmount: number;
  subtotal: number;
  taxAmount: number;
  items: SalesReportDetailItem[];
}

export function normalizeSaleStatus(value: unknown): SaleStatus {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return Object.values(SaleStatus).includes(value) ? (value as SaleStatus) : SaleStatus.Completed;
  }

  if (typeof value === 'string') {
    const normalized = value.trim().toUpperCase();
    const byName: Record<string, SaleStatus> = {
      '0': SaleStatus.Draft,
      DRAFT: SaleStatus.Draft,
      BORRADOR: SaleStatus.Draft,
      '1': SaleStatus.Completed,
      COMPLETED: SaleStatus.Completed,
      COMPLETADA: SaleStatus.Completed,
      '2': SaleStatus.Voided,
      VOIDED: SaleStatus.Voided,
      ANULADA: SaleStatus.Voided,
    };

    return byName[normalized] ?? SaleStatus.Completed;
  }

  return SaleStatus.Completed;
}

export function saleStatusLabel(status: SaleStatus | number): string {
  switch (status) {
    case SaleStatus.Draft:
      return 'Borrador';
    case SaleStatus.Voided:
      return 'Anulada';
    case SaleStatus.Completed:
    default:
      return 'Completada';
  }
}

export function saleStatusSeverity(status: SaleStatus | number): DocumentTagSeverity {
  switch (status) {
    case SaleStatus.Draft:
      return 'info';
    case SaleStatus.Voided:
      return 'danger';
    case SaleStatus.Completed:
    default:
      return 'success';
  }
}

export {
  SaleDocumentStatus,
  SaleDocumentType,
  getVatCategoryOption,
  normalizeSaleDocumentStatus,
  normalizeSaleDocumentType,
  normalizeVatCategory,
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
  saleDocumentTypeLabel,
};
