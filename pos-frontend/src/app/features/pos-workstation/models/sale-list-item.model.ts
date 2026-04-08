export interface SaleListItem {
  id: number;
  createdAt: string;
  status: string;
  total: number;
  createdBy: string | null;
  isVoided: boolean;
}
