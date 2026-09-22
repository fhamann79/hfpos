import { PagedResultWithSummary } from '../../../core/models/paged-result.model';
import { SaleDocumentStatus } from '../../pos-workstation/models/sale-document.model';

export enum ElectronicDocumentKind {
  Invoice = 1,
  CreditNote = 2,
}

export type ElectronicDocumentSortField =
  | 'documentDate'
  | 'number'
  | 'buyerName'
  | 'kind'
  | 'documentStatus'
  | 'total'
  | 'authorizedAt';

export interface ElectronicDocumentQuery {
  from: string | null;
  to: string | null;
  kind: ElectronicDocumentKind | null;
  documentStatus: SaleDocumentStatus | null;
  search: string | null;
  onlyWithSriError: boolean;
  page: number;
  pageSize: number;
  sortField: ElectronicDocumentSortField;
  sortOrder: 'asc' | 'desc';
}

export interface ElectronicDocumentListItem {
  key: string;
  kind: ElectronicDocumentKind;
  id: number;
  number: string | null;
  businessDate: string;
  documentIssuedAt: string | null;
  createdAt: string;
  buyerName: string | null;
  buyerIdentification: string | null;
  buyerEmail: string | null;
  total: number;
  documentStatus: SaleDocumentStatus;
  accessKey: string | null;
  authorizationNumber: string | null;
  authorizedAt: string | null;
  sriEnvironment: number | null;
  sriSignedAt: string | null;
  sriSubmittedAt: string | null;
  sriReceptionStatus: string | null;
  sriAuthorizationStatus: string | null;
  sriLastSubmissionError: string | null;
  sriLastCheckedAt: string | null;
  hasSriXmlDraft: boolean;
  hasSriSignedXml: boolean;
  originalSaleId: number | null;
  originalSaleNumber: string | null;
}

export interface ElectronicDocumentDetail extends ElectronicDocumentListItem {
  buyerIdentificationType: string | null;
  buyerAddress: string | null;
  sriEmissionType: number | null;
  sriNumericCode: string | null;
  sriXmlGeneratedAt: string | null;
  sriSignatureHash: string | null;
  sriSigningCertificateThumbprint: string | null;
  sriSigningCertificateSubject: string | null;
  sriSigningCertificateSerialNumber: string | null;
  grossSubtotal: number;
  discountAmount: number;
  subtotal: number;
  taxAmount: number;
  reason: string | null;
  notes: string | null;
}

export interface ElectronicDocumentSummary {
  totalDocuments: number;
  invoiceCount: number;
  creditNoteCount: number;
  draftCount: number;
  pendingAuthorizationCount: number;
  authorizedCount: number;
  rejectedCount: number;
  cancelledCount: number;
  withSriErrorCount: number;
}

export type ElectronicDocumentListResult = PagedResultWithSummary<
  ElectronicDocumentListItem,
  ElectronicDocumentSummary
>;

export function electronicDocumentKey(
  document: Pick<ElectronicDocumentListItem, 'kind' | 'id'>
): string {
  return `${document.kind === ElectronicDocumentKind.Invoice ? 'invoice' : 'credit-note'}:${document.id}`;
}

export function electronicDocumentKindLabel(kind: ElectronicDocumentKind): string {
  return kind === ElectronicDocumentKind.CreditNote ? 'Nota de crédito' : 'Factura';
}
