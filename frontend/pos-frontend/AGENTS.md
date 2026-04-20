# AGENTS.md

Reglas y estandares para trabajar en el frontend POS.

## Stack

- Angular (standalone APIs)
- TypeScript (tipado estricto)
- PrimeNG + PrimeIcons
- SCSS
- HttpClient
- RxJS + Signals (cuando aplique)
- JWT (token en localStorage)

## Estructura real del repo

La convencion actual usa modules/. No cambiar la estructura sin ticket explicito.

```
pos-frontend/
  src/app/
    core/
      services/
      guards/
      interceptors/
      models/
      stores/
    modules/
      auth/
      dashboard/
      inventario/
      ventas/
      pos/
      sri/
    shared/
      components/
      directives/
      pipes/
```

## Principios y reglas

- Mantener la convencion actual basada en modules/.
- Componentes delgados: sin logica de negocio pesada ni llamadas HTTP directas.
- La logica vive en core/services o en services del modulo correspondiente.
- Tipado estricto: prohibido usar any.
- Auth JWT:
  - El interceptor agrega Authorization: Bearer <token>.
  - Las rutas protegidas usan AuthGuard.
- Usar PrimeNG y PrimeIcons de forma consistente.
- La URL del backend debe estar centralizada o documentada claramente.
  - Actualmente esta en src/app/core/services/auth.ts (variable api).
  - Si se mueve a environment, documentar la nueva ubicacion.

## Como debe trabajar Codex

- Un PR por ticket.
- No refactor masivo.
- No cambiar estructura de carpetas sin ticket explicito.
- No romper codigo existente.
- Entregar pasos de prueba (ng build, ng serve).

## Comandos de validacion

```bash
npm install
ng serve
ng build
ng test
```
