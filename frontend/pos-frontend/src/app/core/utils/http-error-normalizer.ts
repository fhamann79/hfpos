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
  SALE_NOT_FOUND: 'La venta no existe.',
  SALE_ALREADY_VOIDED: 'La venta ya fue anulada.',
  SALE_NOT_INVOICE: 'Solo las facturas pueden enviarse por email.',
  SALE_VOIDED: 'No se puede enviar por email una venta anulada.',
  SALE_NOT_AUTHORIZED: 'La factura aún no está autorizada por el SRI.',
  PRODUCT_NOT_FOUND: 'El producto no existe.',
  INVALID_QUANTITY: 'La cantidad ingresada no es válida.',
  INVALID_UNIT_PRICE: 'El precio unitario ingresado no es válido.',
  INVALID_LINE_DISCOUNT: 'El descuento de línea no es válido.',
  INVALID_SALE_DISCOUNT: 'El descuento global no es válido.',
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
  INVALID_PRODUCT_VAT_CATEGORY: 'La categoría de IVA del producto no es válida.',
  CUSTOMER_NAME_REQUIRED: 'El nombre del cliente es obligatorio.',
  CUSTOMER_EMAIL_INVALID: 'Ingresa un email de cliente válido.',
  CUSTOMER_NOT_FOUND: 'El cliente no existe o no pertenece a esta compañía.',
  INVALID_COMPANY_RUC: 'El RUC debe tener 13 dígitos numéricos.',
  INVALID_COMPANY_FISCAL_SETTINGS: 'Los datos fiscales de la empresa no son válidos.',
  INVALID_SRI_ENVIRONMENT: 'El ambiente SRI seleccionado no es válido.',
  INVALID_SRI_EMISSION_TYPE: 'El tipo de emisión SRI no es válido.',
  INVALID_DOCUMENT_SEQUENCE: 'El secuencial ingresado no es válido.',
  DOCUMENT_SEQUENCE_NOT_FOUND: 'La secuencia documental no existe.',
  DOCUMENT_SEQUENCE_ALREADY_EXISTS: 'Ya existe una secuencia para ese establecimiento, punto de emisión y tipo de documento.',
  DOCUMENT_SEQUENCE_BELOW_USED_NUMBER: 'No puedes bajar el secuencial por debajo de documentos ya emitidos o del valor actual.',
  DOCUMENT_SEQUENCE_REASON_REQUIRED: 'Debes ingresar un motivo para cambiar el secuencial.',
  FISCAL_SETTINGS_OPERATION_FAILED: 'No se pudo completar la operación de configuración fiscal.',
  COMPANY_EMAIL_SETTINGS_NOT_CONFIGURED: 'La configuración de correo no está guardada todavía.',
  COMPANY_EMAIL_DISABLED: 'El correo SMTP de la empresa no está habilitado.',
  COMPANY_EMAIL_SMTP_HOST_REQUIRED: 'Debes ingresar el servidor SMTP.',
  COMPANY_EMAIL_FROM_REQUIRED: 'Debes ingresar el correo remitente.',
  COMPANY_EMAIL_PASSWORD_REQUIRED: 'Debes configurar la contraseña o API Key SMTP antes de enviar la prueba.',
  COMPANY_EMAIL_NOT_TESTED: 'Primero envía un correo de prueba exitoso en Configuración Fiscal.',
  COMPANY_EMAIL_INVALID_ADDRESS: 'Ingresa un correo electrónico válido.',
  COMPANY_EMAIL_TEST_FAILED: 'No se pudo enviar el correo de prueba. Revisa host, puerto, seguridad, usuario, password/API key y remitente.',
  COMPANY_EMAIL_OPERATION_FAILED: 'No se pudo guardar la configuración de correo.',
  SRI_AUTHORIZED_XML_NOT_AVAILABLE: 'No está disponible el XML autorizado de la factura.',
  SRI_RIDE_PDF_NOT_AVAILABLE: 'No está disponible el RIDE PDF de la factura.',
  SALE_INVOICE_EMAIL_SEND_FAILED: 'No se pudo enviar la factura por email.',
  SALE_INVOICE_EMAIL_OPERATION_FAILED: 'No se pudo completar el envío de la factura por email.',
  CERTIFICATE_NOT_FOUND: 'No hay certificado digital activo configurado.',
  CERTIFICATE_FILE_REQUIRED: 'Debes seleccionar un archivo de certificado.',
  CERTIFICATE_PASSWORD_REQUIRED: 'Debes ingresar la contraseña del certificado.',
  INVALID_CERTIFICATE_FILE: 'El archivo no es un certificado válido. Usa un archivo .p12 o .pfx.',
  INVALID_CERTIFICATE_PASSWORD: 'La contraseña del certificado no es correcta.',
  CERTIFICATE_WITHOUT_PRIVATE_KEY: 'El certificado no contiene clave privada y no puede usarse para firmar.',
  CERTIFICATE_EXPIRED: 'El certificado está vencido.',
  CERTIFICATE_NOT_VALID_YET: 'El certificado aún no está vigente.',
  CERTIFICATE_UNPROTECT_FAILED: 'No se pudo acceder de forma segura al certificado configurado.',
  CERTIFICATE_LOAD_FAILED: 'No se pudo cargar el certificado digital.',
  CERTIFICATE_PROTECTION_FAILED: 'No se pudo proteger el certificado. Intenta nuevamente.',
  CERTIFICATE_OPERATION_FAILED: 'No se pudo completar la operación del certificado.',
  SRI_SIGNING_ONLY_INVOICE: 'Solo las facturas pueden firmarse electrónicamente.',
  SRI_SIGNING_SALE_VOIDED: 'No se puede firmar una venta anulada.',
  SRI_XML_DRAFT_NOT_FOUND: 'No existe XML draft para esta factura.',
  SRI_ACCESS_KEY_REQUIRED: 'La factura no tiene clave de acceso SRI.',
  SRI_XML_ALREADY_SIGNED: 'Esta factura ya tiene XML firmado.',
  SRI_XML_SIGNING_FAILED: 'No se pudo firmar el XML de la factura.',
  SRI_SIGNATURE_VALIDATION_FAILED: 'La firma generada no pudo ser validada.',
  SRI_SIGNED_XML_NOT_FOUND: 'No existe XML firmado para esta factura.',
  SRI_DOCUMENT_SIGN_OPERATION_FAILED: 'No se pudo completar la firma electrónica.',
  SRI_SUBMISSION_ONLY_INVOICE: 'Solo las facturas pueden enviarse al SRI.',
  SRI_SIGNED_XML_REQUIRED: 'Primero debes firmar el XML antes de enviarlo al SRI.',
  SRI_SUBMISSION_SALE_VOIDED: 'No se puede enviar al SRI una venta anulada.',
  SRI_SETTINGS_DISABLED: 'La integración SRI está deshabilitada en la configuración fiscal.',
  SRI_PRODUCTION_SUBMISSION_DISABLED: 'El envío a producción está bloqueado por seguridad.',
  SRI_RECEPTION_ENDPOINT_NOT_CONFIGURED: 'No está configurado el endpoint de recepción SRI.',
  SRI_AUTHORIZATION_ENDPOINT_NOT_CONFIGURED: 'No está configurado el endpoint de autorización SRI.',
  SRI_RECEPTION_COMMUNICATION_FAILED: 'No se pudo comunicar con el servicio de recepción del SRI.',
  SRI_AUTHORIZATION_COMMUNICATION_FAILED: 'No se pudo comunicar con el servicio de autorización del SRI.',
  SRI_RECEPTION_REJECTED: 'El SRI devolvió el comprobante. Revisa el historial de intentos para ver el detalle.',
  SRI_AUTHORIZATION_REJECTED: 'El SRI no autorizó el comprobante. Revisa el historial de intentos.',
  SRI_AUTHORIZATION_PENDING: 'La autorización aún está pendiente en el SRI. Intenta consultar nuevamente más tarde.',
  SRI_ALREADY_AUTHORIZED: 'Esta factura ya está autorizada.',
  SRI_RIDE_PDF_GENERATION_FAILED: 'No se pudo generar el RIDE PDF. Intenta nuevamente.',
  SRI_SUBMISSION_OPERATION_FAILED: 'No se pudo completar la operación SRI.',
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
