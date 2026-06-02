import { CommonModule, DatePipe } from '@angular/common';
import { Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { SriRide } from '../../models/sri-ride.model';

@Component({
  selector: 'app-sri-ride-dialog',
  standalone: true,
  imports: [CommonModule, DatePipe, DialogModule, ButtonModule, MessageModule],
  templateUrl: './sri-ride-dialog.html',
  styleUrl: './sri-ride-dialog.scss',
})
export class SriRideDialog {
  @ViewChild('ridePrintArea') private ridePrintArea?: ElementRef<HTMLElement>;

  @Input({ required: true }) visible = false;
  @Input() ride: SriRide | null = null;
  @Input() loading = false;
  @Input() errorMessage = '';

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

    return `${this.currencyLabel(ride)} ${formattedAmount}`;
  }

  currencyLabel(ride: SriRide): string {
    const rawCurrency = ride.totals.currency?.trim().toUpperCase() || 'USD';
    const normalized = rawCurrency.normalize('NFD').replace(/[\u0300-\u036f]/g, '');

    if (normalized === 'DOLAR' || normalized === 'DOLARES' || normalized.includes('DOLLAR')) {
      return 'USD';
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

  footerNote(): string {
    return 'Representación impresa de comprobante electrónico autorizado.';
  }

  private isFinalConsumer(ride: SriRide): boolean {
    const name = ride.buyer.legalName?.trim().toUpperCase();
    const identification = ride.buyer.identification?.trim();

    return name === 'CONSUMIDOR FINAL' && identification === '9999999999999';
  }
}
