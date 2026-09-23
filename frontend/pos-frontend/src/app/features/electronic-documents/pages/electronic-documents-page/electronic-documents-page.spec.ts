import { HttpErrorResponse } from '@angular/common/http';
import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { SaleDocumentStatus } from '../../../pos-workstation/models/sale-document.model';
import {
  ElectronicDocumentDetail,
  ElectronicDocumentKind,
  ElectronicDocumentListItem,
  ElectronicDocumentSummary,
} from '../../models/electronic-document.model';
import { ElectronicDocumentService } from '../../services/electronic-document.service';
import { ElectronicDocumentsPage } from './electronic-documents-page';

const summary: ElectronicDocumentSummary = {
  totalDocuments: 42,
  invoiceCount: 30,
  creditNoteCount: 12,
  draftCount: 8,
  pendingAuthorizationCount: 6,
  authorizedCount: 24,
  rejectedCount: 3,
  cancelledCount: 1,
  withSriErrorCount: 4,
};

function row(overrides: Partial<ElectronicDocumentListItem> = {}): ElectronicDocumentListItem {
  return {
    key: 'invoice:7',
    kind: ElectronicDocumentKind.Invoice,
    id: 7,
    number: '001-001-000000007',
    businessDate: '2026-09-20',
    documentIssuedAt: '2026-09-20T15:00:00Z',
    createdAt: '2026-09-20T15:00:00Z',
    buyerName: 'Comprador',
    buyerIdentification: '0999999999001',
    buyerEmail: 'buyer@example.com',
    total: 115,
    documentStatus: SaleDocumentStatus.Authorized,
    accessKey: 'access-key',
    authorizationNumber: 'authorization-number',
    authorizedAt: '2026-09-20T16:00:00Z',
    sriEnvironment: 1,
    sriSignedAt: '2026-09-20T15:10:00Z',
    sriSubmittedAt: '2026-09-20T15:20:00Z',
    sriReceptionStatus: 'RECIBIDA',
    sriAuthorizationStatus: 'AUTORIZADO',
    sriLastSubmissionError: null,
    sriLastCheckedAt: '2026-09-20T16:00:00Z',
    hasSriXmlDraft: true,
    hasSriSignedXml: true,
    originalSaleId: null,
    originalSaleNumber: null,
    ...overrides,
  };
}

function detail(overrides: Partial<ElectronicDocumentDetail> = {}): ElectronicDocumentDetail {
  return {
    ...row(),
    buyerIdentificationType: '04',
    buyerAddress: 'Dirección',
    sriEmissionType: 1,
    sriNumericCode: '12345678',
    sriXmlGeneratedAt: '2026-09-20T15:00:00Z',
    sriSignatureHash: 'hash',
    sriSigningCertificateThumbprint: 'thumbprint',
    sriSigningCertificateSubject: 'CN=Test',
    sriSigningCertificateSerialNumber: 'serial',
    grossSubtotal: 100,
    discountAmount: 0,
    subtotal: 100,
    taxAmount: 15,
    reason: null,
    notes: null,
    ...overrides,
  };
}

