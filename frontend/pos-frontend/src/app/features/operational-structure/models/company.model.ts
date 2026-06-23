export interface Company {
  id: number;
  name: string;
  timeZoneId: string;
  isActive: boolean;
}

export interface CreateCompanyRequest {
  name: string;
  timeZoneId: string;
}

export interface UpdateCompanyRequest {
  name: string;
  timeZoneId: string;
  isActive: boolean;
}
