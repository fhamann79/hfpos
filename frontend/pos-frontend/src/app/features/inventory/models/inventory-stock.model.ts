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
  stockStatus: StockStatus | keyof typeof StockStatus;
  isActive: boolean;
}
