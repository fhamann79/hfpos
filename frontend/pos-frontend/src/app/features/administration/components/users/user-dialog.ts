import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { EmissionPoint } from '../../../operational-structure/models/emission-point.model';
import { Establishment } from '../../../operational-structure/models/establishment.model';
import { EmissionPointService } from '../../../operational-structure/services/emission-point.service';
import { EstablishmentService } from '../../../operational-structure/services/establishment.service';
import { Role } from '../../models/role.model';
import { CreateUserRequest, UpdateUserRequest, User } from '../../models/user.model';
import { RoleService } from '../../services/role.service';

export type UserDialogSubmit =
  | { mode: 'create'; payload: CreateUserRequest }
  | { mode: 'edit'; id: number; payload: UpdateUserRequest };

@Component({
  selector: 'app-user-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    DialogModule,
    InputTextModule,
    SelectModule,
    CheckboxModule,
    ButtonModule,
  ],
  templateUrl: './user-dialog.html',
  styleUrl: './user-dialog.scss',
})
export class UserDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly roleService = inject(RoleService);
  private readonly establishmentService = inject(EstablishmentService);
  private readonly emissionPointService = inject(EmissionPointService);

  @Input({ required: true }) visible = false;
  @Input() user: User | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<UserDialogSubmit>();

  readonly roles = signal<Role[]>([]);
  readonly establishments = signal<Establishment[]>([]);
  readonly emissionPoints = signal<EmissionPoint[]>([]);

  readonly form = this.fb.nonNullable.group({
    username: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    password: [''],
    roleId: [0, [Validators.required, Validators.min(1)]],
    establishmentId: [0, [Validators.required, Validators.min(1)]],
    emissionPointId: [0, [Validators.required, Validators.min(1)]],
    isActive: [true],
  });

  get isEditMode(): boolean {
    return !!this.user;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] && this.visible) {
      this.loadInitialCatalogs();
      this.syncForm();
    }

    if (changes['user'] && this.visible) {
      this.syncForm();
    }
  }

  hide(): void {
    this.visibleChange.emit(false);
  }

  onEstablishmentChange(establishmentId: number): void {
    this.form.patchValue({ emissionPointId: 0 });
    this.emissionPoints.set([]);

    if (establishmentId > 0) {
      this.loadEmissionPoints(establishmentId);
    }
  }

  save(): void {
    if (!this.isEditMode) {
      this.form.controls.password.setValidators([Validators.required, Validators.minLength(6)]);
    } else {
      this.form.controls.password.clearValidators();
    }

    this.form.controls.password.updateValueAndValidity({ emitEvent: false });

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();

    if (this.user) {
      this.submitForm.emit({
        mode: 'edit',
        id: this.user.id,
        payload: {
          email: values.email.trim(),
          roleId: values.roleId,
          establishmentId: values.establishmentId,
          emissionPointId: values.emissionPointId,
          isActive: values.isActive,
        },
      });
      return;
    }

    this.submitForm.emit({
      mode: 'create',
      payload: {
        username: values.username.trim(),
        email: values.email.trim(),
        password: values.password.trim(),
        roleId: values.roleId,
        establishmentId: values.establishmentId,
        emissionPointId: values.emissionPointId,
      },
    });
  }

  private loadInitialCatalogs(): void {
    this.roleService.getAll().subscribe({ next: (roles) => this.roles.set(roles.filter((role) => role.isActive)), error: () => this.roles.set([]) });
  }

  private loadEstablishments(): void {
    this.establishmentService.getAll().subscribe({
      next: (establishments) => this.establishments.set(establishments.filter((item) => item.isActive)),
      error: () => this.establishments.set([]),
    });
  }

  private loadEmissionPoints(establishmentId: number): void {
    this.emissionPointService.getAll(establishmentId).subscribe({
      next: (emissionPoints) => this.emissionPoints.set(emissionPoints.filter((item) => item.isActive)),
      error: () => this.emissionPoints.set([]),
    });
  }

  private syncForm(): void {
    if (!this.visible) {
      return;
    }

    if (this.user) {
      this.form.reset({
        username: this.user.username,
        email: this.user.email,
        password: '',
        roleId: this.user.roleId,
        establishmentId: this.user.establishmentId,
        emissionPointId: this.user.emissionPointId,
        isActive: this.user.isActive,
      });
      this.loadEstablishments();
      this.loadEmissionPoints(this.user.establishmentId);
      return;
    }

    this.form.reset({
      username: '',
      email: '',
      password: '',
      roleId: 0,
      establishmentId: 0,
      emissionPointId: 0,
      isActive: true,
    });
    this.loadEstablishments();
    this.emissionPoints.set([]);
  }
}
