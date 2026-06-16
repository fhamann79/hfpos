import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TextareaModule } from 'primeng/textarea';
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
  @Input() loading = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() sendEmail = new EventEmitter<SendSaleInvoiceEmailRequest>();

  toEmail = '';
  ccEmail = '';
  subject = '';
  message = '';
  submitted = false;

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['visible'] || changes['sale']) && this.visible && !this.loading) {
      this.reset();
    }
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
    this.toEmail = this.sale?.customerEmail?.trim() ?? '';
    this.ccEmail = '';
    this.subject = '';
    this.message = '';
    this.submitted = false;
  }

  private isValidEmail(value: string): boolean {
    const normalized = value.trim();
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalized);
  }

  private normalizeOptional(value: string): string | null {
    const normalized = value.trim();
    return normalized.length ? normalized : null;
  }
}
