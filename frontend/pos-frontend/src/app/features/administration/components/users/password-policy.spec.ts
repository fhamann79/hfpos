import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { EmissionPointService } from '../../../operational-structure/services/emission-point.service';
import { EstablishmentService } from '../../../operational-structure/services/establishment.service';
import { RoleService } from '../../services/role.service';
import { UserDialog } from './user-dialog';
import { ChangePasswordDialog } from './change-password-dialog';

describe('New administrative password policy', () => {
  const user = { id: 2, username: 'synthetic', email: 'user@test.invalid', roleId: 3,
    roleCode: 'ADMIN', roleName: 'Admin', companyId: 7, establishmentId: 4, emissionPointId: 5, isActive: true };
  beforeEach(() => TestBed.configureTestingModule({ providers: [
    { provide: RoleService, useValue: { getAll: () => of([]) } },
    { provide: EstablishmentService, useValue: { getAll: () => of([]) } },
    { provide: EmissionPointService, useValue: { getAll: () => of([]) } },
  ] }));

  it.each([[11, false], [12, true], [256, true], [257, false]] as const)
    ('create password length %s validity %s', (length, valid) => {
      const component = TestBed.runInInjectionContext(() => new UserDialog());
      component.form.setValue({ username: user.username, email: user.email, password: 'a'.repeat(length),
        roleId: 3, establishmentId: 4, emissionPointId: 5, isActive: true });
      const events: unknown[] = []; component.submitForm.subscribe(event => events.push(event));
      component.save();
      expect(component.form.controls.password.valid).toBe(valid); expect(events.length).toBe(valid ? 1 : 0);
    });

  it.each([[11, false], [12, true], [256, true], [257, false]] as const)
    ('password change length %s validity %s', (length, valid) => {
      const component = TestBed.runInInjectionContext(() => new ChangePasswordDialog()); component.user = user;
      component.form.setValue({ newPassword: 'a'.repeat(length), confirmPassword: 'a'.repeat(length) });
      const events: unknown[] = []; component.submitForm.subscribe(event => events.push(event)); component.save();
      expect(component.form.controls.newPassword.valid).toBe(valid); expect(events.length).toBe(valid ? 1 : 0);
    });

  it('clears both dialogs when the parent hides them after a successful operation', () => {
    const create = TestBed.runInInjectionContext(() => new UserDialog());
    create.form.controls.password.setValue('synthetic-new-password');
    create.ngOnChanges({ visible: new SimpleChange(true, false, false) });
    expect(create.form.controls.password.value).toBe('');
    const change = TestBed.runInInjectionContext(() => new ChangePasswordDialog());
    change.form.setValue({ newPassword: 'synthetic-new-password', confirmPassword: 'synthetic-new-password' });
    change.ngOnChanges({ visible: new SimpleChange(true, false, false) });
    expect(change.form.getRawValue()).toEqual({ newPassword: '', confirmPassword: '' });
  });

  it('does not trim or persist new passwords, and clears on cancellation', () => {
    const component = TestBed.runInInjectionContext(() => new ChangePasswordDialog()); component.user = user;
    const value = ' synthetic-password ';
    component.form.setValue({ newPassword: value, confirmPassword: value });
    let submitted = ''; component.submitForm.subscribe(event => submitted = event.payload.newPassword);
    component.save(); expect(submitted).toBe(value); component.hide();
    expect(component.form.controls.newPassword.value).toBe('');
  });
});
