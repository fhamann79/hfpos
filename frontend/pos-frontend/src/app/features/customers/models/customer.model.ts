export type CustomerStatusFilter = 'active' | 'inactive' | 'all';

export interface Customer {
  id: number;
  name: string;
  identificationType: string | null;
  identification: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateCustomerRequest {
  name: string;
  identificationType?: string | null;
  identification?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  notes?: string | null;
}

export interface UpdateCustomerRequest extends CreateCustomerRequest {
  isActive: boolean;
}

export const CUSTOMER_IDENTIFICATION_TYPE_OPTIONS = [
  { label: 'RUC', value: '04' },
  { label: 'Cedula', value: '05' },
  { label: 'Pasaporte', value: '06' },
  { label: 'Consumidor final', value: '07' },
];

export const CUSTOMER_STATUS_OPTIONS = [
  { label: 'Activos', value: 'active' as CustomerStatusFilter },
  { label: 'Inactivos', value: 'inactive' as CustomerStatusFilter },
  { label: 'Todos', value: 'all' as CustomerStatusFilter },
];

export function customerIdentificationTypeLabel(value: string | null | undefined): string {
  return CUSTOMER_IDENTIFICATION_TYPE_OPTIONS.find((option) => option.value === value)?.label ?? 'Sin tipo';
}
