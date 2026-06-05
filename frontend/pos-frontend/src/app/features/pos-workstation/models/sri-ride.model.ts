export interface SriRide {
  saleId: number;
  documentTypeLabel: string;
  documentNumber: string | null;
  accessKey: string | null;
  qr?: SriRideQr | null;
  authorizationNumber: string | null;
  authorizationDate: string | null;
  environmentLabel: string | null;
  emissionTypeLabel: string | null;
  issueDate: string | null;
  issuer: SriRideIssuer;
  buyer: SriRideBuyer;
  branding: SriRideBranding;
  items: SriRideItem[];
  totals: SriRideTotals;
  payments: SriRidePayment[];
  additionalInfo: SriRideAdditionalInfo[];
  footerNote: string;
}

export interface SriRideIssuer {
  ruc: string | null;
  legalName: string | null;
  tradeName: string | null;
  matrixAddress: string | null;
  establishmentAddress: string | null;
  accountingRequired: string | null;
  taxpayerRegime: string | null;
}

export interface SriRideBuyer {
  identificationType: string | null;
  identification: string | null;
  legalName: string | null;
}

export interface SriRideQr {
  content: string | null;
  dataUrl: string | null;
}

export interface SriRideBranding {
  logoConfigured: boolean;
  logoContentType: string | null;
  logoDataUrl: string | null;
  primaryColor: string | null;
  documentFooterText: string | null;
}

export interface SriRideItem {
  mainCode: string | null;
  description: string | null;
  quantity: number;
  unitPrice: number;
  discount: number;
  subtotal: number;
  taxAmount: number;
  lineTotal: number;
}

export interface SriRideTotals {
  subtotalWithoutTaxes: number;
  totalDiscount: number;
  vat15Subtotal: number;
  vat5Subtotal: number;
  vat0Subtotal: number;
  exemptSubtotal: number;
  notSubjectSubtotal: number;
  taxAmount: number;
  total: number;
  currency: string;
}

export interface SriRidePayment {
  paymentMethod: string | null;
  amount: number;
}

export interface SriRideAdditionalInfo {
  name: string;
  value: string;
}
