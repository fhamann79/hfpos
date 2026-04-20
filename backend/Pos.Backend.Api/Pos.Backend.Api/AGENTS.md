# AGENTS.md

Este archivo define las **reglas, estándares y flujos de trabajo** para que Codex (y cualquier colaborador) pueda trabajar correctamente en el backend del sistema POS.

---

## Alcance del repositorio

Este backend implementa un sistema POS multiempresa con inventario, ventas y facturación. La arquitectura real está organizada por capas:

- **Core**: entidades, DTOs, contratos y servicios de negocio.
- **Infrastructure**: persistencia con Entity Framework Core, DbContext, repositorios y migraciones.
- **WebApi**: controllers delgados sin lógica de negocio.

---

## Arquitectura real del repositorio

```
Pos.Backend.Api/
 ├─ Core/
 │  ├─ Entities/
 │  ├─ DTOs/
 │  ├─ Interfaces/
 │  └─ Services/
 ├─ Infrastructure/
 │  ├─ Data/
 │  └─ Repositories/
 ├─ Migrations/
 ├─ WebApi/
 │  └─ Controllers/
 ├─ Program.cs
 └─ Pos.Backend.Api.csproj
```

---

## Principios obligatorios

- **API stateless**: no almacenar estado de sesión en el servidor.
- **Controllers sin lógica de negocio**: los controllers solo orquestan y delegan en servicios.
- **Toda lógica vive en Core/Services**.
- **Persistencia solo en Infrastructure** (DbContext, repositorios, EF Core).
- **JWT** como mecanismo de autenticación.
- **Multiempresa obligatoria**: toda operación debe considerar `CompanyId` y `EstablishmentId`.
- **Separación estricta de capas**: no cruzar dependencias indebidas.
- **Tests cuando aplique**: cambios en lógica o contratos requieren pruebas.

---

## Convenciones de código

- **Controllers**: `XxxController` con endpoints REST claros (GET/POST/PUT/DELETE).
- **DTOs**: `XxxDto`, `CreateXxxDto`, `UpdateXxxDto`.
- **Respuestas consistentes** (si aplica):

```json
{ "success": true, "data": {}, "message": "" }
```

---

## Manejo de errores

- Evitar excepciones genéricas.
- Preferir manejo centralizado de errores.
- Usar códigos HTTP claros: 400, 401, 403, 404, 409, 500.

---

## Comandos comunes

```bash
dotnet restore
dotnet build
dotnet test
dotnet ef migrations add Nombre
dotnet ef database update
```

---

## Cómo debe trabajar Codex en este repo

1. **Leer este AGENTS.md antes de cambiar cualquier archivo**.
2. Validar la estructura real del proyecto y respetar las capas.
3. **No mover lógica al controller**: todo lo de negocio vive en `Core/Services`.
4. **No tocar Infrastructure desde WebApi** excepto por interfaces expuestas en Core.
5. Mantener el backend **stateless** y con autenticación JWT.
6. Respetar la multiempresa (`CompanyId`, `EstablishmentId`) en cualquier flujo.
7. Si un cambio afecta lógica, **agregar tests cuando aplique**.
8. Explicar los cambios con claridad y sin asumir comportamientos no confirmados.

---

## Objetivo del proyecto

Construir un **POS sólido, mantenible y escalable**, priorizando:

- Claridad
- Seguridad
- Facilidad de evolución
- Aprendizaje continuo del equipo

Este archivo es obligatorio y debe respetarse en cada cambio.
