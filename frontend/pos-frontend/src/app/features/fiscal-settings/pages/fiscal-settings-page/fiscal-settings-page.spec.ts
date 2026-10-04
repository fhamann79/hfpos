import { HttpErrorResponse } from '@angular/common/http';
import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { Confirmation, ConfirmationService, MessageService } from 'primeng/api';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { CompanySriSettings } from '../../models/fiscal-settings.model';
import { FiscalSettingsService } from '../../services/fiscal-settings.service';
import { FiscalSettingsPage } from './fiscal-settings-page';

const settings: CompanySriSettings = {
  companyId: 1, environment: 1, emissionType: 1, isEnabled: true,
  automaticProcessingEnabled: false, automaticProcessingRevision: 0,
  certificateConfigured: true, certificateExpiresAt: null, updatedAt: null,
};

describe('FiscalSettingsPage company delegation', () => {
  let page: FiscalSettingsPage;
  let company: WritableSignal<number>;
  let permissions: WritableSignal<Set<string>>;
  let confirmation: Confirmation | undefined;
  let service: Record<string, ReturnType<typeof vi.fn>>;

  beforeEach(() => {
    company = signal(1);
    permissions = signal(new Set([PERMISSIONS.fiscalSettingsWrite, PERMISSIONS.sriDocumentsSign, PERMISSIONS.sriDocumentsSubmit]));
    confirmation = undefined;
    service = {
      getSriSettings: vi.fn(() => of(settings)),
      updateSriSettings: vi.fn(() => of({ ...settings, automaticProcessingEnabled: true, automaticProcessingRevision: 1 })),
      getSriReadiness: vi.fn(() => of(null)),
    };
    TestBed.configureTestingModule({ providers: [
      FormBuilder,
      { provide: FiscalSettingsService, useValue: service },
      { provide: PermissionService, useValue: { hasPermission: (p: string) => permissions().has(p) } },
      { provide: MessageService, useValue: { add: vi.fn() } },
      { provide: ConfirmationService, useValue: { confirm: (value: Confirmation) => { confirmation = value; } } },
      { provide: AuthStore, useValue: { companyId: company, establishmentId: () => 1, emissionPointId: () => 1, token: () => 'synthetic' } },
    ] });
    page = TestBed.runInInjectionContext(() => new FiscalSettingsPage());
    page.loadSriSettings();
  });

  it('defaults off and requires explicit confirmation before saving a delegation', () => {
    expect(page.sriForm.controls.automaticProcessingEnabled.value).toBe(false);
    page.sriForm.controls.automaticProcessingEnabled.setValue(true);
    page.saveSriSettings();
    expect(service['updateSriSettings']).not.toHaveBeenCalled();
    expect(page.sriSettings()?.automaticProcessingEnabled).toBe(false);
    confirmation?.accept?.();
    expect(service['updateSriSettings']).toHaveBeenCalledOnce();
    expect(page.sriSettings()?.automaticProcessingRevision).toBe(1);
  });

  it.each([403, 500])('restores confirmed delegation after HTTP %s instead of displaying optimistic authority', status => {
    service['updateSriSettings'].mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
    page.sriForm.controls.automaticProcessingEnabled.setValue(true);
    page.saveSriSettings(); confirmation?.accept?.();
    expect(page.sriSettings()?.automaticProcessingEnabled).toBe(false);
    expect(page.sriForm.controls.automaticProcessingEnabled.value).toBe(false);
    expect(page.sriSaving()).toBe(false);
  });

  it('prevents cashier and settings-only delegation but allows settings-only revocation', () => {
    permissions.set(new Set([PERMISSIONS.posSalesCreate]));
    page.sriForm.controls.automaticProcessingEnabled.setValue(true);
    page.saveSriSettings();
    expect(confirmation).toBeUndefined();
    permissions.set(new Set([PERMISSIONS.fiscalSettingsWrite]));
    page.saveSriSettings();
    expect(confirmation).toBeUndefined();
    page.sriSettings.set({ ...settings, automaticProcessingEnabled: true });
    page.sriForm.controls.automaticProcessingEnabled.setValue(false);
    service['updateSriSettings'].mockReturnValue(of(settings));
    page.saveSriSettings(); confirmation?.accept?.();
    expect(service['updateSriSettings']).toHaveBeenCalledOnce();
  });

  it('does not save while loading or saving and discards a confirmation from a previous company', () => {
    page.sriForm.controls.automaticProcessingEnabled.setValue(true);
    page.sriLoading.set(true); page.saveSriSettings();
    expect(confirmation).toBeUndefined();
    page.sriLoading.set(false); page.sriSaving.set(true); page.saveSriSettings();
    expect(confirmation).toBeUndefined();
    page.sriSaving.set(false); page.saveSriSettings();
    company.set(2); confirmation?.accept?.();
    expect(service['updateSriSettings']).not.toHaveBeenCalled();
  });

  it('discards late A responses after B and responses after destroy', () => {
    const a = new Subject<CompanySriSettings>(); const b = new Subject<CompanySriSettings>();
    service['getSriSettings'].mockReturnValueOnce(a).mockReturnValueOnce(b);
    page.loadSriSettings(); company.set(2); page.loadSriSettings();
    b.next({ ...settings, companyId: 2 });
    a.next({ ...settings, automaticProcessingEnabled: true });
    expect(page.sriSettings()?.companyId).toBe(2);
    expect(page.sriSettings()?.automaticProcessingEnabled).toBe(false);
    page.ngOnDestroy(); b.next({ ...settings, companyId: 2, automaticProcessingEnabled: true });
    expect(page.sriSettings()?.automaticProcessingEnabled).toBe(false);
  });
});
