import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-recover-access', standalone: true, imports: [RouterLink],
  template: `<main class="login-page"><section class="access-panel">
    <img class="brand-logo" src="/hf-one-logo.svg" alt="HF One" width="200" height="56" />
    <h1>Recuperar acceso</h1><p class="access-subtitle">Recuperaci\u00f3n asistida del piloto interno</p>
    <p>Contacta a un operador autorizado para verificar tu identidad por un canal previamente acreditado.</p>
    <p>Recibir\u00e1s un enlace temporal de un solo uso, v\u00e1lido por 15 minutos. Con ese enlace crear\u00e1s personalmente tu nueva contrase\u00f1a.</p>
    <p>No enviamos correos autom\u00e1ticos en este piloto.</p>
    <a routerLink="/login">Volver a iniciar sesi\u00f3n</a>
  </section></main>`, styleUrl: '../login/login.scss',
})
export class RecoverAccess {}
