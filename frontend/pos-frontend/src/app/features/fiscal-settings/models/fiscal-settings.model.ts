export enum FiscalDocumentType {
  Ticket = 0,
  Invoice = 1,
}

export interface CompanyFiscalSettings {
  companyId: number;
  name: string;
  tradeName: string | null;
  ruc: string;
  matrixAddress: string | null;
  email: string | null;
  phone: string | null;
  isAccountingRequired: boolean;
  specialTaxpayerNumber: string | null;
  taxpayerRegime: string | null;
  isActive: boolean;
}

export interface UpdateCompanyFiscalSettingsRequest {
  name: string;
  tradeName: string | null;
  ruc: string;
  matrixAddress: string | null;
  email: string | null;
  phone: string | null;
  isAccountingRequired: boolean;
  specialTaxpayerNumber: string | null;
  taxpayerRegime: string | null;
}

export interface CompanySriSettings {
  companyId: number;
  environment: number;
  emissionType: number;
  isEnabled: boolean;
  certificateConfigured: boolean;
  certificateExpiresAt: string | null;
  updatedAt: string | null;
}

export interface CompanySriCertificate {
  companyId: number;
  certificateConfigured: boolean;
  fileName: string;
  thumbprint: string;
  subject: string;
  issuer: string;
  serialNumber: string;
  notBefore: string;
  notAfter: string;
  hasPrivateKey: boolean;
  uploadedAt: string;
  uploadedByUserId: number;
  isActive: boolean;
  daysUntilExpiration: number;
  isExpired: boolean;
}

export interface UpdateCompanySriSettingsRequest {
  environment: number;
  emissionType: number;
  isEnabled: boolean;
}

export interface DocumentSequence {
  id: number;
  companyId: number;
  establishmentId: number;
  establishmentCode: string;
  establishmentName: string;
  emissionPointId: number;
  emissionPointCode: string;
  emissionPointName: string;
  documentType: FiscalDocumentType;
  currentNumber: number;
  nextNumber: number;
  createdAt: string;
  updatedAt: string;
  maxUsedSequential: number;
}

export interface DocumentSequenceFilters {
  establishmentId?: number | null;
  emissionPointId?: number | null;
  documentType?: FiscalDocumentType | null;
}

export interface CreateDocumentSequenceRequest {
  establishmentId: number;
  emissionPointId: number;
  documentType: FiscalDocumentType;
  nextNumber: number;
  reason: string;
}

export interface UpdateDocumentSequenceRequest {
  nextNumber: number;
  reason: string;
}

export interface DocumentSequenceAudit {
  id: number;
  documentSequenceId: number;
  documentType: FiscalDocumentType;
  previousCurrentNumber: number | null;
  newCurrentNumber: number;
  previousNextNumber: number | null;
  newNextNumber: number;
  reason: string;
  userId: number;
  createdAt: string;
}

export interface SelectOption<T> {
  label: string;
  value: T;
}

export function formatFiscalSequential(value: number | null | undefined): string {
  return Math.max(value ?? 0, 0).toString().padStart(9, '0');
}

export function fiscalDocumentTypeLabel(value: FiscalDocumentType): string {
  return value === FiscalDocumentType.Invoice ? 'Factura' : 'Ticket';
}

export function certificateStatusLabel(certificate: CompanySriCertificate | null | undefined): string {
  if (!certificate?.isActive) {
    return 'No configurado';
  }

  if (certificate.isExpired) {
    return 'Vencido';
  }

  return certificate.daysUntilExpiration <= 30 ? 'Próximo a vencer' : 'Activo';
}

export function certificateSeverity(
  certificate: CompanySriCertificate | null | undefined,
): 'success' | 'secondary' | 'warn' | 'danger' {
  if (!certificate?.isActive) {
    return 'secondary';
  }

  if (certificate.isExpired) {
    return 'danger';
  }

  return certificate.daysUntilExpiration <= 30 ? 'warn' : 'success';
}
