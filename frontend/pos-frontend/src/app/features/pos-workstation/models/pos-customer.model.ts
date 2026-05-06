export interface PosCustomer {
  id: number;
  name: string;
  identification?: string | null;
  phone?: string | null;
  isActive: boolean;
}

export interface CreatePosCustomerRequest {
  name: string;
  identification?: string | null;
  phone?: string | null;
}
