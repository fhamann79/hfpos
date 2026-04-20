export interface EmissionPoint {
  id: number;
  establishmentId: number;
  code: string;
  name: string;
  isActive: boolean;
}

export interface CreateEmissionPointRequest {
  establishmentId: number;
  code: string;
  name: string;
}

export interface UpdateEmissionPointRequest {
  establishmentId: number;
  code: string;
  name: string;
  isActive: boolean;
}
