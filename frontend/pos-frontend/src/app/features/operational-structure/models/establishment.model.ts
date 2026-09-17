export interface Establishment {
  id: number;
  companyId: number;
  name: string;
  isActive: boolean;
}

export interface CreateEstablishmentRequest {
  name: string;
}

export interface UpdateEstablishmentRequest {
  name: string;
  isActive: boolean;
}
