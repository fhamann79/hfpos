import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TextareaModule } from 'primeng/textarea';
import { CreditNote } from '../../../credit-notes/models/credit-note.model';
import { SendSaleInvoiceEmailRequest } from '../../models/sale-invoice-email.model';
import { Sale } from '../../models/sale.model';

@Component({
  selector: 'app-sale-invoice-email-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, ButtonModule, InputTextModule, MessageModule, TextareaModule],
  templateUrl: './sale-invoice-email-dialog.html',
  styleUrl: './sale-invoice-email-dialog.scss',
})
export class SaleInvoiceEmailDialog implements OnChanges {
  @Input({ required: true }) visible = false;
  @Input() sale: Sale | null = null;
  @Input() creditNote: CreditNote | null = null;
  @Input() loading = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() sendEmail = new EventEmitter<SendSaleInvoiceEmailRequest>();

  toEmail = '';
  ccEmail = '';
  subject = '';
  message = '';
  submitted = false;

  ngOnChanges(changes: SimpleChanges): void {
    if (
      (changes['visible'] || changes['sale'] || changes['creditNote'])
      && this.visible
      && !this.loading
    ) {
      this.reset();
    }
  }

  get isCreditNote(): boolean {
    return this.creditNote !== null && this.sale === null;
  }

  get dialogHeader(): string {
    return this.isCreditNote
      ? 'Enviar nota de crédito por email'
      : 'Enviar factura por email';
  }

  get documentTypeLabel(): string {
    return this.isCreditNote ? 'Nota de crédito' : 'Factura';
  }

  get documentNumber(): string {
    const document = this.isCreditNote ? this.creditNote : this.sale;
    return document?.number || (document ? `#${document.id}` : '-');
  }

  get authorizationNumber(): string {
    const document = this.isCreditNote ? this.creditNote : this.sale;
    return document?.authorizationNumber || '-';
  }

  get defaultRecipientEmail(): string {
    return this.isCreditNote
      ? this.creditNote?.buyerEmailSnapshot?.trim() ?? ''
      : this.sale?.customerEmail?.trim() ?? '';
  }

  submit(): void {
    this.submitted = true;

    if (!this.isValidEmail(this.toEmail) || (this.ccEmail.trim() && !this.isValidEmail(this.ccEmail))) {
      return;
    }

    this.sendEmail.emit({
      toEmail: this.toEmail.trim(),
      ccEmail: this.normalizeOptional(this.ccEmail),
      subject: this.normalizeOptional(this.subject),
      message: this.normalizeOptional(this.message),
    });
  }

  close(): void {
    if (this.loading) {
      return;
    }

    this.visibleChange.emit(false);
  }

  toEmailInvalid(): boolean {
    return this.submitted && !this.isValidEmail(this.toEmail);
  }

  ccEmailInvalid(): boolean {
    return this.submitted && !!this.ccEmail.trim() && !this.isValidEmail(this.ccEmail);
  }

  private reset(): void {
    this.toEmail = this.defaultRecipientEmail;
    this.ccEmail = '';
    this.subject = '';
    this.message = '';
    this.submitted = false;
  }

  private isValidEmail(value: string): boolean {
    const normalized = value.trim();
    return normalized.length <= 320
      && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalized);
  }

  private normalizeOptional(value: string): string | null {
    const normalized = value.trim();
    return normalized.length ? normalized : null;
  }
}
