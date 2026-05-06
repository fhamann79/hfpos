import { HttpErrorResponse } from '@angular/common/http';

export interface NormalizedHttpError {
  status: number;
  code: string;
  message: string;
  isBusinessError: boolean;
  isTechnicalError: boolean;
}

const BUSINESS_ERROR_MESSAGES: Record<string, string> = {
  INSUFFICIENT_STOCK: 'Stock insuficiente para completar la venta.',
  SALE_ALREADY_VOIDED: 'La venta ya fue anulada.',
  PRODUCT_NOT_FOUND: 'El producto no existe.',
  INVALID_QUANTITY: 'La cantidad ingresada no es válida.',
  INVALID_UNIT_PRICE: 'El precio unitario ingresado no es válido.',
  INVENTORY_CONCURRENCY_CONFLICT: 'El inventario cambió mientras se procesaba la operación. Vuelve a intentarlo.',
  INVALID_CREDENTIALS: 'Credenciales inválidas.',
  CONTEXT_MISMATCH: 'El contexto seleccionado no coincide con la operación solicitada.',
  COMPANY_INACTIVE_OR_NOT_FOUND: 'La compañía no existe o está inactiva.',
  ESTABLISHMENT_INACTIVE_OR_NOT_FOUND: 'El establecimiento no existe o está inactivo.',
  EMISSION_POINT_INACTIVE_OR_NOT_FOUND: 'El punto de emisión no existe o está inactivo.',
  INTERNAL_SERVER_ERROR: 'Ocurrió un error interno. Intenta nuevamente.',
  DB_UPDATE_ERROR: 'No se pudo guardar la información. Intenta nuevamente.',
  UNIQUE_VIOLATION: 'Ya existe un registro con esos datos.',
  FOREIGN_KEY_VIOLATION: 'No se puede completar la acción porque hay información relacionada.',

  CATEGORY_ALREADY_EXISTS: 'Ya existe una categoría con ese nombre.',
  CATEGORY_NOT_FOUND: 'La categoría no existe.',
  NAME_REQUIRED: 'El nombre es obligatorio.',
  PRODUCT_BARCODE_ALREADY_EXISTS: 'Ya existe un producto con ese código de barras.',
  PRODUCT_INTERNAL_CODE_ALREADY_EXISTS: 'Ya existe un producto con ese código interno.',
  CUSTOMER_NAME_REQUIRED: 'El nombre del cliente es obligatorio.',
  CUSTOMER_NOT_FOUND: 'El cliente no existe o no pertenece a esta compañía.',
};

const STATUS_ERROR_MESSAGES: Record<number, string> = {
  0: 'No se puede conectar con el servidor. Revisa tu conexión e intenta nuevamente.',
  400: 'La solicitud no es válida. Revisa los datos e intenta nuevamente.',
  401: 'Credenciales inválidas.',
  403: 'No tienes permisos para esta acción.',
  404: 'No se encontró la información solicitada.',
  409: 'La operación no se pudo completar por un conflicto con la información actual.',
  500: 'Ocurrió un error interno. Intenta nuevamente.',
};

const KNOWN_ERROR_CODES = new Set(Object.keys(BUSINESS_ERROR_MESSAGES));

export function normalizeHttpError(error: HttpErrorResponse, fallback = 'No se pudo completar la acción.'): NormalizedHttpError {
  const code = readErrorCode(error);
  const message = resolveHttpErrorMessage(error, fallback);

  return {
    status: error.status,
    code,
    message,
    isBusinessError: code.length > 0 && error.status < 500,
    isTechnicalError: error.status === 0 || error.status >= 500,
  };
}

export function resolveHttpErrorMessage(error: HttpErrorResponse, fallback = 'No se pudo completar la acción.'): string {
  const code = readErrorCode(error);

  if (code && BUSINESS_ERROR_MESSAGES[code]) {
    return BUSINESS_ERROR_MESSAGES[code];
  }

  return STATUS_ERROR_MESSAGES[error.status] ?? fallback;
}

export function hasHttpBusinessError(error: HttpErrorResponse, code: string): boolean {
  return readErrorCode(error) === normalizeCode(code);
}

export function readErrorCode(error: HttpErrorResponse): string {
  const payload = error.error;

  if (typeof payload === 'string') {
    return detectErrorCode(payload);
  }

  const record = asRecord(payload);
  if (!record) {
    return '';
  }

  const directCode = readCodeFromKeys(record, ['error', 'code', 'errorCode', 'domainCode']);
  if (directCode) {
    return directCode;
  }

  const nestedErrors = record['errors'];
  const nestedCode = readCodeFromErrors(nestedErrors);
  if (nestedCode) {
    return nestedCode;
  }

  return readCodeFromKeys(record, ['message', 'detail', 'title']);
}

function readCodeFromKeys(record: Record<string, unknown>, keys: string[]): string {
  for (const key of keys) {
    const value = record[key];

    if (typeof value === 'string') {
      const code = detectErrorCode(value);
      if (code) {
        return code;
      }
    }

    const nested = asRecord(value);
    if (nested) {
      const code = readCodeFromKeys(nested, ['error', 'code', 'errorCode', 'domainCode', 'message', 'detail']);
      if (code) {
        return code;
      }
    }
  }

  return '';
}

function readCodeFromErrors(errors: unknown): string {
  if (typeof errors === 'string') {
    return detectErrorCode(errors);
  }

  if (Array.isArray(errors)) {
    for (const item of errors) {
      const code =
        typeof item === 'string'
          ? detectErrorCode(item)
          : readCodeFromKeys(asRecord(item) ?? {}, ['error', 'code', 'errorCode', 'domainCode', 'message', 'detail']);

      if (code) {
        return code;
      }
    }
  }

  const record = asRecord(errors);
  if (record) {
    for (const value of Object.values(record)) {
      const code = Array.isArray(value)
        ? value.map((item) => (typeof item === 'string' ? item : '')).find((item) => detectErrorCode(item))
        : typeof value === 'string'
          ? value
          : '';

      if (code) {
        return detectErrorCode(code);
      }
    }
  }

  return '';
}

function detectErrorCode(value: string): string {
  const normalized = normalizeCode(value);

  if (KNOWN_ERROR_CODES.has(normalized)) {
    return normalized;
  }

  for (const code of KNOWN_ERROR_CODES) {
    if (normalized.includes(code) || normalized.includes(code.replaceAll('_', ' '))) {
      return code;
    }
  }

  return looksLikeErrorCode(normalized) ? normalized : '';
}

function normalizeCode(code: string): string {
  return code.trim().toUpperCase();
}

function looksLikeErrorCode(value: string): boolean {
  return /^[A-Z][A-Z0-9_]+$/.test(value);
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
}
