export interface InventoryTransferDestination {
  id: number;
  name: string;
}

export interface InventoryTransferCreateItem {
  productId: number;
  quantity: number;
}

export interface InventoryTransferCreateRequest {
  destinationEstablishmentId: number;
  requestId: string;
  reference: string | null;
  notes: string | null;
  items: InventoryTransferCreateItem[];
}

export interface InventoryTransferListItem {
  id: number;
  sourceEstablishmentId: number;
  sourceEstablishmentName: string;
  destinationEstablishmentId: number;
  destinationEstablishmentName: string;
  createdByUserId: number;
  createdByUsername: string;
  createdAt: string;
  businessDate: string;
  timeZoneIdSnapshot: string;
  reference: string | null;
  lineCount: number;
  totalQuantity: number;
}

export interface InventoryTransferItem {
  id: number;
  productId: number;
  productName: string;
  quantity: number;
  sourceMovementId: number;
  destinationMovementId: number;
  sourceStockBefore: number;
  sourceStockAfter: number;
  destinationStockBefore: number;
  destinationStockAfter: number;
}

export interface InventoryTransferDetail extends InventoryTransferListItem {
  requestId: string;
  wasAlreadyProcessed: boolean;
  notes: string | null;
  items: InventoryTransferItem[];
}

export interface InventoryTransferFilters {
  page: number;
  pageSize: number;
  from: string | null;
  to: string | null;
  search: string | null;
}
