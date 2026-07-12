import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';
import { CreditNoteEligibility } from '../../models/credit-note-eligibility.model';

@Component({
  selector: 'app-credit-note-eligibility-dialog',
  standalone: true,
  imports: [CommonModule, ButtonModule, DialogModule, MessageModule, TableModule],
  templateUrl: './credit-note-eligibility-dialog.html',
  styleUrl: './credit-note-eligibility-dialog.scss',
})
export class CreditNoteEligibilityDialog {
  @Input({ required: true }) visible = false;
  @Input() loading = false;
  @Input() errorMessage = '';
  @Input() eligibility: CreditNoteEligibility | null = null;
  @Input() companyTimeZoneId = 'America/Guayaquil';

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() refresh = new EventEmitter<void>();

  emissionDateLabel(): string {
    return formatBusinessDateTime(
      this.eligibility?.originalSaleDocumentIssuedAt,
      this.companyTimeZoneId
    ) || '-';
  }

  identificationTypeLabel(type: string | null): string {
    switch (type?.trim()) {
      case '04':
        return 'RUC';
      case '05':
        return 'Cédula';
      case '06':
        return 'Pasaporte';
      case '07':
        return 'Consumidor final';
      default:
        return type?.trim() || '-';
    }
  }
}
