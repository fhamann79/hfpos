import { HttpErrorResponse } from '@angular/common/http';
import { resolveHttpErrorMessage } from './http-error-normalizer';

describe('Master data dependency messages', () => {
  it.each([
    ['CATEGORY_HAS_ACTIVE_PRODUCTS', 'contiene productos activos'],
    ['CATEGORY_INACTIVE', 'categoría está inactiva'],
    ['ESTABLISHMENT_HAS_ACTIVE_EMISSION_POINTS', 'puntos de emisión activos'],
    ['ESTABLISHMENT_HAS_ACTIVE_USERS', 'usuarios activos'],
    ['ESTABLISHMENT_HAS_OPEN_CASH_SESSIONS', 'cajas abiertas'],
    ['ESTABLISHMENT_HAS_STOCK', 'stock en cero'],
    ['ESTABLISHMENT_HAS_PENDING_FISCAL_DOCUMENTS', 'facturas y notas de crédito pendientes'],
    ['EMISSION_POINT_HAS_ACTIVE_USERS', 'usuarios activos'],
    ['EMISSION_POINT_HAS_OPEN_CASH_SESSIONS', 'cajas abiertas'],
    ['EMISSION_POINT_HAS_PENDING_FISCAL_DOCUMENTS', 'facturas y notas de crédito pendientes'],
    ['ESTABLISHMENT_INACTIVE', 'establecimiento está inactivo'],
    ['EMISSION_POINT_INACTIVE', 'punto de emisión está inactivo'],
  ])('explains %s in Spanish', (code, fragment) => {
    const error = new HttpErrorResponse({ status: 409, error: { error: code } });
    expect(resolveHttpErrorMessage(error)).toContain(fragment);
  });
});

describe('Purchase cost integrity message', () => {
  it('explains a provenance conflict without exposing database details', () => {
    const error = new HttpErrorResponse({
      status: 409,
      error: { error: 'PRODUCT_COST_PROVENANCE_INVALID' },
    });

    expect(resolveHttpErrorMessage(error)).toContain('procedencia del costo');
  });
});
