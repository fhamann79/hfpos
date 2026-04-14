export type InventoryMovementType = 'ENTRY' | 'EXIT' | 'ADJUSTMENT' | 'SALE' | 'VOID' | string;

export interface InventoryMovement {
  id: number;
  createdAt: string;
  productId: number;
  productName: string;
  type: InventoryMovementType;
  quantity: number;
  user: string | null;
  origin: string | null;
}

export interface InventoryMovementDetail extends InventoryMovement {
  establishmentId: number | null;
  establishmentName: string | null;
  previousStock: number | null;
  newStock: number | null;
  referenceId: number | null;
  reason: string | null;
  notes: string | null;
}
