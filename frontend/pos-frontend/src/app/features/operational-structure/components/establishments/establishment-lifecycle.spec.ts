import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { EstablishmentService } from '../../services/establishment.service';
import { EstablishmentsTable } from './establishments-table';
import { EstablishmentDialog, EstablishmentDialogSubmit } from './establishment-dialog';


describe('Establishment lifecycle', () => {
  const item = { id: 1, companyId: 7, name: 'Sucursal histórica', isActive: true };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EstablishmentsTable, EstablishmentDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), providePrimeNG({ unstyled: true }),
        ConfirmationService, MessageService],
    });

  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('renders Desactivar for active records and Activar for inactive records, without delete', async () => {
    vi.spyOn(TestBed.inject(EstablishmentService), 'getAll').mockReturnValue(of([item, { ...item, id: 2, isActive: false }]));
    const fixture = TestBed.createComponent(EstablishmentsTable);
    fixture.componentRef.setInput('canWrite', true);
    fixture.componentRef.setInput('selectedCompany', { id: 7, name: 'Empresa', timeZoneId: 'America/Guayaquil', isActive: true });
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const rows = element.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Desactivar');
    expect(rows[1].textContent).toContain('Activar');
    expect(element.querySelector('.pi-trash')).toBeNull();
    expect(TestBed.inject(EstablishmentService)).not.toHaveProperty('delete');
  });

  it('confirms lifecycle, posts explicit endpoints and refreshes the table', () => {
    const service = TestBed.inject(EstablishmentService);
    const reload = vi.spyOn(service, 'getAll').mockReturnValue(of([item]));
    const fixture = TestBed.createComponent(EstablishmentsTable);
    fixture.componentRef.setInput('canWrite', true);
    fixture.componentRef.setInput('selectedCompany', { id: 7, name: 'Empresa', timeZoneId: 'America/Guayaquil', isActive: true });
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    const requests = TestBed.inject(HttpTestingController);
    for (const active of [true, false]) {
      fixture.componentInstance.confirmLifecycle({ ...item, isActive: active });
      expect(confirm.mock.lastCall?.[0].acceptLabel).toBe(active ? 'Desactivar' : 'Activar');
      confirm.mock.lastCall?.[0].accept?.();
      const request = requests.expectOne((req) => req.url.endsWith(`/api/Establishments/1/${active ? 'deactivate' : 'activate'}`));
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({});
      request.flush(null);
    }
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('does not offer lifecycle mutations without write permission', () => {
    const fixture = TestBed.createComponent(EstablishmentsTable);
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    fixture.componentInstance.confirmLifecycle(item);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('keeps activation out of the edit form and emitted HTTP payload', () => {
    const dialog = TestBed.runInInjectionContext(() => new EstablishmentDialog());
    dialog.establishment = item;

    dialog.form.patchValue({ name: item.name });
    const events: EstablishmentDialogSubmit[] = [];
    dialog.submitForm.subscribe((event) => events.push(event));
    dialog.save();
    expect(events).toHaveLength(1);
    expect(events[0].mode).toBe('edit');
    expect(events[0].payload).not.toHaveProperty('isActive');
    expect(dialog.form.controls).not.toHaveProperty('isActive');
    const payload = { ...item };
    TestBed.inject(EstablishmentService).update(item.id, payload).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'PUT');
    expect(request.request.body).not.toHaveProperty('isActive');
    request.flush(null);
  });

  it('reports a failed lifecycle change and preserves the loaded records', () => {
    const fixture = TestBed.createComponent(EstablishmentsTable);
    fixture.componentRef.setInput('canWrite', true);
    fixture.componentRef.setInput('selectedCompany', { id: 7, name: 'Empresa', timeZoneId: 'America/Guayaquil', isActive: true });
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    const toast = vi.spyOn(TestBed.inject(MessageService), 'add');
    fixture.componentInstance.confirmLifecycle(item);
    confirm.mock.lastCall?.[0].accept?.();
    TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'POST')
      .flush({ error: 'ESTABLISHMENT_HAS_STOCK' }, { status: 409, statusText: 'Conflict' });
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }));
  });
});
