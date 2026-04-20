import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { MessageModule } from 'primeng/message';
import { TagModule } from 'primeng/tag';
import { AuthStore } from '../../core/stores/auth.store';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, CardModule, ButtonModule, TagModule, MessageModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  private store = inject(AuthStore);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  me = this.store.me;
  loaded = this.store.loaded;
  readonly accessWarning = signal('');

  ngOnInit() {
    this.route.queryParamMap.subscribe((params) => {
      const message = params.get('message');
      this.accessWarning.set(
        message === 'catalog-denied'
          ? 'No tienes permisos para acceder a Catálogo.'
          : message === 'pos-denied'
            ? 'No tienes permisos para acceder a POS.'
            : message === 'administration-denied'
              ? 'No tienes permisos para acceder a Administración.'
              : message === 'operational-structure-denied'
                ? 'No tienes permisos para acceder a Estructura Operativa.'
                : message === 'session-expired'
                  ? 'Tu sesión expiró. Inicia sesión nuevamente.'
                  : ''
      );
    });
  }

  logout() {
    this.store.clear();
    this.router.navigateByUrl('/login');
  }
}
