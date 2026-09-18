export enum SaleVoidCashEffect {
  OriginalOpenSessionRecalculated = 1,
  CurrentSessionCashOut = 2,
  NoCashMovement = 3,
}

export function normalizeSaleVoidCashEffect(value: unknown): SaleVoidCashEffect | null {
  if (value === SaleVoidCashEffect.OriginalOpenSessionRecalculated
    || value === 'OriginalOpenSessionRecalculated') {
    return SaleVoidCashEffect.OriginalOpenSessionRecalculated;
  }

  if (value === SaleVoidCashEffect.CurrentSessionCashOut || value === 'CurrentSessionCashOut') {
    return SaleVoidCashEffect.CurrentSessionCashOut;
  }

  if (value === SaleVoidCashEffect.NoCashMovement || value === 'NoCashMovement') {
    return SaleVoidCashEffect.NoCashMovement;
  }

  return null;
}

export function saleVoidCashEffectLabel(effect: SaleVoidCashEffect | null): string {
  switch (effect) {
    case SaleVoidCashEffect.OriginalOpenSessionRecalculated:
      return 'Totales de la caja original recalculados';
    case SaleVoidCashEffect.CurrentSessionCashOut:
      return 'Salida de efectivo en caja actual';
    case SaleVoidCashEffect.NoCashMovement:
      return 'Sin movimiento de efectivo';
    default:
      return 'Sin auditoría estructurada (registro histórico)';
  }
}
