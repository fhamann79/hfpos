import { ProductVatCategory } from '../../../core/utils/vat-category';

export interface PosProduct {
  tracksInventory?: boolean;
  id: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  vatCategory: ProductVatCategory;
  isActive: boolean;
  stock: number;
}
