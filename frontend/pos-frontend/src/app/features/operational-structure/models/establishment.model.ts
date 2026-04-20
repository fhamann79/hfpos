export interface Establishment {
  id: number;
  companyId: number;
  name: string;
  isActive: boolean;
}

export interface CreateEstablishmentRequest {
  companyId: number;
  name: string;
}

export interface UpdateEstablishmentRequest {
  companyId: number;
  name: string;
  isActive: boolean;
}
