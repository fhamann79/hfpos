export interface Product {
  id: number;
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  isActive: boolean;
}

export interface CreateProductRequest {
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
}

export interface UpdateProductRequest {
  categoryId: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  isActive: boolean;
}
