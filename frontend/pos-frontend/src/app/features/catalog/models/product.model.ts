import { ProductVatCategory } from '../../../core/utils/vat-category';

export interface Product {
  tracksInventory?: boolean;
  id: number;
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  cost: number;
  minimumStock: number;
  vatCategory: ProductVatCategory;
  isActive: boolean;
}

export interface CreateProductRequest {
  tracksInventory?: boolean;
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  cost: number;
  minimumStock: number;
  vatCategory: ProductVatCategory;
}

export interface UpdateProductRequest {
  tracksInventory?: boolean;
  expectedClassificationRevision?: number;
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  cost: number;
  minimumStock: number;
  vatCategory: ProductVatCategory;
}
