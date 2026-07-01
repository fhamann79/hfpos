export enum CashSessionStatus {
  Open = 1,
  Closed = 2,
}

export enum CashMovementType {
  CashIn = 1,
  CashOut = 2,
}

export interface CashMovement {
  id: number;
  cashSessionId: number;
  type: CashMovementType;
  amount: number;
  reason: string;
  userId: number;
  username: string;
  createdAt: string;
  businessDate: string;
  timeZoneIdSnapshot: string;
}

export interface CashSessionListItem {
  id: number;
  openedByUserId: number;
  openedByUsername: string;
  closedByUserId: number | null;
  closedByUsername: string | null;
  status: CashSessionStatus;
  openingAmount: number;
  expectedCashAmount: number;
  countedCashAmount: number | null;
  differenceAmount: number | null;
  cashSalesAmount: number;
  cardSalesAmount: number;
  transferSalesAmount: number;
  otherSalesAmount: number;
  cashInAmount: number;
  cashOutAmount: number;
  openedAt: string;
  openBusinessDate: string;
  openTimeZoneIdSnapshot: string;
  closedAt: string | null;
  closedBusinessDate: string | null;
  closedTimeZoneIdSnapshot: string | null;
  openingNotes: string | null;
  closingNotes: string | null;
}

export interface CashSession extends CashSessionListItem {
  companyId: number;
  establishmentId: number;
  emissionPointId: number;
  movements: CashMovement[];
}

export interface CashSessionFilters {
  from?: string | null;
  to?: string | null;
  status?: CashSessionStatus | null;
  userId?: number | null;
}

export interface OpenCashSessionRequest {
  openingAmount: number;
  openingNotes?: string | null;
}

export interface CreateCashMovementRequest {
  type: CashMovementType;
  amount: number;
  reason: string;
}

export interface CloseCashSessionRequest {
  countedCashAmount: number;
  closingNotes?: string | null;
}
