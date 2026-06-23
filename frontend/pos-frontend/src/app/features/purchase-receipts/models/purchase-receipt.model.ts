export enum PurchaseReceiptStatus {
  Posted = 1,
  Canceled = 2,
}

export interface PurchaseReceiptListItem {
  id: number;
  supplierId: number;
  supplierName: string;
  receiptNumber: string | null;
  supplierDocumentNumber: string | null;
  receiptDate: string;
  receiptBusinessDate: string | null;
  receiptTimeZoneIdSnapshot: string | null;
  status: PurchaseReceiptStatus;
  subtotal: number;
  notes: string | null;
  createdAt: string;
  createdByUserId: number;
  createdByUsername: string;
  postedAt: string | null;
  canceledAt: string | null;
  canceledBusinessDate: string | null;
  canceledTimeZoneIdSnapshot: string | null;
  canceledByUserId: number | null;
  canceledByUsername: string | null;
  cancelReason: string | null;
}

export interface PurchaseReceiptItem {
  id: number;
  productId: number;
  productName: string;
  quantity: number;
  unitCost: number;
  lineTotal: number;
  previousProductCost: number;
  appliedProductCost: number;
  notes: string | null;
}

export interface PurchaseReceipt extends PurchaseReceiptListItem {
  items: PurchaseReceiptItem[];
}

export interface CreatePurchaseReceiptItemRequest {
  productId: number;
  quantity: number;
  unitCost: number;
  notes?: string | null;
}

export interface CreatePurchaseReceiptRequest {
  supplierId: number;
  receiptNumber?: string | null;
  supplierDocumentNumber?: string | null;
  receiptDate: string;
  notes?: string | null;
  items: CreatePurchaseReceiptItemRequest[];
}

export interface CancelPurchaseReceiptRequest {
  reason: string;
}

export interface PurchaseReceiptFilters {
  search?: string | null;
  from?: string | null;
  to?: string | null;
  status?: PurchaseReceiptStatus | null;
}
