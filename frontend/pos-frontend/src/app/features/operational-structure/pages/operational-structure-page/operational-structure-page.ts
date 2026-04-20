import { Component, computed, signal, inject } from '@angular/core';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { MessageModule } from 'primeng/message';
import { ToastModule } from 'primeng/toast';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { CompaniesTable } from '../../components/companies/companies-table';
import { EmissionPointsTable } from '../../components/emission-points/emission-points-table';
import { EstablishmentsTable } from '../../components/establishments/establishments-table';
import { Company } from '../../models/company.model';
import { Establishment } from '../../models/establishment.model';

@Component({
  selector: 'app-operational-structure-page',
  standalone: true,
  imports: [
    ToastModule,
    ConfirmDialogModule,
    MessageModule,
    CompaniesTable,
    EstablishmentsTable,
    EmissionPointsTable,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './operational-structure-page.html',
  styleUrl: './operational-structure-page.scss',
})
export class OperationalStructurePage {
  private readonly permissionService = inject(PermissionService);

  readonly canRead = computed(() => this.permissionService.hasPermission(PERMISSIONS.operationalStructureRead));
  readonly canWrite = computed(() => this.permissionService.hasPermission(PERMISSIONS.operationalStructureWrite));

  readonly selectedCompany = signal<Company | null>(null);
  readonly selectedEstablishment = signal<Establishment | null>(null);

  handleCompanySelected(company: Company | null): void {
    this.selectedCompany.set(company);
    this.selectedEstablishment.set(null);
  }

  handleEstablishmentSelected(establishment: Establishment | null): void {
    this.selectedEstablishment.set(establishment);
  }
}
