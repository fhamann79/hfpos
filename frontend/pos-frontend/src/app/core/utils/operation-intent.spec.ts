import { HttpErrorResponse } from '@angular/common/http';
import { definitiveOperationRejection, OperationIntent, operationResult } from './operation-intent';

describe('OperationIntent', () => {
  const actor = '[10,1]', scope = '[10,1,2,3]';
  beforeEach(() => sessionStorage.clear());

  it('persists an independent snapshot before sending and recovers it after navigation', () => {
    const original = { requestId: 'original', items: [{ quantity: 2 }] };
    const intent = new OperationIntent<typeof original>('receipt', actor);
    intent.capture(scope, original);
    original.items[0].quantity = 9;
    const recovered = new OperationIntent<typeof original>('receipt', actor);
    const retry = recovered.capture(scope, { requestId: 'new', items: [] });
    expect(retry).toEqual({ requestId: 'original', items: [{ quantity: 2 }] });
    retry.items[0].quantity = 8;
    expect(recovered.retry(scope).items[0].quantity).toBe(2);
    expect(() => recovered.retry('[10,1,2,4]')).toThrow('contexto original');
    expect(() => recovered.retry('[11,1,2,3]')).toThrow('actor original');
    expect(new OperationIntent('receipt', '[11,1]').pending).toBe(false);
    recovered.clear();
    expect(new OperationIntent('receipt', actor).pending).toBe(false);
  });

  it('fails closed on corrupted persistence or failed storage without sending a new intention', () => {
    sessionStorage.setItem('hfpos-operation-529:receipt:' + actor, '{}');
    expect(() => new OperationIntent('receipt', actor)).toThrow('ilegible');
    sessionStorage.clear();
    const intent = new OperationIntent('receipt', actor);
    const failure = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota'); });
    expect(() => intent.capture(scope, { requestId: 'original' })).toThrow('quota');
    expect(intent.pending).toBe(false);
    failure.mockRestore();
  });

  it('distinguishes definitive rejection from uncertain transport and conflicting result', () => {
    expect(definitiveOperationRejection(new HttpErrorResponse({ status: 400 }))).toBe(true);
    for (const status of [0, 200, 408, 500, 502])
      expect(definitiveOperationRejection(new HttpErrorResponse({ status }))).toBe(false);
    expect(definitiveOperationRejection(new HttpErrorResponse({ status: 409, error: { error: 'REQUEST_CONFLICT' } }))).toBe(false);
    expect(definitiveOperationRejection(new HttpErrorResponse({ status: 409 }))).toBe(false);
    expect(definitiveOperationRejection(new HttpErrorResponse({ status: 409, error: { error: 'CASH_SESSION_ALREADY_OPEN' } }))).toBe(true);
    expect(() => operationResult({ id: 1 }, 'original')).toThrow();
    expect(() => operationResult({ id: 2, requestId: 'wrong' }, 'original')).toThrow();
    expect(() => operationResult({ id: 2 }, undefined, 1)).toThrow();
    expect(operationResult({ id: 1, requestId: 'original' }, 'original').id).toBe(1);
  });
});
