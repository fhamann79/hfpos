import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';
import { SriRide } from '../../models/sri-ride.model';

@Component({
  selector: 'app-sri-ride-dialog',
  standalone: true,
  imports: [CommonModule, DialogModule, ButtonModule, MessageModule],
  templateUrl: './sri-ride-dialog.html',
  styleUrl: './sri-ride-dialog.scss',
})
export class SriRideDialog {
  @ViewChild('ridePrintArea') private ridePrintArea?: ElementRef<HTMLElement>;

  private static readonly defaultFooterNote = 'Representacion impresa de comprobante electronico autorizado.';

  @Input({ required: true }) visible = false;
  @Input() ride: SriRide | null = null;
  @Input() loading = false;
  @Input() errorMessage = '';
  @Input() companyTimeZoneId = 'America/Guayaquil';

  @Output() visibleChange = new EventEmitter<boolean>();

  printRide(): void {
    if (typeof window === 'undefined' || typeof document === 'undefined' || !this.ridePrintArea) {
      return;
    }

    const printHost = document.createElement('div');
    printHost.className = 'ride-print-clone';
    printHost.appendChild(this.ridePrintArea.nativeElement.cloneNode(true));
    document.body.appendChild(printHost);
    document.body.classList.add('ride-printing');

    let cleaned = false;
    let fallbackCleanupId: number | undefined;
    const cleanup = () => {
      if (cleaned) {
        return;
      }

      cleaned = true;
      if (fallbackCleanupId !== undefined) {
        window.clearTimeout(fallbackCleanupId);
      }
      document.body.classList.remove('ride-printing');
      printHost.remove();
      window.removeEventListener('afterprint', cleanup);
    };

    fallbackCleanupId = window.setTimeout(cleanup, 60000);
    window.addEventListener('afterprint', cleanup, { once: true });

    try {
      window.print();
    } catch (error) {
      cleanup();
      throw error;
    }
  }

  formatMoney(value: number | null | undefined, ride: SriRide): string {
    const parsedValue = typeof value === 'number' ? value : Number(value ?? 0);
    const amount = Number.isFinite(parsedValue) ? parsedValue : 0;
    const formattedAmount = new Intl.NumberFormat('en-US', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);

    const currencyDisplay = this.currencySymbolOrLabel(ride);
    const separator = currencyDisplay === '$' ? '' : ' ';

    return `${currencyDisplay}${separator}${formattedAmount}`;
  }

  currencySymbolOrLabel(ride: SriRide): string {
    const rawCurrency = ride.totals.currency?.trim().toUpperCase() || 'USD';
    const normalized = rawCurrency.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    const compactCurrency = normalized.replace(/[^A-Z$]/g, '');

    if (
      compactCurrency === '$' ||
      compactCurrency === 'USD' ||
      compactCurrency === 'DOLAR' ||
      compactCurrency === 'DOLARES' ||
      compactCurrency === 'DOLLAR' ||
      compactCurrency === 'DOLLARS' ||
      compactCurrency === 'USDOLLAR' ||
      compactCurrency === 'USDOLLARS'
    ) {
      return '$';
    }

    return normalized;
  }

  buyerIdentification(ride: SriRide): string {
    const type = ride.buyer.identificationType?.trim();
    const identification = ride.buyer.identification?.trim();

    if (!type && !identification) {
      return '-';
    }

    if (this.isFinalConsumer(ride)) {
      return identification || '-';
    }

    if (!type || type === identification) {
      return identification || '-';
    }

    if (!identification) {
      return type;
    }

    return `${type}: ${identification}`;
  }

  authorizationDateLabel(ride: SriRide): string {
    const timeZoneId = ride.timeZoneId?.trim()
      || this.companyTimeZoneId
      || 'America/Guayaquil';

    return formatBusinessDateTime(ride.authorizationDate, timeZoneId) || '-';
  }

  calendarDateLabel(value: string | null | undefined): string {
    const normalized = value?.trim();
    if (!normalized) {
      return '-';
    }

    const isoDate = /^(\d{4})-(\d{2})-(\d{2})/.exec(normalized);
    if (isoDate) {
      return `${isoDate[3]}/${isoDate[2]}/${isoDate[1]}`;
    }

    const localDate = /^(\d{1,2})[/-](\d{1,2})[/-](\d{4})/.exec(normalized);
    if (!localDate) {
      return '-';
    }

    return `${localDate[1].padStart(2, '0')}/${localDate[2].padStart(2, '0')}/${localDate[3]}`;
  }

  footerNote(ride: SriRide): string {
    return ride.branding?.documentFooterText?.trim()
      || ride.footerNote?.trim()
      || SriRideDialog.defaultFooterNote;
  }

  hasCustomFooterNote(ride: SriRide): boolean {
    const configuredFooter = ride.branding?.documentFooterText?.trim();

    return !!configuredFooter && configuredFooter !== SriRideDialog.defaultFooterNote;
  }

  issuerInitials(ride: SriRide): string {
    const name = ride.issuer.tradeName || ride.issuer.legalName || 'HFPOS';
    const initials = name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part.charAt(0).toUpperCase())
      .join('');

    return initials || 'HF';
  }

  private isFinalConsumer(ride: SriRide): boolean {
    const name = ride.buyer.legalName?.trim().toUpperCase();
    const identification = ride.buyer.identification?.trim();

    return name === 'CONSUMIDOR FINAL' && identification === '9999999999999';
  }
}
