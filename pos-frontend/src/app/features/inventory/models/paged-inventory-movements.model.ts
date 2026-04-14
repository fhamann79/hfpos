import { InventoryMovement } from './inventory-movement.model';

export interface PagedInventoryMovements {
  items: InventoryMovement[];
  totalRecords: number;
  page: number;
  pageSize: number;
}
