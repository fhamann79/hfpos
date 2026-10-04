export interface Establishment {
  id: number;
  companyId: number;
  name: string;
  code?: string;
  address?: string;
  isActive: boolean;
}

export interface CreateEstablishmentRequest {
  name: string;
  code: string;
  address: string;
}

export interface UpdateEstablishmentRequest {
  name: string;
  code?: string;
  address?: string;
}
