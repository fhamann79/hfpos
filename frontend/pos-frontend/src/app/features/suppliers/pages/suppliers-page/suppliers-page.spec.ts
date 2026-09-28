import { TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { SupplierService } from '../../services/supplier.service';
import { SuppliersPage } from './suppliers-page';

describe('SuppliersPage pagination requests', () => {
  it('ignores an older response that arrives after the latest request', () => {
    const firstRequest = new Subject<any>();
    const secondRequest = new Subject<any>();
    const getPage = vi.fn()
      .mockReturnValueOnce(firstRequest)
      .mockReturnValueOnce(secondRequest);

    TestBed.configureTestingModule({
      providers: [
        FormBuilder,
        MessageService,
        ConfirmationService,
        { provide: SupplierService, useValue: { getPage } },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
      ],
    });

    const page = TestBed.runInInjectionContext(() => new SuppliersPage());
    page.ngOnInit();
    page.search = 'nuevo';
    page.loadSuppliers(2, 15);

    secondRequest.next({
      items: [supplier(2, 'Nuevo proveedor')],
      page: 2,
      pageSize: 15,
      totalItems: 20,
      totalPages: 2,
    });
    firstRequest.next({
      items: [supplier(1, 'Proveedor obsoleto')],
      page: 1,
      pageSize: 15,
      totalItems: 20,
      totalPages: 2,
    });

    expect(page.suppliers().map((item) => item.name)).toEqual(['Nuevo proveedor']);
    expect(page.currentPage()).toBe(2);
    expect(page.first).toBe(15);
    expect(page.loading()).toBe(false);
  });

  it('propagates identification sorting to the server query', () => {
    const getPage = vi.fn().mockReturnValue(of({
      items: [],
      page: 1,
      pageSize: 15,
      totalItems: 0,
      totalPages: 0,
    }));

    TestBed.configureTestingModule({
      providers: [
        FormBuilder,
        MessageService,
        ConfirmationService,
        { provide: SupplierService, useValue: { getPage } },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
      ],
    });

    const page = TestBed.runInInjectionContext(() => new SuppliersPage());
    page.onSuppliersLazyLoad({ first: 0, rows: 15, sortField: 'identification', sortOrder: 1 });

    expect(getPage).toHaveBeenCalledWith(expect.objectContaining({
      sortBy: 'identification',
      sortDir: 'asc',
    }));
  });
});

function supplier(id: number, name: string) {
  return {
    id,
    name,
    identification: null,
    email: null,
    phone: null,
    address: null,
    notes: null,
    isActive: true,
    createdAt: '2026-09-28T12:00:00Z',
    updatedAt: null,
  };
}
