export type InitialDataKind = 'categories' | 'products' | 'customers' | 'suppliers' | 'opening-inventory';
export interface InitialDataPayload { requestId: string; kind: InitialDataKind; csv: string; duplicatePolicy: 'create-only'; }
export interface InitialDataRow { rowNumber: number; values: Record<string, string>; errors: string[]; resolvedId: number | null; }
export interface InitialDataPreview {
  requestId: string; kind: InitialDataKind; duplicatePolicy: 'create-only'; atomicity: 'atomic-batch';
  maxRows: number; canConfirm: boolean; previewToken: string | null; errors: string[]; rows: InitialDataRow[];
}
export interface InitialDataResult {
  batchId: number; requestId: string; kind: InitialDataKind; companyId: number; establishmentId: number;
  emissionPointId: number; userId: number; rowCount: number; createdIds: number[]; rowNumbers: number[]; createdAt: string;
}
export interface InitialDataRowAudit { rowNumber: number; createdId: number; }
export interface TenantReadiness {
  companyId: number; establishmentId: number; emissionPointId: number; ready: boolean;
  checks: { code: string; ready: boolean; route: string }[];
}
