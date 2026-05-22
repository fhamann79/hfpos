import { SaleDocumentStatus, SaleDocumentType } from './sale-document.model';

export interface SaleListItem {
  id: number;
  createdAt: string;
  status: string;
  documentType: SaleDocumentType;
  documentStatus: SaleDocumentStatus;
  number: string | null;
  hasSriXmlDraft: boolean;
  hasSriSignedXml: boolean;
  sriSignatureStatusKnown: boolean;
  accessKey: string | null;
  sriSignedAt: string | null;
  sriSubmittedAt: string | null;
  sriReceptionStatus: string | null;
  sriAuthorizationStatus: string | null;
  sriLastSubmissionError: string | null;
  sriLastCheckedAt: string | null;
  total: number;
  createdBy: string | null;
  isVoided: boolean;
}
