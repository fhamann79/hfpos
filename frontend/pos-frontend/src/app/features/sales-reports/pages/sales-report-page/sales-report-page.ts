import { CommonModule, CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import {
  SaleDocumentStatus,
  SaleDocumentType,
  SaleStatus,
  SalesReportDetail,
  SalesReportDetailItem,
  SalesReportQuery,
  SalesReportRow,
  SalesReportSortField,
  SalesReportSummary,
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

const EMPTY_SUMMARY: SalesReportSummary = {
  salesCount: 0,
  totalSold: 0,
  authorizedCreditNoteTotal: 0,
  authorizedCreditNoteCount: 0,
  netTotal: 0,
  netCost: 0,
  netGrossProfit: 0,
  netGrossMarginPercent: 0,
  invoiceCount: 0,
  ticketCount: 0,
  voidedCount: 0,
  authorizedCount: 0,
};

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
  readonly summary = signal<SalesReportSummary>(EMPTY_SUMMARY);
  readonly totalItems = signal(0);
  readonly totalPages = signal(0);
  readonly currentPage = signal(1);
  readonly loading = signal(false);
  readonly exporting = signal(false);
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
  first = 0;
  rows = 15;
  sortField: SalesReportSortField = 'createdAt';
  sortOrder = -1;

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

  readonly salesCount = computed(() => this.summary().salesCount);
  readonly totalSold = computed(() => this.summary().totalSold);
  readonly creditNoteTotal = computed(() => this.summary().authorizedCreditNoteTotal);
  readonly creditNoteCount = computed(() => this.summary().authorizedCreditNoteCount);
  readonly netTotal = computed(() => this.summary().netTotal);
  readonly netCost = computed(() => this.summary().netCost);
  readonly netGrossProfit = computed(() => this.summary().netGrossProfit);
  readonly netGrossMarginPercent = computed(() => this.summary().netGrossMarginPercent);
  readonly invoiceCount = computed(() => this.summary().invoiceCount);
  readonly ticketCount = computed(() => this.summary().ticketCount);
  readonly voidedCount = computed(() => this.summary().voidedCount);
  readonly authorizedCount = computed(() => this.summary().authorizedCount);
  readonly companyTimeZoneId = computed(() => this.authStore.companyTimeZoneId());

  ngOnInit(): void {
    this.loadSales(1, this.rows);
  }

  loadSales(page = this.currentPage(), pageSize = this.rows): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.salesReportService.getSales(this.currentQuery(page, pageSize, true)).subscribe({
      next: (result) => {
        if (result.totalPages > 0 && result.page > result.totalPages) {
          this.loadSales(result.totalPages, result.pageSize);
          return;
        }

        this.sales.set(result.items);
        this.summary.set(result.summary ?? EMPTY_SUMMARY);
        this.totalItems.set(result.totalItems);
        this.totalPages.set(result.totalPages);
        this.rows = result.pageSize;
        this.first = result.totalItems === 0 ? 0 : (result.page - 1) * result.pageSize;
        this.currentPage.set(result.totalItems === 0 ? 1 : result.page);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(this.resolveError(error, 'No se pudo cargar el reporte de ventas.'));
      },
    });
  }

  onSalesLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.rows;
    const requestedSort = typeof event.sortField === 'string' && this.isSortField(event.sortField)
      ? event.sortField
      : this.sortField;
    const requestedOrder = event.sortOrder === 1 || event.sortOrder === -1
      ? event.sortOrder
      : this.sortOrder;
    const sortChanged = requestedSort !== this.sortField || requestedOrder !== this.sortOrder;

    this.sortField = requestedSort;
    this.sortOrder = requestedOrder;
    const first = sortChanged ? 0 : (event.first ?? this.first);
    this.loadSales(Math.floor(first / rows) + 1, rows);
  }

  applyFilters(): void {
    this.first = 0;
    this.currentPage.set(1);
    this.loadSales(1, this.rows);
  }

  clearFilters(): void {
    this.from = '';
    this.to = '';
    this.search = '';
    this.status = null;
    this.documentType = null;
    this.documentStatus = null;
    this.applyFilters();
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
          customerName: sale.customerName ?? row.customerName,
          customerIdentification: sale.customerIdentification ?? row.customerIdentification,
          customerEmail: sale.customerEmail ?? row.customerEmail,
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
    if (this.salesCount() === 0 || this.exporting()) {
      return;
    }

    this.exporting.set(true);
    this.errorMessage.set('');
    this.salesReportService.exportSales(this.currentQuery(1, this.rows, false)).subscribe({
      next: (blob) => {
        this.downloadCsv(blob);
        this.exporting.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.exporting.set(false);
        this.errorMessage.set(this.resolveError(error, 'No se pudo exportar el reporte de ventas.'));
      },
    });
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

  buyerName(sale: SalesReportDetail): string {
    return this.trimToNull(sale.buyerNameSnapshot)
      ?? this.trimToNull(sale.customerName)
      ?? 'Consumidor final';
  }

  buyerIdentificationTypeLabel(sale: SalesReportDetail): string {
    const type = this.trimToNull(sale.buyerIdentificationTypeSnapshot);

    switch (type) {
      case '04':
        return 'RUC';
      case '05':
        return 'Cédula';
      case '06':
        return 'Pasaporte';
      case '07':
        return 'Consumidor final';
      default:
        return '-';
    }
  }

  buyerIdentification(sale: SalesReportDetail): string {
    return this.trimToNull(sale.buyerIdentificationSnapshot)
      ?? this.trimToNull(sale.customerIdentification)
      ?? '-';
  }

  buyerEmail(sale: SalesReportDetail): string {
    return this.trimToNull(sale.buyerEmailSnapshot)
      ?? this.trimToNull(sale.customerEmail)
      ?? '-';
  }

  buyerAddress(sale: SalesReportDetail): string {
    return this.trimToNull(sale.buyerAddressSnapshot) ?? '-';
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

  private currentQuery(page: number, pageSize: number, includeSummary: boolean): SalesReportQuery {
    return {
      from: this.from || null,
      to: this.to || null,
      search: this.search || null,
      status: this.status,
      documentType: this.documentType,
      documentStatus: this.documentStatus,
      page,
      pageSize,
      includeSummary,
      sortBy: this.sortField,
      sortDirection: this.sortOrder === 1 ? 'asc' : 'desc',
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

  private downloadCsv(blob: Blob): void {
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

  private isSortField(value: string): value is SalesReportSortField {
    return [
      'createdAt',
      'number',
      'customerName',
      'documentType',
      'status',
      'documentStatus',
      'total',
      'username',
    ].includes(value);
  }

  private normalizeSriStatus(status: string | null | undefined): string {
    return status?.trim().toUpperCase() ?? '';
  }

  private trimToNull(value: string | null | undefined): string | null {
    const trimmed = value?.trim();

    return trimmed ? trimmed : null;
  }
}
