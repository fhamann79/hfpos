# HFPOS frontend agent policy

La politica raiz `AGENTS.md` es normativa; estas reglas no la debilitan.

## Stack y estructura actual

Angular standalone, TypeScript estricto, PrimeNG/PrimeIcons, SCSS, HttpClient, RxJS y Signals.

```text
src/app/core/       Servicios, guards, interceptors, modelos, stores y utilidades comunes
src/app/features/   Features funcionales: POS, inventario, compras, administracion, reportes, etc.
src/app/modules/    Auth, dashboard y plataforma
src/app/shared/     Componentes compartidos
src/environments/  Configuracion de build y environment.apiUrl
```

Respetar la ubicacion y convenciones del area real; el frontend no vive exclusivamente en `modules/`. No reorganizar carpetas sin un ticket aprobado.

## Principios

- Componentes delgados, sin logica de negocio pesada ni HTTP directo. Usar servicios del feature/modulo o core segun ownership.
- Tipado estricto: no usar `any`; preservar contratos API, estados loading/error/empty y manejo de respuestas obsoletas cuando corresponda.
- Usar PrimeNG/PrimeIcons y patrones visuales existentes; layouts legibles y responsive, sin refactors cosmeticos fuera de scope.
- Base API mediante `environment.apiUrl`; no hardcodear endpoints alternativos ni introducir secretos en environments/bundles.
- Preservar interceptor JWT, guards, permisos y stores existentes, incluidos limites tenant/plataforma y revocacion de sesion.
- Los guards/botones no sustituyen autorizacion backend. No cambiar auth/permisos incidentalmente.
- La autoridad de dinero, impuestos, stock y estados transaccionales es el backend; no duplicar reglas criticas en UI.

## Trabajo y validacion

- Un ticket aprobado, una rama/worktree aislado y un PR; correcciones en el mismo PR.
- Leer codigo y tests relevantes; cambios de comportamiento/contrato requieren regresiones proporcionales al riesgo.
- No ampliar scope ni convertir un hallazgo ajeno en arreglo oportunista; reportarlo al coordinador.
- Reportar comandos/resultados reales y validacion humana pendiente conforme a la politica raiz.

Desde `frontend/pos-frontend`, los scripts usados por CI son:

```bash
npm ci
npm run build
npm test -- --watch=false
npm audit --omit=dev --audit-level=high
```

Build/test incluyen comprobaciones de seguridad de environments. No confundir audit runtime con avisos de dependencias de desarrollo; reportarlos sin actualizar paquetes fuera del alcance.

`npm start` / `ng serve` es una herramienta opcional de desarrollo, no una barrera automatizada obligatoria. Usar smoke humano conforme al riesgo y al plan aprobado; no levantar API/dev server ni usar credenciales/datos reales solo para completar un check tecnico.
