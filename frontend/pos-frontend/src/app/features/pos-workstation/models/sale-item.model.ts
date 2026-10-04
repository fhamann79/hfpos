import { ProductVatCategory } from '../../../core/utils/vat-category';

export interface SaleItem {
  productId: number;
  productNameSnapshot?: string | null;
  productSkuSnapshot?: string | null;
  productName: string;
  quantity: number;
  unitPrice: number;
  grossSubtotal: number;
  discountAmount: number;
  netSubtotal: number;
  subtotal: number;
  vatCategory: ProductVatCategory;
  vatRate: number;
  taxableSubtotal: number;
  taxAmount: number;
  lineTotal: number;
}
