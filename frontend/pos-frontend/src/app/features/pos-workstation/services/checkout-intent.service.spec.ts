import { ProductVatCategory } from '../../../core/utils/vat-category';
import { CheckoutDraft } from '../models/checkout-intent.model';
import { CheckoutRequest } from '../models/checkout-request.model';
import { SaleDocumentType } from '../models/sale-document.model';
import { SalePaymentMethod } from '../models/sale-payment-method.model';
import { CheckoutIntentService } from './checkout-intent.service';

describe('Actor-owned checkout storage', () => {
  const service = new CheckoutIntentService();
  const request: CheckoutRequest = { requestId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', customerId: null,
    documentType: SaleDocumentType.Ticket, paymentMethod: SalePaymentMethod.Cash, cashReceived: 20,
    items: [{ productId: 7, quantity: 1, unitPrice: 10, discountAmount: 0 }] };
  const draft: CheckoutDraft = { customer: null, notes: '', documentType: SaleDocumentType.Ticket,
    paymentMethod: SalePaymentMethod.Cash, cashReceived: 20, discountAmount: 0,
    cart: [{ productId: 7, productName: 'Synthetic', quantity: 1, unitPrice: 10, discountAmount: 0, stock: 5,
      product: { id: 7, name: 'Synthetic', price: 10, stock: 5, isActive: true, vatCategory: ProductVatCategory.Vat0 } }] };
  beforeEach(() => sessionStorage.clear());

  it('keeps original context and frozen draft under immutable tenant/actor ownership', () => {
    const saved = service.save('1:10', '1:10:2:3', request, draft);
    expect(service.load('1:10')).toEqual(saved); expect(service.load('1:11')).toBeNull(); expect(service.load('2:10')).toBeNull();
    expect(Object.isFrozen(saved.request.items[0])).toBe(true); expect(Object.isFrozen(saved.draft.cart[0].product)).toBe(true);
    expect(() => service.save('1:10', '1:10:2:9', { ...request, requestId: crypto.randomUUID() }, draft)).toThrow();
    expect(service.load('1:10')?.request.requestId).toBe(request.requestId);
  });

  it('fails closed for pre-merge v1 without reading, migrating or discarding unverified payload', () => {
    const key = 'hfpos-checkout-v1:1:2:3:mutable-name'; sessionStorage.setItem(key, 'unverified synthetic payload');
    expect(() => service.load('1:10')).toThrow('Unverified legacy checkout owner');
    expect(sessionStorage.getItem(key)).toBe('unverified synthetic payload'); expect(service.load('2:10')).toBeNull();
  });

  it('does not restore a corrupted draft or a scope belonging to another actor', () => {
    const saved = service.save('1:10', '1:10:2:3', request, draft);
    sessionStorage.setItem('hfpos-checkout-v2:1:10', JSON.stringify({ ...saved, scope: '1:11:2:3' }));
    expect(() => service.load('1:10')).toThrow();
    sessionStorage.setItem('hfpos-checkout-v2:1:10', JSON.stringify({ ...saved, draft: { ...saved.draft, cart: [] } }));
    expect(() => service.load('1:10')).toThrow(); expect(sessionStorage.length).toBe(1);
  });
});
