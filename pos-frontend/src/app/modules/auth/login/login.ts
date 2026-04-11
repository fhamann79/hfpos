import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/services/auth';
import { AuthStore } from '../../../core/stores/auth.store';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ButtonModule,
    CardModule,
    InputTextModule,
    PasswordModule,
    MessageModule,
  ],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login implements OnInit {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);

  readonly loading = signal(false);
  readonly errorMessage = signal('');

  readonly loginForm = this.fb.nonNullable.group({
    username: ['', [Validators.required]],
    password: ['', [Validators.required]],
  });

  constructor(
    private authService: AuthService,
    private store: AuthStore,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.route.queryParamMap.subscribe((params) => {
      if (params.get('message') === 'session-expired') {
        this.errorMessage.set('Tu sesión expiró. Inicia sesión nuevamente.');
      }
    });
  }

  submitLogin() {
    this.errorMessage.set('');

    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    const { username, password } = this.loginForm.getRawValue();
    this.loading.set(true);

    this.authService
      .login(username.trim(), password)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
      next: (res) => {
        this.store.setToken(res.token);
        this.store.loadMe().subscribe({
          next: (me) => {
            if (!me) {
              this.errorMessage.set('Error inesperado');
              return;
            }

            this.router.navigate(['/dashboard']);
          },
        });
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.errorMessage.set('Credenciales inválidas');
          return;
        }

        if (error.status === 0) {
          this.errorMessage.set('No se puede conectar con el servidor');
          return;
        }

        this.errorMessage.set('Error inesperado');
      },
    });
  }

  clearError() {
    if (this.errorMessage()) {
      this.errorMessage.set('');
    }
  }
}
