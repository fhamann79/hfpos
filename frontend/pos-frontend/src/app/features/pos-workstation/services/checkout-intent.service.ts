import { Injectable } from '@angular/core';
import { CheckoutRequest } from '../models/checkout-request.model';

@Injectable({ providedIn: 'root' })
export class CheckoutIntentService {
  private key(scope: string): string { return `hfpos-checkout-v1:${scope}`; }

  load(scope: string): CheckoutRequest | null {
    const stored = sessionStorage.getItem(this.key(scope));
    if (!stored) return null;
    const value: CheckoutRequest = JSON.parse(stored) as CheckoutRequest;
    if (!value || typeof value.requestId !== 'string'
      || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.requestId)
      || !Array.isArray(value.items) || !value.items.length || value.items.some(item =>
        !Number.isInteger(item.productId) || !Number.isFinite(item.quantity) || !Number.isFinite(item.unitPrice)))
      throw new Error('Invalid stored checkout');
    return this.freeze(value);
  }

  save(scope: string, request: CheckoutRequest): CheckoutRequest {
    const frozen = this.freeze(request);
    // Persist before HTTP: navigation/reload must not silently authorize another sale.
    sessionStorage.setItem(this.key(scope), JSON.stringify(frozen));
    return frozen;
  }

  clear(scope: string): void { sessionStorage.removeItem(this.key(scope)); }

  private freeze(request: CheckoutRequest): CheckoutRequest {
    const copy = { ...request, items: request.items.map(item => Object.freeze({ ...item })) };
    Object.freeze(copy.items);
    return Object.freeze(copy);
  }
}
