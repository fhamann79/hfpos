import { TestBed } from '@angular/core/testing';
import { providePrimeNG } from 'primeng/config';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { PosProduct } from '../../models/pos-product.model';
import { CartWorkstation } from './cart-workstation';

describe('Cart quantity inventory limits', () => {
  const service: PosProduct = { id: 71, name: 'Synthetic service', price: 10,
    vatCategory: ProductVatCategory.Vat15, stock: 0, isActive: true, tracksInventory: false };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [CartWorkstation], providers: [providePrimeNG({ unstyled: true })],
  }));

  async function render(product: PosProduct) {
    const fixture = TestBed.createComponent(CartWorkstation);
    fixture.componentInstance.updateQuantity.subscribe(change => {
      // Mirror the controlled quantity input supplied by the parent workstation.
      fixture.componentRef.setInput('items', fixture.componentInstance.items.map(item =>
        item.productId === change.productId ? { ...item, quantity: change.quantity } : item));
    });
    fixture.componentRef.setInput('items', [{ productId: product.id, productName: product.name,
      quantity: 1, unitPrice: product.price, discountAmount: 0, stock: product.stock, product }]);
    fixture.componentRef.setInput('inventoryAvailable', true);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    const input = element.querySelector<HTMLInputElement>(`#qty-${product.id}`)!;
    return { fixture, input };
  }

  it('allows a zero-stock service above one through the real stepper and quantity input without fake stock', async () => {
    const { fixture, input } = await render(service);
    const changes: { productId: number; quantity: number }[] = [];
    fixture.componentInstance.updateQuantity.subscribe(change => changes.push(change));
    expect(input.getAttribute('aria-valuemax')).toBeNull();
    const increment = input.closest('p-inputnumber')!.querySelector('button[data-pc-section="incrementbutton"]')!;
    increment.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    increment.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }));
    fixture.detectChanges(); await fixture.whenStable();
    expect(input.value).toBe('2');
    expect(changes).toContainEqual({ productId: service.id, quantity: 2 });
    input.value = '5'; input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges(); await fixture.whenStable();
    expect(input.value).toBe('5');
    expect(changes).toContainEqual({ productId: service.id, quantity: 5 });
    expect(fixture.componentInstance.items[0].stock).toBe(0);
    fixture.destroy();
  });

  it('still clamps physical quantities to available stock', async () => {
    const { fixture, input } = await render({ ...service, tracksInventory: true, stock: 2 });
    expect(input.getAttribute('aria-valuemax')).toBe('2');
    input.value = '5'; input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges(); await fixture.whenStable();
    expect(input.value).toBe('2'); fixture.destroy();
  });

  it('does not grant the unlimited quantity exemption to an inactive service', async () => {
    const { fixture, input } = await render({ ...service, isActive: false });
    expect(input.getAttribute('aria-valuemax')).toBe('1');
    input.value = '5'; input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges(); await fixture.whenStable();
    expect(input.value).toBe('1'); fixture.destroy();
  });
});
