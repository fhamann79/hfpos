import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { EmissionPointService } from '../../services/emission-point.service';
import { EmissionPointsTable } from './emission-points-table';
import { EmissionPointDialog, EmissionPointDialogSubmit } from './emission-point-dialog';


describe('EmissionPoint lifecycle', () => {
  const item = { id: 1, establishmentId: 3, code: '001', name: 'Punto histórico', isActive: true };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EmissionPointsTable, EmissionPointDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), providePrimeNG({ unstyled: true }),
        ConfirmationService, MessageService],
    });

  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('renders Desactivar for active records and Activar for inactive records, without delete', async () => {
    vi.spyOn(TestBed.inject(EmissionPointService), 'getAll').mockReturnValue(of([item, { ...item, id: 2, isActive: false }]));
    const fixture = TestBed.createComponent(EmissionPointsTable);
    fixture.componentRef.setInput('canWrite', true);
    fixture.componentRef.setInput('selectedEstablishment', { id: 3, companyId: 7, name: 'Sucursal', isActive: true });
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const rows = element.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Desactivar');
    expect(rows[1].textContent).toContain('Activar');
    expect(element.querySelector('.pi-trash')).toBeNull();
    expect(TestBed.inject(EmissionPointService)).not.toHaveProperty('delete');
  });

  it('confirms lifecycle, posts explicit endpoints and refreshes the table', () => {
    const service = TestBed.inject(EmissionPointService);
    const reload = vi.spyOn(service, 'getAll').mockReturnValue(of([item]));
    const fixture = TestBed.createComponent(EmissionPointsTable);
    fixture.componentRef.setInput('canWrite', true);
    fixture.componentRef.setInput('selectedEstablishment', { id: 3, companyId: 7, name: 'Sucursal', isActive: true });
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    const requests = TestBed.inject(HttpTestingController);
    for (const active of [true, false]) {
      fixture.componentInstance.confirmLifecycle({ ...item, isActive: active });
      expect(confirm.mock.lastCall?.[0].acceptLabel).toBe(active ? 'Desactivar' : 'Activar');
      confirm.mock.lastCall?.[0].accept?.();
      const request = requests.expectOne((req) => req.url.endsWith(`/api/EmissionPoints/1/${active ? 'deactivate' : 'activate'}`));
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({});
      request.flush(null);
    }
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('does not offer lifecycle mutations without write permission', () => {
    const fixture = TestBed.createComponent(EmissionPointsTable);
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    fixture.componentInstance.confirmLifecycle(item);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('keeps activation out of the edit form and emitted HTTP payload', () => {
    const dialog = TestBed.runInInjectionContext(() => new EmissionPointDialog());
    dialog.emissionPoint = item;
    dialog.establishmentId = item.establishmentId;
    dialog.form.patchValue({ code: item.code, name: item.name });
    const events: EmissionPointDialogSubmit[] = [];
    dialog.submitForm.subscribe((event) => events.push(event));
    dialog.save();
    expect(events).toHaveLength(1);
    expect(events[0].mode).toBe('edit');
    expect(events[0].payload).not.toHaveProperty('isActive');
    expect(dialog.form.controls).not.toHaveProperty('isActive');
    const payload = { ...item };
    TestBed.inject(EmissionPointService).update(item.id, payload).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'PUT');
    expect(request.request.body).not.toHaveProperty('isActive');
    request.flush(null);
  });

  it('reports a failed lifecycle change and preserves the loaded records', () => {
    const fixture = TestBed.createComponent(EmissionPointsTable);
    fixture.componentRef.setInput('canWrite', true);
    fixture.componentRef.setInput('selectedEstablishment', { id: 3, companyId: 7, name: 'Sucursal', isActive: true });
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    const toast = vi.spyOn(TestBed.inject(MessageService), 'add');
    fixture.componentInstance.confirmLifecycle(item);
    confirm.mock.lastCall?.[0].accept?.();
    TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'POST')
      .flush({ error: 'EMISSION_POINT_HAS_ACTIVE_USERS' }, { status: 409, statusText: 'Conflict' });
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }));
  });
});
