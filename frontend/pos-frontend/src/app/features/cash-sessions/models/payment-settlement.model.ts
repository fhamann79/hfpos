import { PagedResult } from '../../../core/models/paged-result.model';

export enum SettlementPaymentMethod {
  Cash = 0,
  Card = 1,
  Transfer = 2,
  Other = 3,
}

export interface PaymentSettlement {
  id: number;
  businessDate: string;
  paymentMethod: SettlementPaymentMethod;
  grossSalesAmount: number;
  voidAmount: number;
  refundAmount: number;
  expectedNetAmount: number;
  settledAmount: number;
  differenceAmount: number;
  requestId: string;
  reference: string | null;
  notes: string | null;
  reconciledByUserId: number;
  reconciledByUsername: string;
  reconciledAt: string;
  timeZoneIdSnapshot: string;
  wasAlreadyProcessed: boolean;
}

export interface PaymentMethodActivity {
  paymentMethod: SettlementPaymentMethod;
  grossSalesAmount: number;
  voidAmount: number;
  refundAmount: number;
  netPaymentAmount: number;
  canSettle: boolean;
  settlement: PaymentSettlement | null;
}

export interface PaymentReconciliation {
  businessDate: string;
  currentBusinessDate: string;
  legacyUnattributedVoidCount: number;
  methods: PaymentMethodActivity[];
}

export interface CreatePaymentSettlementRequest {
  requestId: string;
  businessDate: string;
  paymentMethod: SettlementPaymentMethod;
  settledAmount: number;
  reference: string | null;
  notes: string | null;
}

export interface PaymentSettlementFilters {
  from?: string;
  to?: string;
  paymentMethod?: SettlementPaymentMethod | null;
  page?: number;
  pageSize?: number;
}

export type PaymentSettlementPage = PagedResult<PaymentSettlement>;
