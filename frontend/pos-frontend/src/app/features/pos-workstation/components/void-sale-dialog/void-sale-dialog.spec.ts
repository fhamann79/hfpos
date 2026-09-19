import { SaleListItem } from '../../models/sale-list-item.model';
import { SalePaymentMethod } from '../../models/sale-payment-method.model';
import { VoidSaleDialog } from './void-sale-dialog';

describe('VoidSaleDialog cash semantics', () => {
  const component = new VoidSaleDialog();

  it('explains open-session recalculation and post-close cash-out for cash sales', () => {
    component.sale = sale(SalePaymentMethod.Cash);

    expect(component.paymentMethodLabel()).toBe('Efectivo');
    expect(component.cashEffectExplanation()).toContain('totales vivos se recalcularán');
    expect(component.cashEffectExplanation()).toContain('caja abierta actual');
    expect(component.cashEffectExplanation()).toContain('salida de efectivo');
  });

  it.each([
    [SalePaymentMethod.Card, 'tarjeta'],
    [SalePaymentMethod.Transfer, 'transferencia'],
    [SalePaymentMethod.Other, 'otro'],
  ])('explains that %s does not create a cash movement', (paymentMethod, label) => {
    component.sale = sale(paymentMethod);

    expect(component.cashEffectExplanation()).toContain(`pago con ${label}`);
    expect(component.cashEffectExplanation()).toContain('no genera un movimiento de efectivo');
    expect(component.cashEffectExplanation()).toContain('snapshot histórico permanecerá intacto');
  });

  function sale(paymentMethod: SalePaymentMethod): SaleListItem {
    return { id: 42, number: '001-001-000000042', paymentMethod } as SaleListItem;
  }
});
