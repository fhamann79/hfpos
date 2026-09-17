export interface Company {
  id: number;
  name: string;
  timeZoneId: string;
  isActive: boolean;
}

export interface UpdateCompanyRequest {
  name: string;
  timeZoneId: string;
}
