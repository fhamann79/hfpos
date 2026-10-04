import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { providePrimeNG } from 'primeng/config';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { EmissionPointService } from '../../../operational-structure/services/emission-point.service';
import { EstablishmentService } from '../../../operational-structure/services/establishment.service';
import { RoleService } from '../../services/role.service';
import { UserService } from '../../services/user.service';
import { UserDialog, UserDialogSubmit } from './user-dialog';
import { EmissionPoint } from '../../../operational-structure/models/emission-point.model';

describe('UserDialog tenant contracts', () => {
  const establishments = { getAll: vi.fn(() => of([])) };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [UserDialog], providers: [
      provideHttpClient(), provideHttpClientTesting(), providePrimeNG({ unstyled: true }),
      { provide: RoleService, useValue: { getAll: () => of([]) } },
      { provide: EstablishmentService, useValue: establishments },
      { provide: EmissionPointService, useValue: { getAll: () => of([]) } },
    ] });
  });

  afterEach(() => { TestBed.inject(HttpTestingController).verify(); vi.clearAllMocks(); });

  it('renders no company selector and loads establishments without a tenant argument', async () => {
    const fixture = TestBed.createComponent(UserDialog);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[formControlName="establishmentId"]')).not.toBeNull();
    expect(element.querySelector('[formControlName="companyId"]')).toBeNull();
    expect(fixture.componentInstance.form.controls).not.toHaveProperty('companyId');
    expect(establishments.getAll).toHaveBeenCalledWith();
  });

  it('emits create and edit payloads without companyId', () => {
    const component = TestBed.runInInjectionContext(() => new UserDialog());
    const events: UserDialogSubmit[] = [];
    component.submitForm.subscribe((event) => events.push(event));
    component.form.patchValue({ username: 'operator', email: 'operator@example.test', password: 'synthetic-unit-only',
      roleId: 3, establishmentId: 4, emissionPointId: 5 });
    component.save();
    component.user = { id: 2, username: 'operator', email: 'operator@example.test', roleId: 3,
      roleCode: 'CASHIER', roleName: 'Caja', companyId: 7, establishmentId: 4, emissionPointId: 5, isActive: true };
    component.save();
    expect(events.map((event) => event.mode)).toEqual(['create', 'edit']);
    for (const event of events) expect(event.payload).not.toHaveProperty('companyId');
  });

  it('keeps only the latest context points and labels codes alongside names', () => {
    const old = new Subject<EmissionPoint[]>();
    const current = new Subject<EmissionPoint[]>();
    TestBed.overrideProvider(EmissionPointService, { useValue: { getAll: (id: number) => id === 4 ? old : current } });
    const component = TestBed.runInInjectionContext(() => new UserDialog());
    component.onEstablishmentChange(4);
    component.onEstablishmentChange(8);
    const point = { id: 9, establishmentId: 8, code: '009', name: 'Caja', isActive: true };
    current.next([point]);
    old.next([{ ...point, id: 5, establishmentId: 4 }]);
    expect(component.emissionPoints()).toEqual([point]);
    expect(component.emissionPointOptions()[0].label).toBe('009 - Caja');
    component.establishments.set([{ id: 8, companyId: 1, code: '017', name: 'Local', address: 'Synthetic 123', isActive: true }]);
    expect(component.establishmentOptions()[0].label).toBe('017 - Local');
    component.hide();
    current.next([]);
    expect(component.emissionPoints()).toEqual([point]);
  });

  it('whitelists create and update HTTP bodies even when an object has extra tenant fields', () => {
    const service = TestBed.inject(UserService);
    const payload = { username: 'operator', email: 'operator@example.test', password: 'unit-only',
      roleId: 3, establishmentId: 4, emissionPointId: 5, companyId: 999, isActive: true };
    service.create(payload).subscribe();
    const create = TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'POST');
    expect(create.request.body).not.toHaveProperty('companyId');
    create.flush({});
    service.update(2, payload).subscribe();
    const update = TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'PUT');
    expect(update.request.body).not.toHaveProperty('companyId');
    expect(update.request.body).not.toHaveProperty('password');
    update.flush(null);
  });
});
