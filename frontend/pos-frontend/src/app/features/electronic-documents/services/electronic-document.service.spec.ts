import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SaleDocumentStatus } from '../../pos-workstation/models/sale-document.model';
import {
  ElectronicDocumentKind,
  ElectronicDocumentQuery,
} from '../models/electronic-document.model';
import { ElectronicDocumentService } from './electronic-document.service';

const query: ElectronicDocumentQuery = {
  from: '2026-09-01',
  to: '2026-09-30',
  kind: ElectronicDocumentKind.CreditNote,
  documentStatus: SaleDocumentStatus.Authorized,
  search: '  comprador  ',
  onlyWithSriError: true,
  page: 2,
  pageSize: 30,
  sortField: 'authorizedAt',
  sortOrder: 'asc',
};

describe('ElectronicDocumentService', () => {
  let service: ElectronicDocumentService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ElectronicDocumentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends global filters, pagination and allowlisted server sorting', () => {
    service.getDocuments(query).subscribe((result) => {
      expect(result.items[0].key).toBe('credit-note:9');
      expect(result.summary?.authorizedCount).toBe(4);
    });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/ElectronicDocuments'));
    expect(request.request.params.get('from')).toBe('2026-09-01');
    expect(request.request.params.get('to')).toBe('2026-09-30');
    expect(request.request.params.get('kind')).toBe('2');
    expect(request.request.params.get('documentStatus')).toBe('3');
    expect(request.request.params.get('search')).toBe('comprador');
    expect(request.request.params.get('onlyWithSriError')).toBe('true');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('30');
    expect(request.request.params.get('sortField')).toBe('authorizedAt');
    expect(request.request.params.get('sortOrder')).toBe('asc');
    request.flush({
      items: [{ kind: 2, id: 9 }],
      page: 2,
      pageSize: 30,
      totalItems: 31,
      totalPages: 2,
      summary: { authorizedCount: 4 },
    });
  });

  it('uses the composite kind and id for fiscal detail', () => {
    service.getDetail(ElectronicDocumentKind.Invoice, 7).subscribe((detail) => {
      expect(detail.key).toBe('invoice:7');
    });
    http.expectOne((candidate) => candidate.url.endsWith('/api/ElectronicDocuments/1/7'))
      .flush({ kind: 1, id: 7 });

    service.getDetail(ElectronicDocumentKind.CreditNote, 7).subscribe((detail) => {
      expect(detail.key).toBe('credit-note:7');
    });
    http.expectOne((candidate) => candidate.url.endsWith('/api/ElectronicDocuments/2/7'))
      .flush({ kind: 2, id: 7 });
  });

  it('dispatches invoice and credit-note mutations to their existing endpoints', () => {
    service.signSri(ElectronicDocumentKind.Invoice, 4).subscribe();
    const invoice = http.expectOne((candidate) => candidate.url.endsWith('/api/Sales/4/sri/sign'));
    expect(invoice.request.method).toBe('POST');
    invoice.flush({});

    service.prepareSriDraft(ElectronicDocumentKind.CreditNote, 8).subscribe();
    const creditNote = http.expectOne((candidate) =>
      candidate.url.endsWith('/api/CreditNotes/8/sri/prepare-draft')
    );
    expect(creditNote.request.method).toBe('POST');
    creditNote.flush({});
  });

  it('dispatches downloads and RIDE by document kind without proxy endpoints', () => {
    service.getSriAuthorizedXml(ElectronicDocumentKind.Invoice, 3).subscribe();
    const invoiceXml = http.expectOne((candidate) =>
      candidate.url.endsWith('/api/Sales/3/sri/authorized-xml')
    );
    expect(invoiceXml.request.responseType).toBe('blob');
    invoiceXml.flush(new Blob(['xml']));

    service.getSriRide(ElectronicDocumentKind.CreditNote, 6).subscribe();
    http.expectOne((candidate) => candidate.url.endsWith('/api/CreditNotes/6/sri/ride')).flush({});
  });

  it('dispatches email and both histories to the matching document workflow', () => {
    service.sendSriEmail(ElectronicDocumentKind.CreditNote, 5, { toEmail: 'buyer@example.com' }).subscribe();
    const email = http.expectOne((candidate) => candidate.url.endsWith('/api/CreditNotes/5/sri/email'));
    expect(email.request.body).toEqual({ toEmail: 'buyer@example.com' });
    email.flush({});

    service.getSriEmailDeliveries(ElectronicDocumentKind.Invoice, 2).subscribe();
    http.expectOne((candidate) => candidate.url.endsWith('/api/Sales/2/sri/email-deliveries')).flush([]);

    service.getSriSubmissionAttempts(ElectronicDocumentKind.CreditNote, 5).subscribe();
    http.expectOne((candidate) =>
      candidate.url.endsWith('/api/CreditNotes/5/sri/submission-attempts')
    ).flush([]);
  });
});
