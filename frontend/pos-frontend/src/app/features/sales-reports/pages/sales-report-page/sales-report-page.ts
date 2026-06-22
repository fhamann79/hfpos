import { CommonModule, CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import {
  SaleDocumentStatus,
  SaleDocumentType,
  SaleStatus,
  SalesReportDetail,
  SalesReportDetailItem,
  SalesReportFilters,
  SalesReportRow,
  getVatCategoryOption,
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
  saleDocumentTypeLabel,
  saleStatusLabel,
  saleStatusSeverity,
} from '../../models/sales-report.model';
import { SalesReportService } from '../../services/sales-report.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import {
  formatBusinessDate as formatBusinessDateValue,
  formatBusinessDateTime as formatBusinessDateTimeValue,
  formatBusinessTime as formatBusinessTimeValue,
} from '../../../../core/utils/business-date-format';

interface SelectOption<T> {
  label: string;
  value: T;
}

@Component({
  selector: 'app-sales-report-page',
  standalone: true,
  imports: [
    CommonModule,
    CurrencyPipe,
    FormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TableModule,
    TagModule,
  ],
  templateUrl: './sales-report-page.html',
  styleUrl: './sales-report-page.scss',
})
export class SalesReportPage implements OnInit {
  private readonly salesReportService = inject(SalesReportService);
  private readonly authStore = inject(AuthStore);

