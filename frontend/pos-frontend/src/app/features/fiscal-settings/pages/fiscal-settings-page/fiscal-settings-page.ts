import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { hasHttpBusinessError, resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import {
  CompanyBranding,
  CompanyEmailEncryptionMode,
  CompanyEmailSettings,
  CompanyFiscalSettings,
  CompanySriCertificate,
  CompanySriSettings,
  DocumentSequence,
  DocumentSequenceAudit,
  FiscalDocumentType,
  ReadinessTagSeverity,
  SelectOption,
  SriFiscalReadiness,
  SriFiscalReadinessCheck,
  certificateSeverity,
  certificateStatusLabel,
  fiscalDocumentTypeLabel,
  formatFiscalSequential,
  readinessCategoryLabel as resolveReadinessCategoryLabel,
  readinessCheckIcon as resolveReadinessCheckIcon,
  readinessSeverityToTagSeverity,
  readinessSummaryLabel,
  readinessSummarySeverity,
} from '../../models/fiscal-settings.model';
import { FiscalSettingsService } from '../../services/fiscal-settings.service';

type SequenceDialogMode = 'create' | 'update';
type CertificateSeverity = 'success' | 'secondary' | 'warn' | 'danger';
type ReadinessTarget = 'sandbox' | 'production';

interface ReadinessCheckGroup {
  category: string;
  categoryLabel: string;
  checks: SriFiscalReadinessCheck[];
}

@Component({
  selector: 'app-fiscal-settings-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    DatePipe,
    ButtonModule,
    ConfirmDialogModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TableModule,
    TabsModule,
    TagModule,
    TextareaModule,
    ToastModule,
    ToggleSwitchModule,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './fiscal-settings-page.html',
  styleUrl: './fiscal-settings-page.scss',
})
export class FiscalSettingsPage implements OnInit, OnDestroy {
  @ViewChild('certificateFileInput') private certificateFileInput?: ElementRef<HTMLInputElement>;
  @ViewChild('brandingLogoInput') private brandingLogoInput?: ElementRef<HTMLInputElement>;

  private static readonly maxBrandingLogoSizeBytes = 512 * 1024;
  private static readonly maxCertificateFileSizeBytes = 2 * 1024 * 1024;

  private readonly fb = inject(FormBuilder);
  private readonly fiscalSettingsService = inject(FiscalSettingsService);
  private readonly permissionService = inject(PermissionService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly canRead = computed(() => this.permissionService.hasPermission(PERMISSIONS.fiscalSettingsRead));
  readonly canWrite = computed(() => this.permissionService.hasPermission(PERMISSIONS.fiscalSettingsWrite));

  readonly companyLoading = signal(false);
  readonly companySaving = signal(false);
  readonly companyError = signal('');
  readonly companySettings = signal<CompanyFiscalSettings | null>(null);
  readonly brandingLoading = signal(false);
  readonly brandingSaving = signal(false);
  readonly brandingError = signal('');
  readonly logoUploading = signal(false);
  readonly logoDeleting = signal(false);
  readonly brandingLogoError = signal('');
  readonly brandingLogoPreviewUrl = signal<string | null>(null);
  readonly companyBranding = signal<CompanyBranding | null>(null);
  readonly emailLoading = signal(false);
  readonly emailSaving = signal(false);
  readonly emailTesting = signal(false);
  readonly emailError = signal('');
  readonly emailTestMessage = signal('');
  readonly emailSettings = signal<CompanyEmailSettings | null>(null);

  readonly sriLoading = signal(false);
  readonly sriSaving = signal(false);
  readonly sriError = signal('');
  readonly sriSettings = signal<CompanySriSettings | null>(null);
  readonly certificateLoading = signal(false);
  readonly certificateUploading = signal(false);
  readonly certificateDeleting = signal(false);
  readonly certificateError = signal('');
  readonly certificateUploadError = signal('');
  readonly sriCertificate = signal<CompanySriCertificate | null>(null);
  readonly selectedCertificateFile = signal<File | null>(null);
  readonly readiness = signal<SriFiscalReadiness | null>(null);
  readonly readinessLoading = signal(false);
  readonly readinessError = signal('');

  readonly sequences = signal<DocumentSequence[]>([]);
  readonly sequencesLoading = signal(false);
  readonly sequencesError = signal('');
  readonly sequenceSaving = signal(false);
  readonly selectedSequence = signal<DocumentSequence | null>(null);
  readonly sequenceDialogMode = signal<SequenceDialogMode>('update');

  readonly auditSequence = signal<DocumentSequence | null>(null);
  readonly audits = signal<DocumentSequenceAudit[]>([]);
  readonly auditsLoading = signal(false);
  readonly auditsError = signal('');
  readonly readinessBlockingChecks = computed(() =>
    this.sortReadinessChecks((this.readiness()?.checks ?? []).filter((check) => check.isBlocking && check.severity === 'error')),
  );
  readonly readinessWarningChecks = computed(() =>
    this.sortReadinessChecks((this.readiness()?.checks ?? []).filter((check) => check.severity === 'warning')),
  );
  readonly readinessSuccessChecks = computed(() =>
    this.sortReadinessChecks((this.readiness()?.checks ?? []).filter((check) => check.severity === 'success')),
  );
  readonly readinessChecksByCategory = computed<ReadinessCheckGroup[]>(() => {
    const groups = new Map<string, SriFiscalReadinessCheck[]>();

    for (const check of this.sortReadinessChecks(this.readiness()?.checks ?? [])) {
      const categoryChecks = groups.get(check.category) ?? [];
      categoryChecks.push(check);
      groups.set(check.category, categoryChecks);
    }

    return Array.from(groups.entries()).map(([category, checks]) => ({
      category,
      categoryLabel: resolveReadinessCategoryLabel(category),
      checks,
    }));
  });

  sequenceDialogVisible = false;
  auditDialogVisible = false;
  certificateDialogVisible = false;

  readonly environmentOptions: SelectOption<number>[] = [
    { label: 'Pruebas', value: 1 },
    { label: 'Producción', value: 2 },
  ];

  readonly emissionTypeOptions: SelectOption<number>[] = [{ label: 'Normal', value: 1 }];

  readonly emailEncryptionOptions: SelectOption<CompanyEmailEncryptionMode>[] = [
    { label: 'Sin cifrado', value: 'None' },
    { label: 'STARTTLS', value: 'StartTls' },
    { label: 'SSL directo', value: 'SslOnConnect' },
  ];

  readonly documentTypeOptions: SelectOption<FiscalDocumentType>[] = [
    { label: 'Ticket', value: FiscalDocumentType.Ticket },
    { label: 'Factura', value: FiscalDocumentType.Invoice },
  ];

  readonly companyForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    tradeName: ['', [Validators.maxLength(150)]],
    ruc: ['', [Validators.required, Validators.pattern(/^\d{13}$/)]],
    matrixAddress: ['', [Validators.maxLength(250)]],
    email: ['', [Validators.email, Validators.maxLength(150)]],
    phone: ['', [Validators.maxLength(30)]],
    isAccountingRequired: [false],
    specialTaxpayerNumber: ['', [Validators.maxLength(50)]],
    taxpayerRegime: ['', [Validators.maxLength(80)]],
  });

  readonly brandingForm = this.fb.nonNullable.group({
    primaryColor: ['#1d4ed8', [Validators.required, Validators.pattern(/^#[0-9A-Fa-f]{6}$/), Validators.maxLength(20)]],
    documentFooterText: ['', [Validators.maxLength(500)]],
  });

  readonly emailForm = this.fb.nonNullable.group({
    isEnabled: [false],
    smtpHost: ['', [Validators.maxLength(255)]],
    smtpPort: [587, [Validators.required, Validators.min(1), Validators.max(65535)]],
    encryptionMode: ['StartTls' as CompanyEmailEncryptionMode, [Validators.required]],
    smtpUsername: ['', [Validators.maxLength(255)]],
    smtpPassword: ['', [Validators.maxLength(500)]],
    fromEmail: ['', [Validators.email, Validators.maxLength(320)]],
    fromDisplayName: ['', [Validators.maxLength(150)]],
    replyToEmail: ['', [Validators.email, Validators.maxLength(320)]],
    testToEmail: ['', [Validators.email, Validators.maxLength(320)]],
  });

  readonly sriForm = this.fb.nonNullable.group({
    environment: [1, [Validators.required]],
    emissionType: [1, [Validators.required]],
    isEnabled: [false],
  });

  readonly certificateForm = this.fb.nonNullable.group({
    password: ['', [Validators.required]],
  });

  readonly sequenceForm = this.fb.nonNullable.group({
    establishmentId: [0, [Validators.required, Validators.min(1)]],
    emissionPointId: [0, [Validators.required, Validators.min(1)]],
    documentType: [FiscalDocumentType.Invoice, [Validators.required]],
    nextNumber: [1, [Validators.required, Validators.min(1)]],
    reason: ['', [Validators.required, Validators.maxLength(500)]],
  });

  ngOnInit(): void {
    this.syncWriteAccess();

    if (this.canRead()) {
      this.refreshAll();
    }
  }

  ngOnDestroy(): void {
    this.revokeBrandingLogoPreview();
  }

  refreshAll(): void {
    this.loadCompanySettings();
    this.loadBranding();
    this.loadEmailSettings();
    this.loadSriSettings();
    this.loadSriCertificate();
    this.loadSriReadiness();
    this.loadSequences();
  }

  loadCompanySettings(): void {
    this.companyLoading.set(true);
    this.companyError.set('');

    this.fiscalSettingsService.getCompanySettings().subscribe({
      next: (settings) => {
        this.companySettings.set(settings);
        this.patchCompanyForm(settings);
        this.syncWriteAccess();
        this.companyLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.companyLoading.set(false);
        this.companyError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la configuración fiscal de la empresa.'));
      },
    });
  }

  saveCompanySettings(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.companyForm.invalid) {
      this.companyForm.markAllAsTouched();
      return;
    }

    const values = this.companyForm.getRawValue();
    this.companySaving.set(true);

    this.fiscalSettingsService
      .updateCompanySettings({
        name: values.name.trim(),
        tradeName: this.normalizeOptional(values.tradeName),
        ruc: values.ruc.trim(),
        matrixAddress: this.normalizeOptional(values.matrixAddress),
        email: this.normalizeOptional(values.email),
        phone: this.normalizeOptional(values.phone),
        isAccountingRequired: values.isAccountingRequired,
        specialTaxpayerNumber: this.normalizeOptional(values.specialTaxpayerNumber),
        taxpayerRegime: this.normalizeOptional(values.taxpayerRegime),
      })
      .subscribe({
        next: (settings) => {
          this.companySettings.set(settings);
          this.patchCompanyForm(settings);
          this.companySaving.set(false);
          this.loadSriReadiness();
          this.messageService.add({
            severity: 'success',
            summary: 'Configuración actualizada',
            detail: 'Los datos fiscales de la empresa fueron guardados.',
          });
        },
        error: (error: HttpErrorResponse) => {
          this.companySaving.set(false);
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
  }

  loadBranding(): void {
    this.brandingLoading.set(true);
    this.brandingError.set('');
    this.brandingLogoError.set('');

    this.fiscalSettingsService.getBranding().subscribe({
      next: (branding) => {
        this.companyBranding.set(branding);
        this.patchBrandingForm(branding);
        this.syncWriteAccess();
        this.brandingLoading.set(false);

        if (branding.logoConfigured) {
          this.loadBrandingLogoPreview();
        } else {
          this.revokeBrandingLogoPreview();
        }
      },
      error: (error: HttpErrorResponse) => {
        this.brandingLoading.set(false);
        this.brandingError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la identidad visual de documentos.'));
      },
    });
  }

  saveBranding(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.brandingForm.invalid) {
      this.brandingForm.markAllAsTouched();
      return;
    }

    const values = this.brandingForm.getRawValue();
    this.brandingSaving.set(true);

    this.fiscalSettingsService
      .updateBranding({
        primaryColor: this.normalizeOptional(values.primaryColor),
        documentFooterText: this.normalizeOptional(values.documentFooterText),
      })
      .subscribe({
        next: (branding) => {
          this.companyBranding.set(branding);
          this.patchBrandingForm(branding);
          this.brandingSaving.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Identidad visual guardada',
            detail: 'La configuracion visual para documentos fue actualizada.',
          });
        },
        error: (error: HttpErrorResponse) => {
          this.brandingSaving.set(false);
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
  }

  onBrandingLogoSelected(event: Event): void {
    if (!this.canWrite()) {
      return;
    }

    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    this.brandingLogoError.set('');

    if (!file) {
      return;
    }

    const validationError = this.validateBrandingLogoFile(file);

    if (validationError) {
      this.brandingLogoError.set(validationError);
      this.resetBrandingLogoInput();
      return;
    }

    this.uploadBrandingLogo(file);
  }

  uploadBrandingLogo(file: File): void {
    if (!this.canWrite()) {
      return;
    }

    this.logoUploading.set(true);
    this.brandingLogoError.set('');

    this.fiscalSettingsService.uploadBrandingLogo(file).subscribe({
      next: (branding) => {
        this.companyBranding.set(branding);
        this.logoUploading.set(false);
        this.resetBrandingLogoInput();
        this.loadBrandingLogoPreview();
        this.messageService.add({
          severity: 'success',
          summary: 'Logo actualizado',
          detail: 'El logo para RIDE y documentos fue guardado.',
        });
      },
      error: (error: HttpErrorResponse) => {
        const message = resolveHttpErrorMessage(error, 'No se pudo cargar el logo.');
        this.logoUploading.set(false);
        this.resetBrandingLogoInput();
        this.brandingLogoError.set(message);
        this.messageService.add({ severity: 'error', summary: 'Error', detail: message });
      },
    });
  }

  confirmDeleteBrandingLogo(): void {
    if (!this.canWrite() || !this.companyBranding()?.logoConfigured) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar logo',
      message: 'El logo dejara de mostrarse en RIDE y documentos impresos. Los datos fiscales y XML SRI no cambiaran. Deseas continuar?',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar logo',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => this.deleteBrandingLogo(),
    });
  }

  deleteBrandingLogo(): void {
    if (!this.canWrite()) {
      return;
    }

    this.logoDeleting.set(true);
    this.brandingLogoError.set('');

    this.fiscalSettingsService.deleteBrandingLogo().subscribe({
      next: () => {
        this.logoDeleting.set(false);
        this.revokeBrandingLogoPreview();
        this.resetBrandingLogoInput();
        this.loadBranding();
        this.messageService.add({
          severity: 'success',
          summary: 'Logo eliminado',
          detail: 'El logo de documentos fue eliminado.',
        });
      },
      error: (error: HttpErrorResponse) => {
        this.logoDeleting.set(false);
        this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
      },
    });
  }

  loadEmailSettings(): void {
    this.emailLoading.set(true);
    this.emailError.set('');
    this.emailTestMessage.set('');

    this.fiscalSettingsService.getEmailSettings().subscribe({
      next: (settings) => {
        this.emailSettings.set(settings);
        this.patchEmailForm(settings);
        this.syncWriteAccess();
        this.emailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.emailLoading.set(false);
        this.emailError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la configuración de correo.'));
      },
    });
  }

  saveEmailSettings(clearPassword = false): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.emailForm.invalid) {
      this.emailForm.markAllAsTouched();
      return;
    }

    const values = this.emailForm.getRawValue();
    this.emailSaving.set(true);
    this.emailTestMessage.set('');

    this.fiscalSettingsService
      .updateEmailSettings({
        isEnabled: values.isEnabled,
        smtpHost: this.normalizeOptional(values.smtpHost),
        smtpPort: values.smtpPort,
        encryptionMode: values.encryptionMode,
        smtpUsername: this.normalizeOptional(values.smtpUsername),
        smtpPassword: this.normalizeOptional(values.smtpPassword),
        clearPassword,
        fromEmail: this.normalizeOptional(values.fromEmail),
        fromDisplayName: this.normalizeOptional(values.fromDisplayName),
        replyToEmail: this.normalizeOptional(values.replyToEmail),
      })
      .subscribe({
        next: (settings) => {
          this.emailSettings.set(settings);
          this.patchEmailForm(settings);
          this.emailSaving.set(false);
          this.messageService.add({
            severity: 'success',
            summary: clearPassword ? 'Contraseña limpiada' : 'Correo guardado',
            detail: clearPassword
              ? 'La contraseña/API Key SMTP fue eliminada.'
              : 'La configuración de correo fue guardada.',
          });
        },
        error: (error: HttpErrorResponse) => {
          this.emailSaving.set(false);
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
  }

  confirmClearEmailPassword(): void {
    if (!this.canWrite() || !this.emailSettings()?.passwordConfigured) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Limpiar contraseña SMTP',
      message: 'Se eliminará la contraseña/API Key guardada. La configuración se conservará, pero no se podrán enviar pruebas hasta ingresar una nueva.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Limpiar contraseña',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => this.saveEmailSettings(true),
    });
  }

  testEmailSettings(): void {
    if (!this.canWrite()) {
      return;
    }

    const toEmailControl = this.emailForm.controls.testToEmail;
    const toEmail = this.normalizeOptional(toEmailControl.value);

    if (!toEmail || toEmailControl.invalid) {
      toEmailControl.markAsTouched();
      this.messageService.add({
        severity: 'warn',
        summary: 'Email destino requerido',
        detail: 'Ingresa un correo destino válido para enviar la prueba SMTP.',
      });
      return;
    }

    this.emailTesting.set(true);
    this.emailTestMessage.set('');

    this.fiscalSettingsService
      .testEmailSettings({ toEmail })
      .subscribe({
        next: (result) => {
          this.emailTesting.set(false);
          this.emailTestMessage.set(result.message);
          this.loadEmailSettings();
          this.messageService.add({
            severity: result.success ? 'success' : 'error',
            summary: result.success ? 'Correo enviado' : 'Prueba fallida',
            detail: result.message,
          });
        },
        error: (error: HttpErrorResponse) => {
          this.emailTesting.set(false);
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
  }

  loadSriSettings(): void {
    this.sriLoading.set(true);
    this.sriError.set('');

    this.fiscalSettingsService.getSriSettings().subscribe({
      next: (settings) => {
        this.sriSettings.set(settings);
        this.sriForm.patchValue({
          environment: settings.environment,
          emissionType: settings.emissionType,
          isEnabled: settings.isEnabled,
        });
        this.syncWriteAccess();
        this.sriLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.sriLoading.set(false);
        this.sriError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la configuración SRI.'));
      },
    });
  }

  loadSriCertificate(): void {
    this.certificateLoading.set(true);
    this.certificateError.set('');

    this.fiscalSettingsService.getSriCertificate().subscribe({
      next: (certificate) => {
        this.sriCertificate.set(certificate);
        this.certificateLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.certificateLoading.set(false);

        if (hasHttpBusinessError(error, 'CERTIFICATE_NOT_FOUND')) {
          this.sriCertificate.set(null);
          return;
        }

        this.certificateError.set(resolveHttpErrorMessage(error, 'No se pudo cargar el certificado digital.'));
      },
    });
  }

  reloadSriSection(): void {
    this.loadSriSettings();
    this.loadSriCertificate();
    this.loadSriReadiness();
  }

  loadSriReadiness(): void {
    this.readinessLoading.set(true);
    this.readinessError.set('');

    this.fiscalSettingsService.getSriReadiness().subscribe({
      next: (readiness) => {
        this.readiness.set(readiness);
        this.readinessLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.readinessLoading.set(false);
        this.readinessError.set(resolveHttpErrorMessage(error, 'No se pudo cargar el diagnóstico SRI.'));
      },
    });
  }

  saveSriSettings(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.sriForm.invalid) {
      this.sriForm.markAllAsTouched();
      return;
    }

    const values = this.sriForm.getRawValue();
    this.sriSaving.set(true);

    this.fiscalSettingsService
      .updateSriSettings({
        environment: values.environment,
        emissionType: values.emissionType,
        isEnabled: values.isEnabled,
      })
      .subscribe({
        next: (settings) => {
          this.sriSettings.set(settings);
          this.sriSaving.set(false);
          this.loadSriReadiness();
          this.messageService.add({
            severity: 'success',
            summary: 'Configuración SRI guardada',
            detail: 'El ambiente y tipo de emisión fueron actualizados.',
          });
        },
        error: (error: HttpErrorResponse) => {
          this.sriSaving.set(false);
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
  }

  openCertificateDialog(): void {
    if (!this.canWrite()) {
      return;
    }

    this.resetCertificateUploadForm();
    this.certificateDialogVisible = true;
  }

  onCertificateDialogVisibleChange(visible: boolean): void {
    this.certificateDialogVisible = visible;

    if (!visible) {
      this.resetCertificateUploadForm();
      this.certificateUploading.set(false);
    }
  }

  onCertificateFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;

    this.selectedCertificateFile.set(file);
    this.certificateUploadError.set('');

    if (!file) {
      return;
    }

    const validationError = this.validateCertificateFile(file);

    if (validationError) {
      this.certificateUploadError.set(validationError);
    }
  }

  uploadSriCertificate(): void {
    if (!this.canWrite()) {
      return;
    }

    this.certificateUploadError.set('');

    const file = this.selectedCertificateFile();
    const password = this.certificateForm.controls.password.value;

    if (!file) {
      this.certificateUploadError.set('Debes seleccionar un archivo de certificado.');
      return;
    }

    const validationError = this.validateCertificateFile(file);

    if (validationError) {
      this.certificateUploadError.set(validationError);
      return;
    }

    if (this.certificateForm.invalid) {
      this.certificateForm.markAllAsTouched();
      return;
    }

    this.certificateUploading.set(true);

    this.fiscalSettingsService.uploadSriCertificate(file, password).subscribe({
      next: (certificate) => {
        this.sriCertificate.set(certificate);
        this.certificateUploading.set(false);
        this.certificateDialogVisible = false;
        this.resetCertificateUploadForm();
        this.loadSriSettings();
        this.loadSriCertificate();
        this.loadSriReadiness();
        this.messageService.add({
          severity: 'success',
          summary: 'Certificado cargado',
          detail: 'El certificado digital fue validado y configurado.',
        });
      },
      error: (error: HttpErrorResponse) => {
        const message = resolveHttpErrorMessage(error, 'No se pudo cargar el certificado digital.');
        this.certificateUploading.set(false);
        this.certificateUploadError.set(message);
        this.messageService.add({ severity: 'error', summary: 'Error', detail: message });
      },
    });
  }

  confirmDeleteSriCertificate(): void {
    if (!this.canWrite() || !this.sriCertificate()) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar certificado digital',
      message:
        'Esta acción desactivará el certificado configurado. No se eliminará el historial, pero la empresa quedará sin certificado activo para firmar XML SRI. ¿Deseas continuar?',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar certificado',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => this.deleteSriCertificate(),
    });
  }

  deleteSriCertificate(): void {
    if (!this.canWrite()) {
      return;
    }

    this.certificateDeleting.set(true);

    this.fiscalSettingsService.deleteSriCertificate().subscribe({
      next: () => {
        this.certificateDeleting.set(false);
        this.sriCertificate.set(null);
        this.loadSriSettings();
        this.loadSriCertificate();
        this.loadSriReadiness();
        this.messageService.add({
          severity: 'success',
          summary: 'Certificado eliminado',
          detail: 'El certificado activo fue desactivado.',
        });
      },
      error: (error: HttpErrorResponse) => {
        this.certificateDeleting.set(false);
        this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
      },
    });
  }

  loadSequences(): void {
    this.sequencesLoading.set(true);
    this.sequencesError.set('');

    this.fiscalSettingsService.getDocumentSequences().subscribe({
      next: (sequences) => {
        this.sequences.set(sequences);
        this.sequencesLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.sequencesLoading.set(false);
        this.sequencesError.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los secuenciales.'));
      },
    });
  }

  openCreateSequenceDialog(): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedSequence.set(null);
    this.sequenceDialogMode.set('create');
    this.sequenceForm.reset({
      establishmentId: 0,
      emissionPointId: 0,
      documentType: FiscalDocumentType.Invoice,
      nextNumber: 1,
      reason: '',
    });
    this.setSequenceContextControlsDisabled(false);
    this.sequenceDialogVisible = true;
  }

  openUpdateSequenceDialog(sequence: DocumentSequence): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedSequence.set(sequence);
    this.sequenceDialogMode.set('update');
    this.sequenceForm.reset({
      establishmentId: sequence.establishmentId,
      emissionPointId: sequence.emissionPointId,
      documentType: sequence.documentType,
      nextNumber: sequence.nextNumber,
      reason: '',
    });
    this.setSequenceContextControlsDisabled(true);
    this.sequenceDialogVisible = true;
  }

  onSequenceDialogVisibleChange(visible: boolean): void {
    this.sequenceDialogVisible = visible;

    if (!visible) {
      this.selectedSequence.set(null);
      this.sequenceSaving.set(false);
    }
  }

  confirmSaveSequence(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.sequenceForm.invalid) {
      this.sequenceForm.markAllAsTouched();
      return;
    }

    this.confirmationService.confirm({
      header: 'Confirmar ajuste de secuencial',
      message: 'Esta acción cambiará el próximo número de documento. El cambio quedará auditado. ¿Deseas continuar?',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Confirmar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'warn' },
      accept: () => this.saveSequence(),
    });
  }

  openAuditDialog(sequence: DocumentSequence): void {
    this.auditSequence.set(sequence);
    this.auditDialogVisible = true;
    this.audits.set([]);
    this.auditsError.set('');
    this.auditsLoading.set(true);

    this.fiscalSettingsService.getDocumentSequenceAudits(sequence.id).subscribe({
      next: (audits) => {
        this.audits.set(audits);
        this.auditsLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.auditsLoading.set(false);
        this.auditsError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la auditoría del secuencial.'));
      },
    });
  }

  onAuditDialogVisibleChange(visible: boolean): void {
    this.auditDialogVisible = visible;

    if (!visible) {
      this.auditSequence.set(null);
      this.audits.set([]);
      this.auditsError.set('');
    }
  }

  formatSequential(value: number | null | undefined): string {
    return formatFiscalSequential(value);
  }

  documentTypeLabel(value: FiscalDocumentType): string {
    return fiscalDocumentTypeLabel(value);
  }

  environmentLabel(value: number | null | undefined): string {
    return value === 2 ? 'Producción' : 'Pruebas';
  }

  emissionTypeLabel(value: number | null | undefined): string {
    return value === 1 ? 'Normal' : 'No válido';
  }

  formatLogoSize(value: number | null | undefined): string {
    if (!value) {
      return '-';
    }

    return value >= 1024 * 1024
      ? `${(value / 1024 / 1024).toFixed(1)} MB`
      : `${Math.ceil(value / 1024)} KB`;
  }

  isProductionEnvironment(): boolean {
    return this.sriForm.controls.environment.value === 2;
  }

  certificateStatusLabel(): string {
    return certificateStatusLabel(this.sriCertificate());
  }

  certificateSeverity(): CertificateSeverity {
    return certificateSeverity(this.sriCertificate());
  }

  certificateExpirationText(certificate: CompanySriCertificate): string {
    if (certificate.isExpired) {
      return 'Vencido';
    }

    return certificate.daysUntilExpiration === 1
      ? '1 día restante'
      : `${certificate.daysUntilExpiration} días restantes`;
  }

  certificatePrivateKeyLabel(certificate: CompanySriCertificate): string {
    return certificate.hasPrivateKey ? 'Disponible' : 'No disponible';
  }

  emailPasswordStatusLabel(): string {
    return this.emailSettings()?.passwordConfigured ? 'Contraseña configurada' : 'Sin contraseña';
  }

  emailPasswordSeverity(): 'success' | 'secondary' {
    return this.emailSettings()?.passwordConfigured ? 'success' : 'secondary';
  }

  emailLastTestSeverity(): 'success' | 'secondary' | 'danger' {
    const succeeded = this.emailSettings()?.lastTestSucceeded;

    if (succeeded === true) {
      return 'success';
    }

    if (succeeded === false) {
      return 'danger';
    }

    return 'secondary';
  }

  readinessStatusLabel(readiness: SriFiscalReadiness, target: ReadinessTarget): string {
    return readinessSummaryLabel(this.isReadyForTarget(readiness, target));
  }

  readinessStatusSeverity(readiness: SriFiscalReadiness, target: ReadinessTarget): ReadinessTagSeverity {
    return readinessSummarySeverity(this.isReadyForTarget(readiness, target), target === 'production');
  }

  readinessCategoryLabel(category: string): string {
    return resolveReadinessCategoryLabel(category);
  }

  readinessCheckSeverity(check: SriFiscalReadinessCheck): ReadinessTagSeverity {
    return readinessSeverityToTagSeverity(check.severity);
  }

  readinessCheckIcon(check: SriFiscalReadinessCheck): string {
    return resolveReadinessCheckIcon(check);
  }

  readinessSeverityLabel(check: SriFiscalReadinessCheck): string {
    switch (check.severity) {
      case 'success':
        return 'Correcto';
      case 'warning':
        return 'Advertencia';
      case 'error':
        return 'Error';
      case 'info':
        return 'Info';
    }
  }

  private saveSequence(): void {
    const values = this.sequenceForm.getRawValue();
    const reason = values.reason.trim();
    this.sequenceSaving.set(true);

    if (this.sequenceDialogMode() === 'create') {
      this.fiscalSettingsService
        .createDocumentSequence({
          establishmentId: values.establishmentId,
          emissionPointId: values.emissionPointId,
          documentType: values.documentType,
          nextNumber: values.nextNumber,
          reason,
        })
        .subscribe({
          next: () => this.handleSequenceSaved('Secuencia inicializada.'),
          error: (error: HttpErrorResponse) => this.handleSequenceSaveError(error),
        });
      return;
    }

    const sequence = this.selectedSequence();

    if (!sequence) {
      this.sequenceSaving.set(false);
      return;
    }

    this.fiscalSettingsService
      .updateDocumentSequence(sequence.id, {
        nextNumber: values.nextNumber,
        reason,
      })
      .subscribe({
        next: () => this.handleSequenceSaved('Secuencia ajustada.'),
        error: (error: HttpErrorResponse) => this.handleSequenceSaveError(error),
      });
  }

  private handleSequenceSaved(detail: string): void {
    this.sequenceSaving.set(false);
    this.sequenceDialogVisible = false;
    this.selectedSequence.set(null);
    this.loadSequences();
    this.loadSriReadiness();
    this.messageService.add({ severity: 'success', summary: 'Secuencial actualizado', detail });
  }

  private handleSequenceSaveError(error: HttpErrorResponse): void {
    this.sequenceSaving.set(false);
    this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
  }

  private patchCompanyForm(settings: CompanyFiscalSettings): void {
    this.companyForm.patchValue({
      name: settings.name,
      tradeName: settings.tradeName ?? '',
      ruc: settings.ruc,
      matrixAddress: settings.matrixAddress ?? '',
      email: settings.email ?? '',
      phone: settings.phone ?? '',
      isAccountingRequired: settings.isAccountingRequired,
      specialTaxpayerNumber: settings.specialTaxpayerNumber ?? '',
      taxpayerRegime: settings.taxpayerRegime ?? '',
    });
  }

  private patchBrandingForm(branding: CompanyBranding): void {
    this.brandingForm.patchValue({
      primaryColor: branding.primaryColor ?? '#1d4ed8',
      documentFooterText: branding.documentFooterText ?? '',
    });
  }

  private patchEmailForm(settings: CompanyEmailSettings): void {
    this.emailForm.patchValue({
      isEnabled: settings.isEnabled,
      smtpHost: settings.smtpHost ?? '',
      smtpPort: settings.smtpPort || 587,
      encryptionMode: settings.encryptionMode ?? 'StartTls',
      smtpUsername: settings.smtpUsername ?? '',
      smtpPassword: '',
      fromEmail: settings.fromEmail ?? '',
      fromDisplayName: settings.fromDisplayName ?? '',
      replyToEmail: settings.replyToEmail ?? '',
    });
  }

  private loadBrandingLogoPreview(): void {
    this.fiscalSettingsService.getBrandingLogoBlob().subscribe({
      next: (blob) => {
        this.revokeBrandingLogoPreview();
        this.brandingLogoPreviewUrl.set(URL.createObjectURL(blob));
      },
      error: (error: HttpErrorResponse) => {
        if (hasHttpBusinessError(error, 'COMPANY_LOGO_NOT_FOUND')) {
          this.revokeBrandingLogoPreview();
          return;
        }

        this.brandingLogoError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la vista previa del logo.'));
      },
    });
  }

  private syncWriteAccess(): void {
    if (this.canWrite()) {
      this.companyForm.enable({ emitEvent: false });
      this.brandingForm.enable({ emitEvent: false });
      this.emailForm.enable({ emitEvent: false });
      this.sriForm.enable({ emitEvent: false });
      this.certificateForm.enable({ emitEvent: false });
      return;
    }

    this.companyForm.disable({ emitEvent: false });
    this.brandingForm.disable({ emitEvent: false });
    this.emailForm.disable({ emitEvent: false });
    this.sriForm.disable({ emitEvent: false });
    this.certificateForm.disable({ emitEvent: false });
    this.sequenceForm.disable({ emitEvent: false });
  }

  private setSequenceContextControlsDisabled(disabled: boolean): void {
    const controls = [
      this.sequenceForm.controls.establishmentId,
      this.sequenceForm.controls.emissionPointId,
      this.sequenceForm.controls.documentType,
    ];

    for (const control of controls) {
      if (disabled) {
        control.disable({ emitEvent: false });
      } else {
        control.enable({ emitEvent: false });
      }
    }

    this.sequenceForm.controls.nextNumber.enable({ emitEvent: false });
    this.sequenceForm.controls.reason.enable({ emitEvent: false });
  }

  private isReadyForTarget(readiness: SriFiscalReadiness, target: ReadinessTarget): boolean {
    return target === 'sandbox' ? readiness.isReadyForSandboxSubmission : readiness.isReadyForProductionSubmission;
  }

  private sortReadinessChecks(checks: SriFiscalReadinessCheck[]): SriFiscalReadinessCheck[] {
    return [...checks].sort((left, right) => {
      const rankDifference = this.readinessSortRank(left) - this.readinessSortRank(right);

      if (rankDifference !== 0) {
        return rankDifference;
      }

      const categoryDifference = this.readinessCategoryLabel(left.category).localeCompare(
        this.readinessCategoryLabel(right.category),
        'es',
      );

      if (categoryDifference !== 0) {
        return categoryDifference;
      }

      return left.title.localeCompare(right.title, 'es');
    });
  }

  private readinessSortRank(check: SriFiscalReadinessCheck): number {
    if (check.isBlocking && check.severity === 'error') {
      return 0;
    }

    if (check.severity === 'error') {
      return 1;
    }

    if (check.severity === 'warning') {
      return 2;
    }

    if (check.severity === 'info') {
      return 3;
    }

    return 4;
  }

  private normalizeOptional(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  private validateBrandingLogoFile(file: File): string | null {
    const allowedTypes = ['image/png', 'image/jpeg', 'image/webp'];
    const fileName = file.name.toLowerCase();
    const hasValidExtension = fileName.endsWith('.png')
      || fileName.endsWith('.jpg')
      || fileName.endsWith('.jpeg')
      || fileName.endsWith('.webp');

    if (file.type && !allowedTypes.includes(file.type)) {
      return 'El logo debe ser PNG, JPG o WEBP.';
    }

    if (!hasValidExtension) {
      return 'El archivo debe tener extension .png, .jpg, .jpeg o .webp.';
    }

    if (file.size > FiscalSettingsPage.maxBrandingLogoSizeBytes) {
      return 'El logo no debe superar 512 KB.';
    }

    return null;
  }

  private resetBrandingLogoInput(): void {
    if (this.brandingLogoInput?.nativeElement) {
      this.brandingLogoInput.nativeElement.value = '';
    }
  }

  private revokeBrandingLogoPreview(): void {
    const currentUrl = this.brandingLogoPreviewUrl();

    if (currentUrl) {
      URL.revokeObjectURL(currentUrl);
    }

    this.brandingLogoPreviewUrl.set(null);
  }

  private validateCertificateFile(file: File): string | null {
    const fileName = file.name.toLowerCase();
    const hasValidExtension = fileName.endsWith('.p12') || fileName.endsWith('.pfx');

    if (!hasValidExtension) {
      return 'El archivo debe tener extensión .p12 o .pfx.';
    }

    if (file.size > FiscalSettingsPage.maxCertificateFileSizeBytes) {
      return 'El archivo no debe superar 2 MB.';
    }

    return null;
  }

  private resetCertificateUploadForm(): void {
    this.certificateForm.reset({ password: '' });
    this.selectedCertificateFile.set(null);
    this.certificateUploadError.set('');

    if (this.certificateFileInput?.nativeElement) {
      this.certificateFileInput.nativeElement.value = '';
    }
  }
}
