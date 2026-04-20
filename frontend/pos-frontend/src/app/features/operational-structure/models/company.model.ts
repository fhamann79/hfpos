export interface Company {
  id: number;
  name: string;
  isActive: boolean;
}

export interface CreateCompanyRequest {
  name: string;
}

export interface UpdateCompanyRequest {
  name: string;
  isActive: boolean;
}
