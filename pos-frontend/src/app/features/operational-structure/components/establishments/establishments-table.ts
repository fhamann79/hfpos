import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
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
import { Establishment } from '../../models/establishment.model';
import { EstablishmentService } from '../../services/establishment.service';
import { EstablishmentDialog, EstablishmentDialogSubmit } from './establishment-dialog';

@Component({
  selector: 'app-establishments-table',
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
    EstablishmentDialog,
  ],
  templateUrl: './establishments-table.html',
  styleUrl: './establishments-table.scss',
})
export class EstablishmentsTable implements OnChanges {
  @Input() canWrite = false;
  @Input() selectedCompany: Company | null = null;
  @Output() establishmentSelected = new EventEmitter<Establishment | null>();

  private readonly establishmentService = inject(EstablishmentService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly establishments = signal<Establishment[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  globalFilter = '';
  dialogVisible = false;
  selectedEstablishment: Establishment | null = null;
  editingEstablishment: Establishment | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['selectedCompany']) {
      this.selectedEstablishment = null;
      this.establishmentSelected.emit(null);
      this.loadEstablishments();
    }
  }

  loadEstablishments(): void {
    if (!this.selectedCompany) {
      this.establishments.set([]);
      this.errorMessage.set('');
      return;
    }

    this.loading.set(true);
    this.errorMessage.set('');

    this.establishmentService.getAll(this.selectedCompany.id).subscribe({
      next: (establishments) => {
        this.establishments.set(establishments);
        if (this.selectedEstablishment) {
          const stillExists = establishments.find((item) => item.id === this.selectedEstablishment?.id) ?? null;
          this.selectedEstablishment = stillExists;
          this.establishmentSelected.emit(stillExists);
        }
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los establecimientos.'));
      },
    });
  }

  selectEstablishment(establishment: Establishment): void {
    this.selectedEstablishment = establishment;
    this.establishmentSelected.emit(establishment);
  }

  openCreateDialog(): void {
    if (!this.canWrite || !this.selectedCompany) {
      return;
    }

    this.editingEstablishment = null;
    this.dialogVisible = true;
  }

  openEditDialog(establishment: Establishment): void {
    if (!this.canWrite || !this.selectedCompany) {
      return;
    }

    this.editingEstablishment = establishment;
    this.dialogVisible = true;
  }

  onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible = visible;
    if (!visible) {
      this.editingEstablishment = null;
    }
  }

  submitDialog(event: EstablishmentDialogSubmit): void {
    if (!this.canWrite || !this.selectedCompany) {
      return;
    }

    if (event.mode === 'create') {
      this.establishmentService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Establecimiento creado.' });
          this.dialogVisible = false;
          this.loadEstablishments();
        },
        error: (error: HttpErrorResponse) => {
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
      return;
    }

    this.establishmentService.update(event.id, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Establecimiento actualizado.' });
        this.dialogVisible = false;
        this.loadEstablishments();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
      },
    });
  }

  confirmDelete(establishment: Establishment): void {
    if (!this.canWrite) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar establecimiento',
      message: `¿Deseas eliminar el establecimiento "${establishment.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => {
        this.establishmentService.delete(establishment.id).subscribe({
          next: () => {
            if (this.selectedEstablishment?.id === establishment.id) {
              this.selectedEstablishment = null;
              this.establishmentSelected.emit(null);
            }
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Establecimiento eliminado.' });
            this.loadEstablishments();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
          },
        });
      },
    });
  }

  isSelected(establishment: Establishment): boolean {
    return this.selectedEstablishment?.id === establishment.id;
  }

}
