import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToolbarModule } from 'primeng/toolbar';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { Establishment } from '../../models/establishment.model';
import { EmissionPoint } from '../../models/emission-point.model';
import { EmissionPointService } from '../../services/emission-point.service';
import { EmissionPointDialog, EmissionPointDialogSubmit } from './emission-point-dialog';

@Component({
  selector: 'app-emission-points-table',
  standalone: true,
  imports: [CommonModule, TableModule, ButtonModule, ToolbarModule, TagModule, MessageModule, EmissionPointDialog],
  templateUrl: './emission-points-table.html',
  styleUrl: './emission-points-table.scss',
})
export class EmissionPointsTable implements OnChanges {
  @Input() canWrite = false;
  @Input() selectedEstablishment: Establishment | null = null;

  private readonly emissionPointService = inject(EmissionPointService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly emissionPoints = signal<EmissionPoint[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  dialogVisible = false;
  editingEmissionPoint: EmissionPoint | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['selectedEstablishment']) {
      this.loadEmissionPoints();
    }
  }

  loadEmissionPoints(): void {
    if (!this.selectedEstablishment) {
      this.emissionPoints.set([]);
      this.errorMessage.set('');
      return;
    }

    this.loading.set(true);
    this.errorMessage.set('');

    this.emissionPointService.getAll(this.selectedEstablishment.id).subscribe({
      next: (emissionPoints) => {
        this.emissionPoints.set(emissionPoints);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los puntos de emisión.'));
      },
    });
  }

  openCreateDialog(): void {
    if (!this.canWrite || !this.selectedEstablishment) {
      return;
    }

    this.editingEmissionPoint = null;
    this.dialogVisible = true;
  }

  openEditDialog(emissionPoint: EmissionPoint): void {
    if (!this.canWrite || !this.selectedEstablishment) {
      return;
    }

    this.editingEmissionPoint = emissionPoint;
    this.dialogVisible = true;
  }

  onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible = visible;
    if (!visible) {
      this.editingEmissionPoint = null;
    }
  }

  submitDialog(event: EmissionPointDialogSubmit): void {
    if (!this.canWrite || !this.selectedEstablishment) {
      return;
    }

    if (event.mode === 'create') {
      this.emissionPointService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Punto de emisión creado.' });
          this.dialogVisible = false;
          this.loadEmissionPoints();
        },
        error: (error: HttpErrorResponse) => {
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
      return;
    }

    this.emissionPointService.update(event.id, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Punto de emisión actualizado.' });
        this.dialogVisible = false;
        this.loadEmissionPoints();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
      },
    });
  }

  confirmDelete(emissionPoint: EmissionPoint): void {
    if (!this.canWrite) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar punto de emisión',
      message: `¿Deseas eliminar el punto "${emissionPoint.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => {
        this.emissionPointService.delete(emissionPoint.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Punto de emisión eliminado.' });
            this.loadEmissionPoints();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
          },
        });
      },
    });
  }

}
