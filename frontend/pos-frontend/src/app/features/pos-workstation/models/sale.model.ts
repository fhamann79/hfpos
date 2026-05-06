import { SaleItem } from './sale-item.model';

export interface Sale {
  id: number;
  createdAt: string;
  status: string;
  customerName: string | null;
  notes: string | null;
  subtotal: number;
  total: number;
  createdBy: string | null;
  isVoided: boolean;
  items: SaleItem[];
}
