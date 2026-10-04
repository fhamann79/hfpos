import { Injectable } from '@angular/core';
import { CheckoutRequest } from '../models/checkout-request.model';
import { CheckoutDraft, CheckoutIntent } from '../models/checkout-intent.model';

@Injectable({ providedIn: 'root' })
export class CheckoutIntentService {
  private key(actor: string): string { return `hfpos-checkout-v2:${actor}`; }

  load(actor: string): CheckoutIntent | null {
    // Pre-merge v1 entries have no immutable owner. Never read or silently discard their payload.
    const legacyPrefix = `hfpos-checkout-v1:${actor.split(':')[0]}:`;
    for (let i = 0; i < sessionStorage.length; i++) {
      if (sessionStorage.key(i)?.startsWith(legacyPrefix)) throw new Error('Unverified legacy checkout owner');
    }
    const stored = sessionStorage.getItem(this.key(actor));
    if (!stored) return null;
    const intent = JSON.parse(stored) as CheckoutIntent;
    const value = intent?.request;
    if (!value || typeof value.requestId !== 'string'
      || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.requestId)
      || !Array.isArray(value.items) || !value.items.length || value.items.some(item =>
        !Number.isInteger(item.productId) || !Number.isFinite(item.quantity) || !Number.isFinite(item.unitPrice)))
      throw new Error('Invalid stored checkout');
    if (typeof intent.scope !== 'string' || !intent.scope.startsWith(`${actor}:`)
      || !intent.draft || !Array.isArray(intent.draft.cart) || intent.draft.cart.length !== value.items.length
      || !Number.isFinite(intent.draft.discountAmount) || typeof intent.draft.notes !== 'string'
      || intent.draft.documentType !== value.documentType || intent.draft.paymentMethod !== value.paymentMethod
      || intent.draft.cart.some((item, i) => !item.product || item.productId !== value.items[i].productId
        || item.quantity !== value.items[i].quantity || item.unitPrice !== value.items[i].unitPrice
        || item.discountAmount !== (value.items[i].discountAmount ?? 0))
      || (intent.draft.customer?.id ?? null) !== (value.customerId ?? null))
      throw new Error('Invalid stored checkout draft');
    return this.freezeIntent(intent);
  }

  save(actor: string, scope: string, request: CheckoutRequest, draft: CheckoutDraft): CheckoutIntent {
    if (this.load(actor)) throw new Error('A checkout is already pending for this actor');
    const frozen = this.freezeIntent({ scope, request, draft });
    // Persist before HTTP: navigation/reload must not silently authorize another sale.
    sessionStorage.setItem(this.key(actor), JSON.stringify(frozen));
    return frozen;
  }

  clear(actor: string): void { sessionStorage.removeItem(this.key(actor)); }

  private freezeIntent(intent: CheckoutIntent): CheckoutIntent {
    const draft = { ...intent.draft, customer: intent.draft.customer ? Object.freeze({ ...intent.draft.customer }) : null,
      cart: intent.draft.cart.map(item => Object.freeze({ ...item, product: Object.freeze({ ...item.product }) })) };
    Object.freeze(draft.cart);
    return Object.freeze({ scope: intent.scope, request: this.freeze(intent.request), draft: Object.freeze(draft) });
  }

  private freeze(request: CheckoutRequest): CheckoutRequest {
    const copy = { ...request, items: request.items.map(item => Object.freeze({ ...item })) };
    Object.freeze(copy.items);
    return Object.freeze(copy);
  }
}