  readonly sales = signal<SalesReportRow[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  readonly detailVisible = signal(false);
  readonly detailLoading = signal(false);
  readonly detailErrorMessage = signal('');
  readonly selectedSale = signal<SalesReportDetail | null>(null);
  readonly selectedSaleRow = signal<SalesReportRow | null>(null);

  from = '';
  to = '';
  search = '';
  status: SaleStatus | null = null;
  documentType: SaleDocumentType | null = null;
  documentStatus: SaleDocumentStatus | null = null;

  readonly statusOptions: SelectOption<SaleStatus>[] = [
    { label: 'Completada', value: SaleStatus.Completed },
    { label: 'Anulada', value: SaleStatus.Voided },
    { label: 'Borrador', value: SaleStatus.Draft },
  ];

  readonly documentTypeOptions: SelectOption<SaleDocumentType>[] = [
    { label: 'Factura', value: SaleDocumentType.Invoice },
    { label: 'Ticket', value: SaleDocumentType.Ticket },
  ];

  readonly documentStatusOptions: SelectOption<SaleDocumentStatus>[] = [
    { label: 'No requerido', value: SaleDocumentStatus.NotRequired },
    { label: 'Borrador', value: SaleDocumentStatus.Draft },
    { label: 'Pendiente autorizacion', value: SaleDocumentStatus.PendingAuthorization },
    { label: 'Autorizado', value: SaleDocumentStatus.Authorized },
    { label: 'Rechazado', value: SaleDocumentStatus.Rejected },
    { label: 'Cancelado', value: SaleDocumentStatus.Cancelled },
  ];

  readonly salesCount = computed(() => this.sales().length);
  readonly reportableSales = computed(() => this.sales().filter((sale) => sale.status !== SaleStatus.Voided));
  readonly totalSold = computed(() =>
    this.reportableSales().reduce((total, sale) => total + sale.total, 0)
  );
  readonly totalCost = computed(() =>
    this.reportableSales().reduce((total, sale) => total + sale.totalCost, 0)
  );
  readonly totalGrossProfit = computed(() =>
    this.reportableSales().reduce((total, sale) => total + sale.grossProfit, 0)
  );
  readonly grossMarginPercent = computed(() => {
    const marginBase = this.totalCost() + this.totalGrossProfit();

    return marginBase > 0 ? (this.totalGrossProfit() / marginBase) * 100 : 0;
  });
  readonly invoiceCount = computed(() => this.sales().filter((sale) => sale.documentType === SaleDocumentType.Invoice).length);
  readonly ticketCount = computed(() => this.sales().filter((sale) => sale.documentType === SaleDocumentType.Ticket).length);
  readonly voidedCount = computed(() => this.sales().filter((sale) => sale.status === SaleStatus.Voided).length);
  readonly authorizedCount = computed(() =>
    this.sales().filter((sale) =>
      sale.documentType === SaleDocumentType.Invoice
      && (sale.documentStatus === SaleDocumentStatus.Authorized || this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'AUTORIZADO')
    ).length
  );
  readonly companyTimeZoneId = computed(() => this.authStore.companyTimeZoneId());

  ngOnInit(): void {
    this.loadSales();
  }

  loadSales(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.salesReportService.getSales(this.currentFilters()).subscribe({
      next: (sales) => {
        this.sales.set(sales);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(this.resolveError(error, 'No se pudo cargar el reporte de ventas.'));
      },
    });
  }

  clearFilters(): void {
    this.from = '';
    this.to = '';
    this.search = '';
    this.status = null;
    this.documentType = null;
    this.documentStatus = null;
    this.loadSales();
  }

  openDetail(row: SalesReportRow): void {
    this.selectedSaleRow.set(row);
    this.selectedSale.set(null);
    this.detailErrorMessage.set('');
    this.detailLoading.set(true);
    this.detailVisible.set(true);

    this.salesReportService.getSaleDetail(row.id).subscribe({
      next: (sale) => {
        this.selectedSale.set({
          ...sale,
          username: sale.username ?? row.username,
          customerIdentification: sale.customerIdentification ?? row.customerIdentification,
        });
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailErrorMessage.set(this.resolveError(error, 'No se pudo cargar el detalle de la venta.'));
      },
    });
  }

  onDetailVisibleChange(visible: boolean): void {
    this.detailVisible.set(visible);

    if (!visible) {
      this.selectedSale.set(null);
      this.selectedSaleRow.set(null);
      this.detailErrorMessage.set('');
    }
  }

  exportCsv(): void {
    const rows = this.sales();

    if (rows.length === 0) {
      return;
    }

    const headers = [
      'ID',
      'Fecha',
      'Documento',
      'Cliente',
      'Identificacion cliente',
      'Tipo documento',
      'Estado venta',
      'Estado fiscal',
      'Total',
      'Costo total',
      'Utilidad bruta',
      'Margen bruto %',
      'Usuario',
      'Notas',
    ];

    const csvRows = rows.map((sale) => [
      sale.id,
      this.formatBusinessDateTime(sale.createdAt),
      sale.number ?? '',
      sale.customerName ?? '',
      sale.customerIdentification ?? '',
      this.documentTypeLabel(sale),
      this.saleStatusLabel(sale),
      this.fiscalStatusLabel(sale),
      this.csvMoneyValue(sale.total),
      this.csvMoneyValue(sale.totalCost),
      this.csvMoneyValue(sale.grossProfit),
      this.csvPercentValue(sale.grossMarginPercent),
      sale.username ?? '',
      sale.notes ?? '',
    ]);

    const csvSeparator = ';';
    const csv = [headers, ...csvRows]
      .map((row) => row.map((value) => this.csvValue(value)).join(csvSeparator))
      .join('\r\n');

    this.downloadCsv(csv);
  }

  saleStatusLabel(sale: SalesReportRow | SalesReportDetail): string {
    return saleStatusLabel(sale.status);
  }

  saleStatusSeverity(sale: SalesReportRow | SalesReportDetail) {
    return saleStatusSeverity(sale.status);
  }

  documentTypeLabel(sale: SalesReportRow | SalesReportDetail): string {
    return saleDocumentTypeLabel(sale.documentType);
  }

  documentStatusLabel(sale: SalesReportRow | SalesReportDetail): string {
    return saleDocumentStatusLabel(sale.documentStatus);
  }

  documentStatusSeverity(sale: SalesReportRow | SalesReportDetail) {
    return saleDocumentStatusSeverity(sale.documentStatus);
  }

  fiscalStatusLabel(sale: SalesReportRow | SalesReportDetail): string {
    if (sale.documentType !== SaleDocumentType.Invoice) {
      return 'No aplica';
    }

    if (sale.documentStatus === SaleDocumentStatus.Authorized || this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'AUTORIZADO') {
      return 'Autorizado SRI';
    }

    return this.documentStatusLabel(sale);
  }

  fiscalStatusSeverity(sale: SalesReportRow | SalesReportDetail) {
    if (sale.documentType !== SaleDocumentType.Invoice) {
      return 'secondary';
    }

    if (sale.documentStatus === SaleDocumentStatus.Authorized || this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'AUTORIZADO') {
      return 'success';
    }

    return this.documentStatusSeverity(sale);
  }

  customerLabel(sale: SalesReportRow | SalesReportDetail): string {
    return sale.customerName?.trim() || 'Consumidor final';
  }

  getVatLabel(item: SalesReportDetailItem): string {
    return getVatCategoryOption(item.vatCategory).shortLabel;
  }

  formatBusinessDate(value: string | Date | null | undefined): string {
    return formatBusinessDateValue(value, this.companyTimeZoneId());
  }

  formatBusinessTime(value: string | Date | null | undefined): string {
    return formatBusinessTimeValue(value, this.companyTimeZoneId());
  }

  formatBusinessDateTime(value: string | Date | null | undefined): string {
    return formatBusinessDateTimeValue(value, this.companyTimeZoneId());
  }

  private currentFilters(): SalesReportFilters {
    return {
      from: this.from || null,
      to: this.to || null,
      search: this.search || null,
      status: this.status,
      documentType: this.documentType,
      documentStatus: this.documentStatus,
    };
  }

  private resolveError(error: HttpErrorResponse, fallback: string): string {
    if (typeof error.error === 'object' && error.error !== null) {
      const payload = error.error as Record<string, unknown>;
      const code = payload['error'] ?? payload['code'];

      if (typeof code === 'string' && code.trim()) {
        return `${fallback} (${code})`;
      }
    }

    return fallback;
  }

  private csvValue(value: string | number): string {
    const text = String(value);
    const escaped = text.replace(/"/g, '""');

    return /[";\r\n]/.test(escaped) ? `"${escaped}"` : escaped;
  }

  private csvMoneyValue(value: number): string {
    return value.toFixed(2).replace('.', ',');
  }

  private csvPercentValue(value: number): string {
    return value.toFixed(2).replace('.', ',');
  }

  private downloadCsv(csv: string): void {
    const utf8Bom = '\uFEFF';
    const blob = new Blob([`${utf8Bom}${csv}`], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    const today = new Date().toISOString().slice(0, 10);

    anchor.href = url;
    anchor.download = `reporte-ventas-${today}.csv`;
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  private normalizeSriStatus(status: string | null | undefined): string {
    return status?.trim().toUpperCase() ?? '';
  }
}
