import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TagModule } from 'primeng/tag';
import {
  FISCAL_SETTINGS_ACCESS_REQUIREMENT,
  INVENTORY_ACCESS_REQUIREMENT,
  POS_ACCESS_REQUIREMENT,
  PURCHASES_ACCESS_REQUIREMENT,
  SALES_REPORTS_ACCESS_REQUIREMENT,
} from '../../core/constants/feature-access';
import { PermissionRequirement } from '../../core/constants/feature-access';
import { PermissionService } from '../../core/services/permission.service';
import { resolveHttpErrorMessage } from '../../core/utils/http-error-normalizer';
import { DashboardAlert, DashboardLowStockProduct, DashboardSummary } from './dashboard.model';
import { DashboardService } from './dashboard.service';

interface QuickAction extends PermissionRequirement {
  label: string;
  description: string;
  icon: string;
  route: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, ButtonModule, TagModule, MessageModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  private readonly permissionService = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly summary = signal<DashboardSummary | null>(null);
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly accessWarning = signal('');

  readonly fiscalAlertCount = computed(() => this.summary()?.alerts.filter((alert) => alert.category === 'Fiscal').length ?? 0);
  readonly inventoryAlertCount = computed(() => this.summary()?.alerts.filter((alert) => alert.category === 'Inventario').length ?? 0);

  readonly quickActions = computed<QuickAction[]>(() =>
    [
      {
        label: 'Ir a POS',
        description: 'Abrir caja y registrar ventas.',
        icon: 'pi pi-shopping-cart',
        route: '/pos',
        ...POS_ACCESS_REQUIREMENT,
      },
      {
        label: 'Reporte de ventas',
        description: 'Consultar ventas y exportar CSV.',
        icon: 'pi pi-chart-bar',
        route: '/sales-reports',
        ...SALES_REPORTS_ACCESS_REQUIREMENT,
      },
      {
        label: 'Inventario',
        description: 'Revisar stock y movimientos.',
        icon: 'pi pi-warehouse',
        route: '/inventory',
        ...INVENTORY_ACCESS_REQUIREMENT,
      },
      {
        label: 'Compras',
        description: 'Registrar y revisar recepciones.',
        icon: 'pi pi-shopping-bag',
        route: '/purchase-receipts',
        ...PURCHASES_ACCESS_REQUIREMENT,
      },
      {
        label: 'Configuracion fiscal',
        description: 'SRI, certificado y email empresarial.',
        icon: 'pi pi-receipt',
        route: '/fiscal-settings',
        ...FISCAL_SETTINGS_ACCESS_REQUIREMENT,
      },
    ].filter((action) => this.permissionService.canAccess(action))
  );

  ngOnInit(): void {
    this.route.queryParamMap.subscribe((params) => {
      const message = params.get('message');
      this.accessWarning.set(this.resolveAccessWarning(message));
    });

    this.loadSummary();
  }

  loadSummary(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.dashboardService.getSummary().subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.summary.set(null);
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudo cargar el dashboard operativo.'));
      },
    });
  }

  goTo(route: string): void {
    this.router.navigateByUrl(route);
  }

  dayBarWidth(total: number): string {
    const days = this.summary()?.salesLastSevenDays.days ?? [];
    const max = Math.max(...days.map((day) => day.totalSold), 0);

    if (max <= 0 || total <= 0) {
      return '0%';
    }

    return `${Math.max((total / max) * 100, 6)}%`;
  }

  profitBarWidth(grossProfit: number): string {
    const days = this.summary()?.salesLastSevenDays.days ?? [];
    const max = Math.max(...days.map((day) => Math.abs(day.grossProfit)), 0);

    if (max <= 0 || grossProfit === 0) {
      return '0%';
    }

    return `${Math.max((Math.abs(grossProfit) / max) * 100, 6)}%`;
  }

  purchaseBarWidth(netPurchased: number): string {
    const days = this.summary()?.purchasesLastSevenDays.days ?? [];
    const max = Math.max(...days.map((day) => Math.abs(day.netPurchased)), 0);

    if (max <= 0 || netPurchased === 0) {
      return '0%';
    }

    return `${Math.max((Math.abs(netPurchased) / max) * 100, 6)}%`;
  }

  marginLabel(value: number | null | undefined): string {
    return `${(value ?? 0).toLocaleString('es-EC', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })}%`;
  }

  stockSeverity(product: DashboardLowStockProduct): 'danger' | 'warn' | 'success' {
    if (product.quantity <= 0) {
      return 'danger';
    }

    return product.minimumStock > 0 && product.quantity <= product.minimumStock ? 'warn' : 'success';
  }

  fiscalStatusLabel(summary: DashboardSummary): string {
    if (summary.fiscal.certificateExpired || !summary.fiscal.certificateConfigured) {
      return 'Requiere atencion';
    }

    if (summary.fiscal.certificateExpiringSoon || !summary.fiscal.emailLastTestSucceeded) {
      return 'Con alertas';
    }

    return 'Operativo';
  }

  fiscalStatusSeverity(summary: DashboardSummary): 'danger' | 'warn' | 'success' {
    if (summary.fiscal.certificateExpired || !summary.fiscal.certificateConfigured) {
      return 'danger';
    }

    if (summary.fiscal.certificateExpiringSoon || !summary.fiscal.emailLastTestSucceeded) {
      return 'warn';
    }

    return 'success';
  }

  alertIcon(alert: DashboardAlert): string {
    switch (alert.severity) {
      case 'danger':
        return 'pi pi-exclamation-triangle';
      case 'warn':
        return 'pi pi-exclamation-circle';
      case 'info':
        return 'pi pi-info-circle';
      default:
        return 'pi pi-check-circle';
    }
  }

  private resolveAccessWarning(message: string | null): string {
    return message === 'catalog-denied'
      ? 'No tienes permisos para acceder a Catalogo.'
      : message === 'pos-denied'
        ? 'No tienes permisos para acceder a POS.'
        : message === 'sales-reports-denied'
          ? 'No tienes permisos para acceder al reporte de ventas.'
          : message === 'inventory-denied'
            ? 'No tienes permisos para acceder a Inventario.'
            : message === 'purchase-receipts-denied'
              ? 'No tienes permisos para acceder a Compras.'
              : message === 'fiscal-settings-denied'
                ? 'No tienes permisos para acceder a Configuracion Fiscal.'
                : message === 'administration-denied'
                  ? 'No tienes permisos para acceder a Administracion.'
                  : message === 'operational-structure-denied'
                    ? 'No tienes permisos para acceder a Estructura Operativa.'
                    : message === 'session-expired'
                      ? 'Tu sesion expiro. Inicia sesion nuevamente.'
                      : '';
  }
}
