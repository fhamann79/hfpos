export interface CheckoutItemRequest {
  productId: number;
  quantity: number;
  unitPrice: number;
}

export interface CheckoutRequest {
  notes?: string;
  items: CheckoutItemRequest[];
}
