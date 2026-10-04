export interface InventoryOperationRequest {
  productId: number;
  quantity: number;
  reference?: string;
  notes?: string;
  expectedMovementWatermark?: number;
  expectedQuantity?: number;
  expectedCompanyId?: number;
  expectedEstablishmentId?: number;
}

export interface InventoryCountSnapshot {
  productId: number;
  productName: string;
  quantity: number;
  movementWatermark: number;
  companyId: number;
  establishmentId: number;
}
