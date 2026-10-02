import { PagedResult } from '../../../core/models/paged-result.model';

export interface InventoryStockSummary {
  totalProducts: number;
  outOfStockProducts: number;
  lowStockProducts: number;
  inactiveProducts: number;
  totalInventoryUnits: number;
  totalInventoryValue: number;
}

export interface InventoryStockPage extends PagedResult<InventoryStock> {
  summary: InventoryStockSummary;
}

export interface InventoryTransferProduct {
  productId: number;
  productName: string;
  quantity: number;
  isActive: boolean;
  barcode: string | null;
  internalCode: string | null;
}

export enum StockStatus {
  OutOfStock = 0,
  LowStock = 1,
  Ok = 2,
}

export interface InventoryStock {
  productId: number;
  productName: string;
  categoryId: number;
  categoryName: string;
  quantity: number;
  minimumStock: number;
  unitCost: number;
  inventoryValue: number;
  stockStatus: StockStatus | keyof typeof StockStatus;
  isActive: boolean;
}
