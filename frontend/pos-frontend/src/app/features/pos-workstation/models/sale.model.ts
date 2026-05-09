import { SaleItem } from './sale-item.model';

export interface Sale {
  id: number;
  createdAt: string;
  status: string;
  customerName: string | null;
  notes: string | null;
  grossSubtotal: number;
  discountAmount: number;
  subtotal: number;
  taxAmount: number;
  vat15Subtotal: number;
  vat5Subtotal: number;
  vat0Subtotal: number;
  vatExemptSubtotal: number;
  vatNotSubjectSubtotal: number;
  total: number;
  createdBy: string | null;
  isVoided: boolean;
  items: SaleItem[];
}
