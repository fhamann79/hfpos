export interface PosProduct {
  id: number;
  name: string;
  barcode?: string | null;
  internalCode?: string | null;
  price: number;
  isActive: boolean;
  stock: number;
}
