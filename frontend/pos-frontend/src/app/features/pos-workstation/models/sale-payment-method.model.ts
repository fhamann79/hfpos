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
