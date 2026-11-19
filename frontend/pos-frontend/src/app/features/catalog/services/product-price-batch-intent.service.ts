import { Injectable } from '@angular/core';
import { PriceBatchPreview } from './product-price-batch.service';

interface PriceBatchIntent { scope: string; preview: PriceBatchPreview }

@Injectable({ providedIn: 'root' })
export class ProductPriceBatchIntentService {
  private key(actor: string) { return `hfpos-price-batch-v1:${actor}`; }

  load(actor: string): PriceBatchIntent | null {
    const stored = sessionStorage.getItem(this.key(actor));
    if (!stored) return null;
    const value = JSON.parse(stored) as PriceBatchIntent;
    if (!value || typeof value.scope !== 'string' || !value.scope.startsWith(`${actor}:`)
      || typeof value.preview?.operationId !== 'string'
      || !/^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(value.preview.operationId)
      || typeof value.preview.previewToken !== 'string' || !value.preview.previewToken
      || !Array.isArray(value.preview.rows) || value.preview.rows.length < 1 || value.preview.rows.length > 250)
      throw new Error('Invalid pending price batch');
    return value;
  }

  save(actor: string, scope: string, preview: PriceBatchPreview) {
    if (this.load(actor)) throw new Error('A price batch is already pending');
    // Same recovery boundary as checkout: persist before HTTP, never drop an uncertain commit.
    sessionStorage.setItem(this.key(actor), JSON.stringify({ scope, preview }));
  }

  clear(actor: string) { sessionStorage.removeItem(this.key(actor)); }
}
