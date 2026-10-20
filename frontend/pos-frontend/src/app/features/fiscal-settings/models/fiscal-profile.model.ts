export enum FiscalRegime { General = 0, PopularBusiness = 1, Entrepreneur = 2 }
export enum TaxpayerKind { Natural = 0, Juridical = 1 }
export interface DeclareFiscalProfile {
  expectedRevision: number; regime: FiscalRegime | null; taxpayerKind: TaxpayerKind | null;
  isAccountingRequired: boolean; specialTaxpayerResolution: string | null;
  withholdingAgentResolution: string | null; effectiveFrom: string; effectiveUntil: string | null;
  evidenceReference: string; reason: string; confirmed: boolean; rimpeEligibilityConfirmed: boolean;
}
export interface FiscalProfile extends Omit<DeclareFiscalProfile, 'expectedRevision'> {
  id: number; revision: number; declaredByUserId: number; declaredAt: string;
}
export interface DeclareProductFiscalClassification {
  expectedRevision: number; expectedProductRevision: number | null; kind: number | null; activity: number | null; taxBasis: number | null;
  legalReference: string; evidenceReference: string; sriMaterialCode: string | null;
  naturalAgriculturalFoodConfirmed: boolean; foodProcessing: number | null;
  constructionMaterialConfirmed: boolean; confirmed: boolean;
}
export interface ProductFiscalClassification extends Omit<DeclareProductFiscalClassification, 'expectedRevision' | 'expectedProductRevision'> {
  revision: number; declaredVatCategory: number; declaredByUserId: number; declaredAt: string; isCurrent: boolean;
}
export interface ProductFiscalState {
  productRevision: number; name: string; internalCode: string | null; barcode: string | null;
  vatCategory: number; classification: ProductFiscalClassification | null;
}
export const FISCAL_REGIMES = [
  { label: 'Régimen General', value: FiscalRegime.General },
  { label: 'RIMPE Negocio Popular', value: FiscalRegime.PopularBusiness },
  { label: 'RIMPE Emprendedor', value: FiscalRegime.Entrepreneur },
];
