export interface InventoryOperationRequest {
  productId: number;
  quantity: number;
  reference?: string;
  notes?: string;
}
