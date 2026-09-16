import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { AuthStore } from '../../../../core/stores/auth.store';
import { SaleDocumentStatus, SaleDocumentType, SaleStatus, SalesReportRow } from '../../models/sales-report.model';
import { SalesReportService } from '../../services/sales-report.service';
import { SalesReportPage } from './sales-report-page';

function sale(overrides: Partial<SalesReportRow> = {}): SalesReportRow {
  return {
    id: 1, businessDate: '2026-07-01', timeZoneIdSnapshot: 'America/Guayaquil',
    createdAt: '2026-07-01T15:00:00Z', status: SaleStatus.Completed, number: '001-001-000000001',
    customerName: 'Cliente', customerIdentification: null, customerEmail: null,
    documentType: SaleDocumentType.Invoice, documentStatus: SaleDocumentStatus.Authorized,
    sriAuthorizationStatus: 'AUTORIZADO', total: 115, totalCost: 60, grossProfit: 40, grossMarginPercent: 40,
    itemsCount: 1, userId: 1, username: 'Operador', notes: null,
    creditNoteImpact: {
      authorizedCreditNoteCount: 2, authorizedCreditNoteTotal: 23, authorizedCreditNoteSubtotal: 20,
      returnedCost: 12, netTotal: 92, netSubtotal: 80, netCost: 48, netGrossProfit: 32, netGrossMarginPercent: 40,
    },
    ...overrides,
  };
}

describe('SalesReportPage net reporting', () => {
  let component: SalesReportPage;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: SalesReportService, useValue: {} },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
      ],
    });
    component = TestBed.runInInjectionContext(() => new SalesReportPage());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('excludes voided sales and computes a weighted net margin, not an average', () => {
    const second = sale({
      id: 2, total: 23, totalCost: 18, grossProfit: 2, grossMarginPercent: 10,
      creditNoteImpact: {
        authorizedCreditNoteCount: 0, authorizedCreditNoteTotal: 0, authorizedCreditNoteSubtotal: 0,
        returnedCost: 0, netTotal: 23, netSubtotal: 20, netCost: 18, netGrossProfit: 2, netGrossMarginPercent: 10,
      },
    });
    component.sales.set([sale(), second, sale({ id: 3, status: SaleStatus.Voided })]);
    expect(component.salesCount()).toBe(3);
    expect(component.totalSold()).toBe(138);
    expect(component.creditNoteTotal()).toBe(23);
    expect(component.creditNoteCount()).toBe(2);
    expect(component.netTotal()).toBe(115);
    expect(component.netCost()).toBe(66);
    expect(component.netGrossProfit()).toBe(34);
    expect(component.netGrossMarginPercent()).toBe(34);
  });

  it('preserves negative net totals and returns zero margin for a nonpositive net subtotal', () => {
    const row = sale();
    row.creditNoteImpact = { ...row.creditNoteImpact, netTotal: -5, netSubtotal: -10, netGrossProfit: -58 };
    component.sales.set([row]);
    expect(component.netTotal()).toBe(-5);
    expect(component.netGrossProfit()).toBe(-58);
    expect(component.netGrossMarginPercent()).toBe(0);
  });

  it('exports matching net amounts with semicolons, UTF-8 BOM, decimal commas and escaped text', async () => {
    let exportedBlob: Blob | undefined;
    vi.stubGlobal('URL', {
      createObjectURL: vi.fn((blob: Blob) => { exportedBlob = blob; return 'blob:report'; }),
      revokeObjectURL: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    component.sales.set([sale({ notes: 'A; "B"\nC' })]);
    component.exportCsv();
    expect(exportedBlob?.type).toBe('text/csv;charset=utf-8');
    const bytes = await new Promise<ArrayBuffer>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as ArrayBuffer);
      reader.onerror = () => reject(reader.error);
      reader.readAsArrayBuffer(exportedBlob!);
    });
    expect([...new Uint8Array(bytes).slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    const csv = new TextDecoder().decode(bytes);
    expect(csv).toContain('Total original;Notas de crédito autorizadas;Cantidad NC;Total neto;Costo original;Costo revertido;Costo neto;Utilidad original;Margen bruto %;Utilidad neta;Margen neto %');
    expect(csv).toContain(';115,00;23,00;2;92,00;60,00;12,00;48,00;40,00;40,00;32,00;40,00;');
    expect(csv).toContain('"A; ""B""\nC"');
    expect(csv).not.toContain('$');
  });
});
