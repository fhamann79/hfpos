import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToolbarModule } from 'primeng/toolbar';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { Company } from '../../models/company.model';
import { CompanyService } from '../../services/company.service';
import { CompanyDialog, CompanyDialogSubmit } from './company-dialog';

@Component({
  selector: 'app-companies-table',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TableModule,
    ButtonModule,
    InputTextModule,
    ToolbarModule,
    TagModule,
    MessageModule,
    CompanyDialog,
  ],
  templateUrl: './companies-table.html',
  styleUrl: './companies-table.scss',
})
export class CompaniesTable implements OnInit {
  @Input() canWrite = false;
  @Output() companySelected = new EventEmitter<Company | null>();

  private readonly companyService = inject(CompanyService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly companies = signal<Company[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  globalFilter = '';
  dialogVisible = false;
  selectedCompany: Company | null = null;
  editingCompany: Company | null = null;

  ngOnInit(): void {
    this.loadCompanies();
  }

  loadCompanies(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.companyService.getAll().subscribe({
      next: (companies) => {
        this.companies.set(companies);
        if (this.selectedCompany) {
          const stillExists = companies.find((company) => company.id === this.selectedCompany?.id) ?? null;
          this.selectedCompany = stillExists;
          this.companySelected.emit(stillExists);
        }
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar las compañías.'));
      },
    });
  }

  selectCompany(company: Company): void {
    this.selectedCompany = company;
    this.companySelected.emit(company);
  }

  openCreateDialog(): void {
    if (!this.canWrite) {
      return;
    }

    this.editingCompany = null;
    this.dialogVisible = true;
  }

  openEditDialog(company: Company): void {
    if (!this.canWrite) {
      return;
    }

    this.editingCompany = company;
    this.dialogVisible = true;
  }

  onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible = visible;
    if (!visible) {
      this.editingCompany = null;
    }
  }

  submitDialog(event: CompanyDialogSubmit): void {
    if (!this.canWrite) {
      return;
    }

    if (event.mode === 'create') {
      this.companyService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Compañía creada.' });
          this.dialogVisible = false;
          this.loadCompanies();
        },
        error: (error: HttpErrorResponse) => {
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
      return;
    }

    this.companyService.update(event.id, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Compañía actualizada.' });
        this.dialogVisible = false;
        this.loadCompanies();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
      },
    });
  }

  confirmDelete(company: Company): void {
    if (!this.canWrite) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar compañía',
      message: `¿Deseas eliminar la compañía "${company.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => {
        this.companyService.delete(company.id).subscribe({
          next: () => {
            if (this.selectedCompany?.id === company.id) {
              this.selectedCompany = null;
              this.companySelected.emit(null);
            }
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Compañía eliminada.' });
            this.loadCompanies();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
          },
        });
      },
    });
  }

  isSelected(company: Company): boolean {
    return this.selectedCompany?.id === company.id;
  }

}
