import { HttpErrorResponse } from '@angular/common/http';
import { AuthStore } from '../stores/auth.store';
import { readErrorCode } from './http-error-normalizer';

export function operationScope(auth: AuthStore): string {
  return JSON.stringify([auth.me?.()?.userId ?? null, auth.companyId?.() ?? null,
    auth.establishmentId?.() ?? null, auth.emissionPointId?.() ?? null]);
}

export function operationActor(auth: AuthStore): string {
  return JSON.stringify([auth.me?.()?.userId ?? null, auth.companyId?.() ?? null]);
}

export function definitiveOperationRejection(error: HttpErrorResponse): boolean {
  return [400, 401, 403, 404, 422].includes(error.status)
    || (error.status === 409 && ['INVENTORY_SNAPSHOT_STALE', 'INSUFFICIENT_STOCK',
      'CASH_SESSION_NOT_OPEN', 'INVENTORY_TRANSFER_INSUFFICIENT_STOCK',
      'PAYMENT_SETTLEMENT_ALREADY_RECONCILED'].includes(readErrorCode(error) ?? ''));
}

// A successful status alone does not identify the committed operation.
export function operationResult<T extends { id: number; requestId?: string | null }>(value: T, requestId?: string, id?: number): T {
  if (!value || !Number.isInteger(value.id) || value.id <= 0
    || (id !== undefined && value.id !== id) || (requestId !== undefined && value.requestId !== requestId))
    throw new HttpErrorResponse({ status: 200, error: { error: 'OPERATION_RESULT_UNKNOWN' } });
  return value;
}

// The actor owns the unresolved request, even after navigation or a context change.
export class OperationIntent<T> {
  private readonly key: string;
  private readonly actor: string;
  private value: { scope: string; request: T } | null;

  constructor(family: string, actor: string) {
    this.actor = actor;
    this.key = 'hfpos-operation-529:' + family + ':' + actor;
    const stored = sessionStorage.getItem(this.key);
    this.value = null;
    if (stored) {
      const parsed: unknown = JSON.parse(stored);
      if (!parsed || typeof parsed !== 'object' || !('scope' in parsed)
        || typeof parsed.scope !== 'string' || !('request' in parsed)
        || !parsed.request || typeof parsed.request !== 'object') {
        throw new Error('Solicitud pendiente ilegible. No se enviara otra operacion.');
      }
      this.value = parsed as { scope: string; request: T };
      this.checkActor(this.value.scope);
    }
  }

  get pending(): boolean { return this.value !== null; }

  capture(scope: string, request: T): T {
    this.checkActor(scope);
    if (!this.value) {
      const value = { scope, request: structuredClone(request) };
      sessionStorage.setItem(this.key, JSON.stringify(value));
      this.value = value;
    }
    return this.retry(scope);
  }

  retry(scope: string): T {
    this.checkActor(scope);
    if (!this.value || this.value.scope !== scope)
      throw new Error('Vuelve al contexto original para recuperar la operacion pendiente.');
    return structuredClone(this.value.request);
  }

  clear(): void {
    sessionStorage.removeItem(this.key);
    this.value = null;
  }
  private checkActor(scope: string): void {
    const values: unknown = JSON.parse(scope);
    if (!Array.isArray(values) || JSON.stringify(values.slice(0, 2)) !== this.actor)
      throw new Error('Vuelve al actor original para recuperar la operacion pendiente.');
  }
}
