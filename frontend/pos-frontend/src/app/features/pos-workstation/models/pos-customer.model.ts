export interface PosCustomer {
  id: number;
  name: string;
  identificationType?: string | null;
  identification?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  isActive: boolean;
}

export interface CreatePosCustomerRequest {
  name: string;
  identificationType?: string | null;
  identification?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
}
