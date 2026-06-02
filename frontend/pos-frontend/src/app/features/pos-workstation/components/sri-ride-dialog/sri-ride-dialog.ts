import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { SriRide } from '../../models/sri-ride.model';

@Component({
  selector: 'app-sri-ride-dialog',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, DialogModule, ButtonModule, MessageModule],
  templateUrl: './sri-ride-dialog.html',
  styleUrl: './sri-ride-dialog.scss',
})
export class SriRideDialog {
  @Input({ required: true }) visible = false;
  @Input() ride: SriRide | null = null;
  @Input() loading = false;
  @Input() errorMessage = '';

  @Output() visibleChange = new EventEmitter<boolean>();

  printRide(): void {
    if (typeof window === 'undefined') {
      return;
    }

    window.print();
  }

  currencyCode(ride: SriRide): string {
    return ride.totals.currency || 'USD';
  }
}
