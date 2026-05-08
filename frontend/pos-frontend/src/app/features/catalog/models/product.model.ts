import { ProductVatCategory } from '../../../core/utils/vat-category';

export interface Product {
  id: number;
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  vatCategory: ProductVatCategory;
  isActive: boolean;
}

export interface CreateProductRequest {
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  vatCategory: ProductVatCategory;
}

export interface UpdateProductRequest {
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  vatCategory: ProductVatCategory;
  isActive: boolean;
}
