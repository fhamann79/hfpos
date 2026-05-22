import { DocumentTagSeverity } from './sale-document.model';

export enum SriSubmissionAttemptType {
  Reception = 1,
  Authorization = 2,
}

export enum SriSubmissionAttemptStatus {
  Pending = 0,
  Success = 1,
  Failed = 2,
}

export interface SriSubmissionAttempt {
  id: number;
  saleId: number;
  accessKey: string;
  environment: number;
  attemptType: SriSubmissionAttemptType;
  status: SriSubmissionAttemptStatus;
  receptionStatus: string | null;
  authorizationStatus: string | null;
  authorizationNumber: string | null;
  authorizationDate: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  sriMessageIdentifier: string | null;
  sriMessageType: string | null;
  sriMessage: string | null;
  sriAdditionalInfo: string | null;
  createdAt: string;
  createdByUserId: number;
}

export function normalizeSriSubmissionAttemptType(value: unknown): SriSubmissionAttemptType {
  if (typeof value === 'number') {
    return value === SriSubmissionAttemptType.Authorization
      ? SriSubmissionAttemptType.Authorization
      : SriSubmissionAttemptType.Reception;
  }

  if (typeof value === 'string') {
    const normalized = value.trim().toUpperCase();
    return normalized === '2' || normalized === 'AUTHORIZATION' || normalized === 'AUTORIZACION'
      ? SriSubmissionAttemptType.Authorization
      : SriSubmissionAttemptType.Reception;
  }

  return SriSubmissionAttemptType.Reception;
}

export function normalizeSriSubmissionAttemptStatus(value: unknown): SriSubmissionAttemptStatus {
  if (typeof value === 'number') {
    if (Object.values(SriSubmissionAttemptStatus).includes(value)) {
      return value as SriSubmissionAttemptStatus;
    }

    return SriSubmissionAttemptStatus.Pending;
  }

  if (typeof value === 'string') {
    const normalized = value.trim().toUpperCase();
    const byName: Record<string, SriSubmissionAttemptStatus> = {
      '0': SriSubmissionAttemptStatus.Pending,
      PENDING: SriSubmissionAttemptStatus.Pending,
      PENDIENTE: SriSubmissionAttemptStatus.Pending,
      '1': SriSubmissionAttemptStatus.Success,
      SUCCESS: SriSubmissionAttemptStatus.Success,
      EXITOSO: SriSubmissionAttemptStatus.Success,
      '2': SriSubmissionAttemptStatus.Failed,
      FAILED: SriSubmissionAttemptStatus.Failed,
      FALLIDO: SriSubmissionAttemptStatus.Failed,
    };

    return byName[normalized] ?? SriSubmissionAttemptStatus.Pending;
  }

  return SriSubmissionAttemptStatus.Pending;
}

export function sriReceptionStatusLabel(status: string | null | undefined): string {
  const normalized = normalizeSriStatus(status);

  switch (normalized) {
    case 'RECIBIDA':
      return 'Recibida por SRI';
    case 'DEVUELTA':
      return 'Devuelta por SRI';
    default:
      return normalized || 'No enviada';
  }
}

export function sriReceptionStatusSeverity(status: string | null | undefined): DocumentTagSeverity {
  const normalized = normalizeSriStatus(status);

  switch (normalized) {
    case 'RECIBIDA':
      return 'success';
    case 'DEVUELTA':
      return 'danger';
    default:
      return 'secondary';
  }
}

export function sriAuthorizationStatusLabel(status: string | null | undefined): string {
  const normalized = normalizeSriStatus(status);

  switch (normalized) {
    case 'AUTORIZADO':
      return 'Autorizado';
    case 'NO AUTORIZADO':
    case 'NO_AUTORIZADO':
      return 'No autorizado';
    case 'PENDIENTE':
      return 'Pendiente';
    default:
      return normalized || 'No consultada';
  }
}

export function sriAuthorizationStatusSeverity(status: string | null | undefined): DocumentTagSeverity {
  const normalized = normalizeSriStatus(status);

  switch (normalized) {
    case 'AUTORIZADO':
      return 'success';
    case 'NO AUTORIZADO':
    case 'NO_AUTORIZADO':
      return 'danger';
    case 'PENDIENTE':
      return 'warn';
    default:
      return 'secondary';
  }
}

export function sriSubmissionAttemptTypeLabel(type: SriSubmissionAttemptType | number): string {
  return type === SriSubmissionAttemptType.Authorization ? 'Autorización' : 'Recepción';
}

export function sriSubmissionAttemptStatusLabel(status: SriSubmissionAttemptStatus | number): string {
  switch (status) {
    case SriSubmissionAttemptStatus.Success:
      return 'Exitoso';
    case SriSubmissionAttemptStatus.Failed:
      return 'Fallido';
    case SriSubmissionAttemptStatus.Pending:
    default:
      return 'Pendiente';
  }
}

export function sriSubmissionAttemptStatusSeverity(status: SriSubmissionAttemptStatus | number): DocumentTagSeverity {
  switch (status) {
    case SriSubmissionAttemptStatus.Success:
      return 'success';
    case SriSubmissionAttemptStatus.Failed:
      return 'danger';
    case SriSubmissionAttemptStatus.Pending:
    default:
      return 'warn';
  }
}

function normalizeSriStatus(status: string | null | undefined): string {
  return status?.trim().toUpperCase() ?? '';
}
