import { InventoryMovementSourceType, InventoryMovementType } from './inventory-movement.model';

export interface InventoryStockFilters {
  search: string | null;
  productId: number | null;
  onlyPositive: boolean;
}

export interface InventoryMovementFilters {
  productId: number | null;
  type: InventoryMovementType | null;
  sourceType: InventoryMovementSourceType | null;
  sourceId: number | null;
  from: string | null;
  to: string | null;
  userId: number | null;
  search: string | null;
  page: number;
  pageSize: number;
}
