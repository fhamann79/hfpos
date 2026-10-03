import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { ProductService } from '../../../catalog/services/product.service';
import { SupplierService } from '../../../suppliers/services/supplier.service';
import { PurchaseReceiptStatus } from '../../models/purchase-receipt.model';
import { PurchaseReceiptService } from '../../services/purchase-receipt.service';
import { PurchaseReceiptsPage } from './purchase-receipts-page';

describe('PurchaseReceiptsPage CSV export', () => {
  let fixture: ComponentFixture<PurchaseReceiptsPage>;
  let component: PurchaseReceiptsPage;
  const service = {
    getAll: vi.fn(() => of({ items: [], page: 1, pageSize: 15, totalItems: 0, totalPages: 0 })),
    exportCsv: vi.fn<() => ReturnType<PurchaseReceiptService['exportCsv']>>(),
  };

  beforeEach(async () => {
    vi.resetAllMocks();
    service.getAll.mockReturnValue(of({ items: [], page: 1, pageSize: 15, totalItems: 0, totalPages: 0 }));
    service.exportCsv.mockReturnValue(of(response('attachment; filename="receipts.csv"')));
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:purchase-receipts');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    await TestBed.configureTestingModule({
      imports: [PurchaseReceiptsPage], providers: [
        { provide: PurchaseReceiptService, useValue: service },
        { provide: SupplierService, useValue: {} },
        { provide: ProductService, useValue: {} },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(PurchaseReceiptsPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });
  afterEach(() => vi.restoreAllMocks());

  it('downloads using all current filters, not the visible page, and revokes the URL', () => {
    component.search = 'supplier'; component.from = '2026-09-01'; component.to = '2026-09-30';
    component.status = PurchaseReceiptStatus.Canceled; component.currentPage.set(3); component.rows = 30;
    let filename = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function(this: HTMLAnchorElement) {
      filename = this.download;
      expect(this.href).toBe('blob:purchase-receipts');
      expect(this.isConnected).toBe(true);
    });
    component.exportCsv();
    expect(service.exportCsv).toHaveBeenCalledWith({ search: 'supplier', from: '2026-09-01',
      to: '2026-09-30', status: PurchaseReceiptStatus.Canceled });
    expect(filename).toBe('receipts.csv');
    expect(URL.createObjectURL).toHaveBeenCalledOnce();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:purchase-receipts');
    expect(document.querySelector('a[download]')).toBeNull();
    expect(component.exporting()).toBe(false);
  });

  it.each([
    ["attachment; filename*=UTF-8''recepci%C3%B3n.csv", 'recepci\u00f3n.csv'],
    ['attachment; filename=receipts.csv', 'receipts.csv'],
    [null, 'recepciones-compra.csv'],
    ['attachment; filename="../../unsafe.csv"', 'recepciones-compra.csv'],
    ["attachment; filename*=UTF-8''%ZZ.csv", 'recepciones-compra.csv'],
    ['attachment; filename="unsafe.html"', 'recepciones-compra.csv'],
  ])('resolves a safe filename from %s', (header, expected) => {
    service.exportCsv.mockReturnValue(of(response(header)));
    let filename = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function(this: HTMLAnchorElement) {
      filename = this.download;
    });
    component.exportCsv();
    expect(filename).toBe(expected);
  });

  it('disables the button and prevents duplicate downloads while pending or list is loading', () => {
    const pending = new Subject<HttpResponse<Blob>>();
    service.exportCsv.mockReturnValue(pending);
    component.loading.set(true);
    component.exportCsv();
    expect(service.exportCsv).not.toHaveBeenCalled();
    component.loading.set(false);
    component.exportCsv();
    component.exportCsv();
    fixture.detectChanges();
    const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(b => b.textContent?.includes('Exportar CSV'));
    expect(button?.disabled).toBe(true);
    expect(component.exporting()).toBe(true);
    expect(service.exportCsv).toHaveBeenCalledOnce();
    pending.next(response(null)); pending.complete();
    expect(component.exporting()).toBe(false);
  });

  it('shows Spanish error text and restores loading without creating an object URL', () => {
    service.exportCsv.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.exportCsv();
    expect(component.exporting()).toBe(false);
    expect(component.exportError()).toContain('No se pudo exportar el CSV de recepciones');
    expect(URL.createObjectURL).not.toHaveBeenCalled();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Intenta nuevamente');
  });

  it('handles an empty response body without allocating a URL', () => {
    service.exportCsv.mockReturnValue(of(new HttpResponse<Blob>()));
    component.exportCsv();
    expect(component.exportError()).toContain('No se pudo descargar');
    expect(component.exporting()).toBe(false);
    expect(URL.createObjectURL).not.toHaveBeenCalled();
  });

  function response(header: string | null): HttpResponse<Blob> {
    return new HttpResponse({ body: new Blob(['csv']),
      headers: header ? new HttpHeaders({ 'Content-Disposition': header }) : new HttpHeaders() });
  }
});
