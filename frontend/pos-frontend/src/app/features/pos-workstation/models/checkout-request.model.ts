export interface CheckoutItemRequest {
  productId: number;
  quantity: number;
  unitPrice: number;
}

export interface CheckoutRequest {
  customerId?: number | null;
  notes?: string;
  items: CheckoutItemRequest[];
}
