export interface CheckoutItemRequest {
  productId: number;
  quantity: number;
  unitPrice: number;
  discountAmount?: number;
}

export interface CheckoutRequest {
  customerId?: number | null;
  discountAmount?: number;
  notes?: string;
  items: CheckoutItemRequest[];
}
