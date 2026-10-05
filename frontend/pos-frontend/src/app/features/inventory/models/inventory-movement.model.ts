export enum InventoryMovementType {
  Initial = 0,
  Entry = 1,
  Exit = 2,
  Adjustment = 3,
  Sale = 4,
  Void = 5,
  Return = 6,
}

export enum InventoryMovementSourceType {
  ManualEntry = 1,
  ManualExit = 2,
  ManualAdjustment = 3,
  Sale = 4,
  SaleVoid = 5,
  PurchaseReceipt = 6,
  PurchaseReceiptCancel = 7,
  CreditNoteReturn = 8,
  InventoryTransferOut = 9,
  InventoryTransferIn = 10,
  OpeningInventory = 11,
}

export interface InventoryMovement {
  requestId?: string | null;
  id: number;
  productId: number;
  productName: string;
  type: InventoryMovementType;
  sourceType: InventoryMovementSourceType;
  sourceId: number | null;
  sourceLineId: number | null;
  quantity: number;
  stockBefore: number;
  stockAfter: number;
  reference: string | null;
  notes: string | null;
  userId: number;
  businessDate: string | null;
  timeZoneIdSnapshot: string | null;
  createdAt: string;
}
