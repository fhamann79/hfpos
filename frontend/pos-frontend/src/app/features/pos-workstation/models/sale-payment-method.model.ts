export enum SalePaymentMethod {
  Cash = 0,
  Card = 1,
  Transfer = 2,
  Other = 3,
}

export const SALE_PAYMENT_METHOD_OPTIONS = [
  { label: 'Efectivo', value: SalePaymentMethod.Cash },
  { label: 'Tarjeta', value: SalePaymentMethod.Card },
  { label: 'Transferencia', value: SalePaymentMethod.Transfer },
  { label: 'Otro', value: SalePaymentMethod.Other },
];

export function salePaymentMethodLabel(method: SalePaymentMethod): string {
  return SALE_PAYMENT_METHOD_OPTIONS.find((option) => option.value === method)?.label ?? 'Efectivo';
}

export function normalizeSalePaymentMethod(value: unknown): SalePaymentMethod {
  if (typeof value === 'number' && Object.values(SalePaymentMethod).includes(value)) {
    return value as SalePaymentMethod;
  }

  if (typeof value === 'string') {
    const normalized = value.trim().toLowerCase();
    const names: Record<string, SalePaymentMethod> = {
      cash: SalePaymentMethod.Cash,
      card: SalePaymentMethod.Card,
      transfer: SalePaymentMethod.Transfer,
      other: SalePaymentMethod.Other,
    };

    if (normalized in names) {
      return names[normalized];
    }

    const numericValue = Number(normalized);
    if (Number.isInteger(numericValue) && Object.values(SalePaymentMethod).includes(numericValue)) {
      return numericValue as SalePaymentMethod;
    }
  }

  return SalePaymentMethod.Cash;
}
