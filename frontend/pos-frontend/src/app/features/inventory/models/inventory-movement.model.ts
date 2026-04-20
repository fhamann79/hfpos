export enum InventoryMovementType {
  Initial = 0,
  Entry = 1,
  Exit = 2,
  Adjustment = 3,
  Sale = 4,
  Void = 5,
}

export enum InventoryMovementSourceType {
  ManualEntry = 1,
  ManualExit = 2,
  ManualAdjustment = 3,
  Sale = 4,
  SaleVoid = 5,
}

export interface InventoryMovement {
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
  createdAt: string;
}
