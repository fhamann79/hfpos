import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { Sale } from '../../models/sale.model';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';
import {
  SriSubmissionAttempt,
  sriAuthorizationStatusLabel,
  sriAuthorizationStatusSeverity,
  sriReceptionStatusLabel,
  sriReceptionStatusSeverity,
  sriSubmissionAttemptStatusLabel,
  sriSubmissionAttemptStatusSeverity,
  sriSubmissionAttemptTypeLabel,
} from '../../models/sri-submission-attempt.model';

@Component({
  selector: 'app-sri-submission-attempts-dialog',
  standalone: true,
  imports: [CommonModule, DialogModule, ButtonModule, MessageModule, TableModule, TagModule],
  templateUrl: './sri-submission-attempts-dialog.html',
  styleUrl: './sri-submission-attempts-dialog.scss',
})
export class SriSubmissionAttemptsDialog {
  @Input({ required: true }) visible = false;
  @Input() sale: Sale | null = null;
  @Input() attempts: SriSubmissionAttempt[] = [];
  @Input() loading = false;
  @Input() errorMessage = '';
  @Input() companyTimeZoneId = 'America/Guayaquil';

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() refresh = new EventEmitter<void>();

  attemptTypeLabel(attempt: SriSubmissionAttempt): string {
    return sriSubmissionAttemptTypeLabel(attempt.attemptType);
  }

  attemptStatusLabel(attempt: SriSubmissionAttempt): string {
    return sriSubmissionAttemptStatusLabel(attempt.status);
  }

  attemptStatusSeverity(attempt: SriSubmissionAttempt) {
    return sriSubmissionAttemptStatusSeverity(attempt.status);
  }

  receptionStatusLabel(attempt: SriSubmissionAttempt): string {
    return sriReceptionStatusLabel(attempt.receptionStatus);
  }

  receptionStatusSeverity(attempt: SriSubmissionAttempt) {
    return sriReceptionStatusSeverity(attempt.receptionStatus);
  }

  authorizationStatusLabel(attempt: SriSubmissionAttempt): string {
    return sriAuthorizationStatusLabel(attempt.authorizationStatus);
  }

  authorizationStatusSeverity(attempt: SriSubmissionAttempt) {
    return sriAuthorizationStatusSeverity(attempt.authorizationStatus);
  }

  primaryMessage(attempt: SriSubmissionAttempt): string {
    return attempt.sriMessage || attempt.errorMessage || '-';
  }

  attemptCreatedAtLabel(attempt: SriSubmissionAttempt): string {
    return formatBusinessDateTime(attempt.createdAt, this.companyTimeZoneId) || '-';
  }
}
