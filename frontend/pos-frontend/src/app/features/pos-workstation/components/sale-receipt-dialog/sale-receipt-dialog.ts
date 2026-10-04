import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy, Output, ViewChild, inject } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { Sale } from '../../models/sale.model';
import { salePaymentMethodLabel } from '../../models/sale-payment-method.model';
import { saleDocumentStatusLabel } from '../../models/sale-document.model';
import { ReceiptPrintService } from '../../services/receipt-print.service';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';

@Component({
  selector: 'app-sale-receipt-dialog', standalone: true,
  imports: [CommonModule, DialogModule, ButtonModule],
  template: `
    <p-dialog header="Ticket interno" [visible]="visible" [modal]="true"
      [style]="{width: '28rem', maxWidth: '96vw'}" (visibleChange)="visibleChange.emit($event)">
      @if (sale; as s) {
        <article #receipt aria-label="Ticket interno persistido">
          <h2>Ticket interno {{ s.number || ('#' + s.id) }}</h2>
          <p>{{ dateLabel(s) }}</p>
          <p>{{ s.status }} · {{ documentStatus(s) }}</p>
          <p>Cliente: {{ s.buyerNameSnapshot || 'Nombre histórico no disponible' }}</p>
          @if (s.buyerIdentificationSnapshot) { <p>{{ s.buyerIdentificationSnapshot }}</p> }
          @if (s.buyerAddressSnapshot) { <p>{{ s.buyerAddressSnapshot }}</p> }
          @if (s.buyerEmailSnapshot) { <p>{{ s.buyerEmailSnapshot }}</p> }
          <table><thead><tr><th>Detalle</th><th>Importe</th></tr></thead><tbody>
            @for (item of s.items; track $index) {
              <tr><td>{{ item.productNameSnapshot || ('Producto #' + item.productId + ' · sin nombre histórico') }}
                @if (item.productSkuSnapshot) { <small>{{ item.productSkuSnapshot }}</small> }
                <small>{{ item.quantity }} × {{ item.unitPrice | currency:'USD' }}</small>
                <small>Desc. {{ item.discountAmount | currency:'USD' }} · IVA {{ item.taxAmount | currency:'USD' }}</small>
              </td><td>{{ item.lineTotal | currency:'USD' }}</td></tr>
            }
          </tbody></table>
          <div class="totals">
            <p>Subtotal bruto: {{ s.grossSubtotal | currency:'USD' }}</p>
            <p>Descuento: {{ s.discountAmount | currency:'USD' }}</p>
            <p>Subtotal neto: {{ s.subtotal | currency:'USD' }}</p>
            <p>IVA: {{ s.taxAmount | currency:'USD' }}</p>
            <p><strong>Total: {{ s.total | currency:'USD' }}</strong></p>
            <p>Pago: {{ paymentLabel(s) }}</p>
            @if (s.cashReceived !== null && s.cashReceived !== undefined) {
              <p>Recibido: {{ s.cashReceived | currency:'USD' }}</p>
              <p><strong>Vuelto: {{ s.cashChange | currency:'USD' }}</strong></p>
            }
            @if (s.notes) { <p>{{ s.notes }}</p> }
          </div>
          <p>Comprobante interno · no es RIDE ni comprobante fiscal.</p>
        </article>
        @if (printError) { <p role="alert">{{ printError }}</p> }
        <div class="actions">
          <p-button label="Imprimir" icon="pi pi-print" (onClick)="printReceipt()" />
          @if (postSale) { <p-button label="Siguiente cliente" icon="pi pi-arrow-right" (onClick)="nextCustomer.emit()" /> }
        </div>
      }
    </p-dialog>`,
  styles: [`article { color: #222; overflow-wrap: anywhere; } h2 { font-size: 1.1rem; }
    table { width: 100%; table-layout: fixed; border-collapse: collapse; } td, th { padding: .4rem 0; text-align: left; vertical-align: top; }
    td:last-child, th:last-child { width: 6rem; text-align: right; } small { display: block; }
    .totals { border-top: 1px dashed #999; } .actions { display: flex; flex-wrap: wrap; gap: .5rem; margin-top: 1rem; }`],
})
export class SaleReceiptDialog implements OnChanges, OnDestroy {
  @Input() visible = false;
  @Input() sale: Sale | null = null;
  @Input() postSale = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() nextCustomer = new EventEmitter<void>();
  @ViewChild('receipt') receipt?: ElementRef<HTMLElement>;
  private readonly printer = inject(ReceiptPrintService);
  private cleanup?: () => void;
  printError = '';
  paymentLabel(sale: Sale): string { return salePaymentMethodLabel(sale.paymentMethod); }
  documentStatus(sale: Sale): string { return saleDocumentStatusLabel(sale.documentStatus); }
  dateLabel(sale: Sale): string { return formatBusinessDateTime(sale.createdAt, sale.timeZoneIdSnapshot ?? 'America/Guayaquil'); }
  printReceipt(): void {
    if (!this.receipt) return;
    this.cleanup?.();
    const result = this.printer.print(this.receipt.nativeElement);
    this.cleanup = result.cleanup;
    this.printError = result.ok ? '' : 'No se pudo abrir la impresión. La venta ya está registrada; puedes reimprimir.';
  }
  ngOnDestroy(): void { this.cleanup?.(); }
  ngOnChanges(): void { if (!this.visible) this.cleanup?.(); this.printError = ''; }
}
