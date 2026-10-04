import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { Sale } from '../../models/sale.model';
import { SalePaymentMethod } from '../../models/sale-payment-method.model';
import { SaleDocumentStatus } from '../../models/sale-document.model';
import { ReceiptPrintService, RECEIPT_PRINT_CSS } from '../../services/receipt-print.service';
import { SaleReceiptDialog } from './sale-receipt-dialog';

describe('Persisted internal receipt', () => {
  const syntheticSale = {
    id: 42, number: '017-009-000000042', status: 'Completada', documentStatus: SaleDocumentStatus.NotRequired,
    paymentMethod: SalePaymentMethod.Cash, createdAt: '2026-10-04T12:00:00Z', timeZoneIdSnapshot: 'America/Guayaquil',
    buyerNameSnapshot: 'Comprador histórico', customerName: 'Changed live customer', total: 10, subtotal: 10,
    grossSubtotal: 11, discountAmount: 1, taxAmount: 0, cashReceived: 20, cashChange: 10,
    items: [{ productId: 7, productName: 'Changed live product', productNameSnapshot: 'Nombre histórico '.repeat(10),
      productSkuSnapshot: '<SKU>', quantity: 1, unitPrice: 11, discountAmount: 1, taxAmount: 0, lineTotal: 10 }],
  } as unknown as Sale;

  beforeEach(() => TestBed.configureTestingModule({ imports: [SaleReceiptDialog] }));

  it('renders historical snapshots, authoritative amounts and safe legacy labels without live names or costs', async () => {
    const fixture = TestBed.createComponent(SaleReceiptDialog);
    fixture.componentRef.setInput('sale', syntheticSale); fixture.componentRef.setInput('visible', true);
    fixture.detectChanges(); await fixture.whenStable();
    const article = fixture.componentInstance.receipt!.nativeElement;
    expect(article.textContent).toContain('Comprador histórico'); expect(article.textContent).toContain('Nombre histórico');
    expect(article.textContent).toContain('$20.00'); expect(article.textContent).toContain('$10.00');
    expect(article.textContent).not.toContain('Changed live'); expect(article.textContent).not.toContain('Costo');
    fixture.componentRef.setInput('sale', { ...syntheticSale, buyerNameSnapshot: null, cashReceived: null, cashChange: null,
      items: [{ ...syntheticSale.items[0], productNameSnapshot: null, productSkuSnapshot: null }] });
    fixture.detectChanges();
    expect(article.textContent).toContain('sin nombre histórico'); expect(article.textContent).toContain('Nombre histórico no disponible');
    expect(article.textContent).not.toContain('Recibido:'); expect(article.textContent).not.toContain('Changed live');
    fixture.destroy();
  });

  it('prints only the receipt in an isolated 80mm document and cleans on afterprint', () => {
    const printer = TestBed.inject(ReceiptPrintService);
    const article = document.createElement('article'); article.textContent = '<synthetic buyer>';
    const originalAppend = document.body.appendChild.bind(document.body);
    let frame: HTMLIFrameElement | undefined;
    const append = vi.spyOn(document.body, 'appendChild').mockImplementation(<T extends Node>(node: T): T => {
      const added = originalAppend(node);
      if (node instanceof HTMLIFrameElement) {
        frame = node; node.contentWindow!.focus = vi.fn(); node.contentWindow!.print = vi.fn();
      }
      return added;
    });
    const result = printer.print(article); const printedDocument = frame!.contentDocument!;
    expect(result.ok).toBe(true); expect(printedDocument.querySelectorAll('article')).toHaveLength(1);
    expect(printedDocument.querySelectorAll('button, nav, p-dialog, script')).toHaveLength(0);
    expect(printedDocument.body.textContent).toBe('<synthetic buyer>'); expect(printedDocument.head.textContent).toContain('size: 80mm');
    expect(RECEIPT_PRINT_CSS).not.toContain('A4');
    frame!.contentWindow!.dispatchEvent(new Event('afterprint')); expect(frame!.isConnected).toBe(false);
    result.cleanup(); append.mockRestore();
  });

  it('print exceptions clean the host and never emit another sale or next customer', () => {
    const fixture = TestBed.createComponent(SaleReceiptDialog);
    const print = vi.spyOn(TestBed.inject(ReceiptPrintService), 'print').mockReturnValue({ ok: false, cleanup: vi.fn() });
    fixture.componentRef.setInput('sale', syntheticSale); fixture.componentRef.setInput('visible', true); fixture.detectChanges();
    const next = vi.spyOn(fixture.componentInstance.nextCustomer, 'emit');
    fixture.componentInstance.printReceipt();
    expect(print).toHaveBeenCalledOnce(); expect(next).not.toHaveBeenCalled(); expect(fixture.componentInstance.printError).toContain('ya está registrada');
    fixture.destroy();
  });

  it('removes an iframe when browser print throws', () => {
    const originalAppend = document.body.appendChild.bind(document.body);
    const append = vi.spyOn(document.body, 'appendChild').mockImplementation(<T extends Node>(node: T): T => {
      const added = originalAppend(node);
      if (node instanceof HTMLIFrameElement) {
        node.contentWindow!.focus = vi.fn(); node.contentWindow!.print = () => { throw new Error('Synthetic print failure'); };
      }
      return added;
    });
    expect(TestBed.inject(ReceiptPrintService).print(document.createElement('article')).ok).toBe(false);
    expect(document.querySelectorAll('iframe')).toHaveLength(0); append.mockRestore();
  });
});