describe('ElectronicDocumentsPage', () => {
  let component: ElectronicDocumentsPage;
  let permissions: WritableSignal<Set<string>>;
  let service: Record<string, ReturnType<typeof vi.fn>>;

  beforeEach(() => {
    permissions = signal(new Set([PERMISSIONS.reportsSalesRead]));
    service = {
      getDocuments: vi.fn(() => of({
        items: [row(), row({ key: 'credit-note:7', kind: ElectronicDocumentKind.CreditNote })],
        page: 1,
        pageSize: 15,
        totalItems: 42,
        totalPages: 3,
        summary,
      })),
      getDetail: vi.fn(() => of(detail())),
      prepareSriDraft: vi.fn(() => of({})),
      signSri: vi.fn(() => of({})),
      submitSri: vi.fn(() => of({})),
      checkSriAuthorization: vi.fn(() => of({})),
      getSriXmlDraft: vi.fn(() => of(new Blob(['xml']))),
      getSriSignedXml: vi.fn(() => of(new Blob(['xml']))),
      getSriAuthorizedXml: vi.fn(() => of(new Blob(['xml']))),
      getSriRide: vi.fn(() => of({})),
      getSriRidePdf: vi.fn(() => of(new Blob(['pdf']))),
      getSriSubmissionAttempts: vi.fn(() => of([])),
      getSriEmailDeliveries: vi.fn(() => of([])),
      sendSriEmail: vi.fn(() => of({})),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: ElectronicDocumentService, useValue: service },
        { provide: PermissionService, useValue: { hasPermission: (permission: string) => permissions().has(permission) } },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
      ],
    });
    component = TestBed.runInInjectionContext(() => new ElectronicDocumentsPage());
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads once on init, uses backend summary and never triggers SRI actions automatically', () => {
    component.ngOnInit();

    expect(service['getDocuments']).toHaveBeenCalledOnce();
    expect(service['getDocuments']).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 15 }));
    expect(component.summary()).toEqual(summary);
    expect(component.totalItems()).toBe(42);
    expect(service['signSri']).not.toHaveBeenCalled();
    expect(service['submitSri']).not.toHaveBeenCalled();
    expect(service['checkSriAuthorization']).not.toHaveBeenCalled();
    expect(service['sendSriEmail']).not.toHaveBeenCalled();
  });

  it('preserves the composite key when invoice and credit note share the same id', () => {
    component.ngOnInit();
    expect(component.documents().map((document) => document.key)).toEqual(['invoice:7', 'credit-note:7']);
  });

  it('translates lazy pagination, page size and server sorting', () => {
    component.onLazyLoad({ first: 30, rows: 30, sortField: 'total', sortOrder: 1 });
    expect(service['getDocuments']).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1, pageSize: 30, sortField: 'total', sortOrder: 'asc' })
    );

    component.onLazyLoad({ first: 30, rows: 30, sortField: 'total', sortOrder: 1 });
    expect(service['getDocuments']).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 2, pageSize: 30 })
    );
  });

  it('applies filters globally from page one and clear restores defaults', () => {
    component.first = 45;
    component.currentPage.set(4);
    component.search = 'clave';
    component.kind = ElectronicDocumentKind.CreditNote;
    component.documentStatus = SaleDocumentStatus.Rejected;
    component.onlyWithSriError = true;
    component.applyFilters();

    expect(service['getDocuments']).toHaveBeenLastCalledWith(expect.objectContaining({
      page: 1,
      search: 'clave',
      kind: ElectronicDocumentKind.CreditNote,
      documentStatus: SaleDocumentStatus.Rejected,
      onlyWithSriError: true,
    }));

    component.clearFilters();
    expect(service['getDocuments']).toHaveBeenLastCalledWith(expect.objectContaining({
      page: 1,
      search: null,
      kind: null,
      documentStatus: null,
      onlyWithSriError: false,
      sortField: 'documentDate',
      sortOrder: 'desc',
    }));
  });

  it('gates fiscal actions by both permission and document state', () => {
    const draft = detail({
      documentStatus: SaleDocumentStatus.Draft,
      hasSriXmlDraft: true,
      hasSriSignedXml: false,
      sriSubmittedAt: null,
      sriReceptionStatus: null,
      sriAuthorizationStatus: null,
    });
    expect(component.canSign(draft)).toBe(false);

    permissions.update((current) => new Set([...current, PERMISSIONS.sriDocumentsSign]));
    expect(component.canSign(draft)).toBe(true);

    const signed = { ...draft, hasSriSignedXml: true };
    expect(component.canSubmit(signed)).toBe(false);
    permissions.update((current) => new Set([...current, PERMISSIONS.sriDocumentsSubmit]));
    expect(component.canSubmit(signed)).toBe(true);
    expect(component.canSubmit(detail())).toBe(false);
  });

  it('keeps the inherited credit-note read policy visible without widening privileges', () => {
    const creditNote = detail({ kind: ElectronicDocumentKind.CreditNote, key: 'credit-note:7' });
    expect(component.canReadArtifacts(creditNote)).toBe(false);
    permissions.update((current) => new Set([...current, PERMISSIONS.posSalesVoid]));
    expect(component.canReadArtifacts(creditNote)).toBe(true);
    expect(component.canReadArtifacts(detail())).toBe(true);
  });

  it('dispatches invoice and credit-note detail and actions through the service', () => {
    const invoiceRow = row();
    component.openDetail(invoiceRow);
    expect(service['getDetail']).toHaveBeenCalledWith(ElectronicDocumentKind.Invoice, 7);

    const creditNote = detail({ kind: ElectronicDocumentKind.CreditNote, key: 'credit-note:7' });
    component.prepareSriDraft(creditNote);
    expect(service['prepareSriDraft']).toHaveBeenCalledWith(ElectronicDocumentKind.CreditNote, 7);

    component.openAttempts(creditNote);
    expect(service['getSriSubmissionAttempts']).toHaveBeenCalledWith(ElectronicDocumentKind.CreditNote, 7);
    component.openDeliveries(creditNote);
    expect(service['getSriEmailDeliveries']).toHaveBeenCalledWith(ElectronicDocumentKind.CreditNote, 7);
  });

  it('shows readable backend error codes in fiscal detail', () => {
    service['getDetail'].mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 404,
      error: { error: 'ELECTRONIC_DOCUMENT_NOT_FOUND' },
    })));

    component.openDetail(row());
    expect(component.detailError()).toContain('ELECTRONIC_DOCUMENT_NOT_FOUND');
  });
});
