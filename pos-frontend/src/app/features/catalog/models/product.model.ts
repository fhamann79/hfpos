export interface Product {
  id: number;
  categoryId: number;
  name: string;
  price: number;
  isActive: boolean;
}

export interface CreateProductRequest {
  categoryId: number;
  name: string;
  price: number;
}

export interface UpdateProductRequest {
  categoryId: number;
  name: string;
  price: number;
  isActive: boolean;
}
