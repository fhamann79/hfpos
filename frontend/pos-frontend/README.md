# POS Frontend - Angular

Frontend del sistema POS + Inventario + Facturacion electronica (Ecuador).

## Requisitos

- Node.js LTS
- npm (el repo usa npm 11.x)
- Angular CLI 21 (npm install -g @angular/cli)

## Instalacion

Desde la carpeta del proyecto:

```bash
npm install
```

## Ejecutar

Levantar servidor de desarrollo:

```bash
ng serve
```

Abrir en el navegador:

```
http://localhost:4200
```

## Scripts

```bash
ng build
ng test
```

## Configuracion de backend URL

- La URL base del backend esta definida en src/app/core/services/auth.ts.
- Variable: private api = 'https://localhost:7096/api/auth';
- Si se migra a environment, documentar la nueva ubicacion.

## Flujo de autenticacion (breve)

1. Login: el componente de login ejecuta AuthService.login('admin', '1234') y guarda el token en localStorage.
2. Contexto: luego se llama a /api/auth/me mediante AuthStore.loadMe().
3. Interceptor: agrega Authorization: Bearer <token> a cada request.
4. Guard: AuthGuard protege rutas verificando el token.
