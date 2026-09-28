import { TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { CustomerService } from '../../services/customer.service';
import { CustomersPage } from './customers-page';

describe('CustomersPage pagination requests', () => {
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
        { provide: CustomerService, useValue: { getPage } },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
      ],
    });

    const page = TestBed.runInInjectionContext(() => new CustomersPage());
    page.ngOnInit();
    page.search = 'nuevo';
    page.loadCustomers(2, 15);

    secondRequest.next({
      items: [customer(2, 'Nuevo resultado')],
      page: 2,
      pageSize: 15,
      totalItems: 20,
      totalPages: 2,
    });
    firstRequest.next({
      items: [customer(1, 'Resultado obsoleto')],
      page: 1,
      pageSize: 15,
      totalItems: 20,
      totalPages: 2,
    });

    expect(page.customers().map((item) => item.name)).toEqual(['Nuevo resultado']);
    expect(page.currentPage()).toBe(2);
    expect(page.first).toBe(15);
    expect(page.loading()).toBe(false);
  });
});

function customer(id: number, name: string) {
  return {
    id,
    name,
    identificationType: null,
    identification: null,
    phone: null,
    email: null,
    address: null,
    notes: null,
    isActive: true,
    createdAt: '2026-09-28T12:00:00Z',
    updatedAt: null,
  };
}
