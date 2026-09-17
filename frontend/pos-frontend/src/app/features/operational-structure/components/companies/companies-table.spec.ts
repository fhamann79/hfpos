import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { providePrimeNG } from 'primeng/config';
import { MessageService } from 'primeng/api';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { CompanyService } from '../../services/company.service';
import { EstablishmentService } from '../../services/establishment.service';
import { CompaniesTable } from './companies-table';
import { CompanyDialog, CompanyDialogSubmit } from './company-dialog';

describe('Current company tenant boundary', () => {
  const company = { id: 7, name: 'Company A', timeZoneId: 'America/Guayaquil', isActive: true };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [CompaniesTable, CompanyDialog], providers: [
      provideHttpClient(), provideHttpClientTesting(), providePrimeNG({ unstyled: true }), MessageService,
    ] });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); vi.restoreAllMocks(); });

  it('shows current company automatically with no selector, create or delete action', async () => {
    vi.spyOn(TestBed.inject(CompanyService), 'getAll').mockReturnValue(of([company]));
    const fixture = TestBed.createComponent(CompaniesTable);
    fixture.componentRef.setInput('canWrite', true);
    const selected = vi.fn();
    fixture.componentInstance.companySelected.subscribe(selected);
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Empresa actual');
    expect(element.textContent).toContain('Company A');
    expect(element.textContent).not.toContain('Nueva Company');
    expect(element.querySelector('.pi-trash')).toBeNull();
    expect(element.querySelector('p-select')).toBeNull();
    expect(element.querySelector('.pi-pencil')).not.toBeNull();
    expect(selected).toHaveBeenCalledWith(company);
    expect(TestBed.inject(CompanyService)).not.toHaveProperty('create');
    expect(TestBed.inject(CompanyService)).not.toHaveProperty('delete');
  });

  it('only emits permitted company fields, never activation or provisioning', () => {
    const dialog = TestBed.runInInjectionContext(() => new CompanyDialog());
    const events: CompanyDialogSubmit[] = [];
    dialog.submitForm.subscribe((event) => events.push(event));
    dialog.form.patchValue({ name: 'Company A', timeZoneId: 'America/Guayaquil' });
    dialog.save();
    expect(events).toHaveLength(0);
    dialog.company = company;
    dialog.save();
    expect(events).toEqual([{ mode: 'edit', id: 7, payload: { name: 'Company A', timeZoneId: 'America/Guayaquil' } }]);
    expect(dialog.form.controls).not.toHaveProperty('isActive');
  });

  it('does not send isActive when updating company or companyId for establishments', () => {
    const requests = TestBed.inject(HttpTestingController);
    TestBed.inject(CompanyService).update(7, company).subscribe();
    const update = requests.expectOne((req) => req.method === 'PUT');
    expect(update.request.body).toEqual({ name: company.name, timeZoneId: company.timeZoneId });
    update.flush(null);
    const establishments = TestBed.inject(EstablishmentService);
    establishments.getAll().subscribe();
    const list = requests.expectOne((req) => req.url.endsWith('/api/Establishments'));
    expect(list.request.params.has('companyId')).toBe(false);
    list.flush([]);
    const input = { name: 'A2', companyId: 999 };
    establishments.create(input).subscribe();
    const create = requests.expectOne((req) => req.method === 'POST');
    expect(create.request.body).toEqual({ name: 'A2' });
    create.flush({});
  });
});
