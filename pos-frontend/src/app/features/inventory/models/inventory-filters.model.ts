import { InventoryMovementType } from './inventory-movement.model';

export interface InventoryMovementFilters {
  productId: number | null;
  type: InventoryMovementType | null;
  fromDate: string | null;
  toDate: string | null;
}
