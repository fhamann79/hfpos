import { TestBed } from '@angular/core/testing';
import { providePrimeNG } from 'primeng/config';
import { of } from 'rxjs';
import { RolePermission } from '../../models/role-permission.model';
import { Role } from '../../models/role.model';
import { RoleService } from '../../services/role.service';
import { RolePermissionsDialog } from './role-permissions-dialog';

const permissions: RolePermission[] = [
  { permissionId: 1, code: 'ADMIN_ROLES_READ', description: 'Read roles', assigned: false },
  { permissionId: 2, code: 'ADMIN_ROLES_WRITE', description: 'Write roles', assigned: false },
  { permissionId: 3, code: 'ADMIN_USERS_READ', description: 'Read users', assigned: true },
];

const adminRole: Role = { id: 8, code: 'ADMIN', name: 'Admin', isActive: true, permissionsCount: 1 };
const otherRole: Role = { id: 9, code: 'CASHIER', name: 'Cashier', isActive: true, permissionsCount: 1 };

describe('RolePermissionsDialog ADMIN recoverability', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [RolePermissionsDialog],
    providers: [
      providePrimeNG({ unstyled: false }),
      { provide: RoleService, useValue: { getPermissions: () => of(permissions.map((item) => ({ ...item }))) } },
    ],
  }));

  it('keeps both ADMIN role permissions checked, disabled, and in the submitted set', async () => {
    const fixture = TestBed.createComponent(RolePermissionsDialog);
    fixture.componentRef.setInput('role', adminRole);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('ADMIN debe conservar');
    expect((element.querySelector('#perm-1') as HTMLInputElement).disabled).toBe(true);
    expect((element.querySelector('#perm-2') as HTMLInputElement).disabled).toBe(true);
    expect((element.querySelector('#perm-1') as HTMLInputElement).checked).toBe(true);
    expect((element.querySelector('#perm-2') as HTMLInputElement).checked).toBe(true);
    for (const id of [1, 2]) {
      const checkbox = element.querySelector(`#perm-${id}`)?.closest('p-checkbox');
      expect(checkbox?.closest('.permission-item--required')).not.toBeNull();
      expect(checkbox?.classList.contains('p-checkbox-checked')).toBe(true);
      expect(checkbox?.classList.contains('p-disabled')).toBe(true);
      const box = checkbox?.querySelector('.p-checkbox-box');
      const icon = box?.querySelector('.p-checkbox-icon');
      expect(box).not.toBeNull();
      expect(icon).not.toBeNull();
    }
    component.togglePermission(1, false);
    const emitted: number[][] = [];
    component.submitForm.subscribe((event) => emitted.push(event.permissionIds));
    component.save();
    expect(emitted[0]).toEqual([1, 2, 3]);
  });

  it('allows another role to toggle the same permission codes', async () => {
    const fixture = TestBed.createComponent(RolePermissionsDialog);
    fixture.componentRef.setInput('role', otherRole);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect((element.querySelector('#perm-1') as HTMLInputElement).disabled).toBe(false);
    expect((element.querySelector('#perm-2') as HTMLInputElement).disabled).toBe(false);
    expect(element.querySelector('#perm-1')?.closest('.permission-item--required')).toBeNull();
    expect(element.querySelector('#perm-2')?.closest('.permission-item--required')).toBeNull();
    const component = fixture.componentInstance;
    component.togglePermission(1, true);
    component.togglePermission(2, true);
    component.togglePermission(3, false);
    const emitted: number[][] = [];
    component.submitForm.subscribe((event) => emitted.push(event.permissionIds));
    component.save();
    expect(emitted[0]).toEqual([1, 2]);
  });
});
