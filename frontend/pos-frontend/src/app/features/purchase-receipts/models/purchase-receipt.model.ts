export enum PurchaseReceiptStatus {
  Posted = 1,
}

export interface PurchaseReceiptListItem {
  id: number;
  supplierId: number;
  supplierName: string;
  receiptNumber: string | null;
  supplierDocumentNumber: string | null;
  receiptDate: string;
  status: PurchaseReceiptStatus;
  subtotal: number;
  notes: string | null;
  createdAt: string;
  createdByUserId: number;
  createdByUsername: string;
  postedAt: string | null;
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

export interface PurchaseReceiptFilters {
  search?: string | null;
  from?: string | null;
  to?: string | null;
}
