import { PosProduct } from './pos-product.model';

export interface CartItem {
  productId: number;
  productName: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  stock: number;
  product: PosProduct;
}
