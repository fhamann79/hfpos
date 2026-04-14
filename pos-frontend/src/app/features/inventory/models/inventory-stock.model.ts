export interface InventoryStock {
  productId: number;
  productName: string;
  currentStock: number;
  establishmentId: number | null;
  establishmentName: string | null;
  updatedAt: string | null;
}
