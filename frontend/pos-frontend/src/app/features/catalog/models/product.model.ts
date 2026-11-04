import { ProductVatCategory } from '../../../core/utils/vat-category';
import { DeclareProductFiscalClassification, ProductFiscalClassification } from '../../fiscal-settings/models/fiscal-profile.model';

export interface ProductEditorSave extends CreateProductRequest {
  setManualCost: boolean;
  expectedStateToken: string | null;
  classification: DeclareProductFiscalClassification | null;
}

export interface ProductEditorState {
  product: Product;
  stateToken: string;
  productRevision: number;
  classificationRevision: number;
  classification: ProductFiscalClassification | null;
  canDisableInventory: boolean;
  salePending: string[];
}

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
