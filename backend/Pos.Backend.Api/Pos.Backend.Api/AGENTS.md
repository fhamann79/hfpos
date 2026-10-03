# HFPOS backend agent policy

La politica raiz `AGENTS.md` es normativa; estas reglas no la debilitan.

## Arquitectura actual

Backend .NET / EF Core / PostgreSQL, organizado dentro de `Pos.Backend.Api/`:

```text
Core/           Entities, DTOs, Enums, Models, Security, Services, Interfaces
Infrastructure/ Data, Services, Repositories, Assets
WebApi/         Controllers, Filters, Middleware
Configuration/  Opciones y validacion de configuracion
HealthChecks/   Liveness/readiness
Migrations/     Migraciones EF y snapshot
Program.cs      Composicion y registro de dependencias
```

Las pruebas estan en el proyecto hermano `Pos.Backend.Api.Tests/`.

- Core contiene modelo, contratos y reglas/servicios de negocio cuando corresponde.
- Infrastructure contiene EF, persistencia y servicios concretos de infraestructura; queries/helpers de lectura siguen los patrones existentes aqui.
- WebApi expone HTTP. Existen controllers actuales con `PosDbContext` directo para CRUD/admin acotado; esto describe main, no una licencia para agregar logica compleja a controllers.
- Para codigo nuevo, preferir controllers delgados y contratos de Core con servicios apropiados. Logica de negocio no trivial, transacciones, locks e idempotencia deben permanecer fuera del controller.
- No exigir un refactor incidental para ajustar codigo existente a una descripcion historica. Cambios arquitectonicos materiales requieren alcance/aprobacion explicitos.

## Invariantes

- API stateless y autenticacion JWT. Conservar validaciones de autorizacion/session version existentes; stateless no significa omitir revocacion.
- Contexto tenant autoritativo: `CompanyId` y contexto de establecimiento/punto de emision cuando corresponda; no confiar en IDs arbitrarios del cliente.
- Autorizacion server-side y limites plataforma/tenant intactos. No modificar auth/RBAC desde tickets ajenos.
- Mantener aislamiento, consistencia transaccional, locking e idempotencia; evaluar SQL, N+1 y lecturas no acotadas.
- Persistencia/modelo EF en Infrastructure y Migrations, segun patrones existentes. No introducir dependencias indebidas en Core.
- No crear migraciones sin necesidad de schema; mantener coherencia modelo/migraciones cuando aplique.

## Convenciones y errores

- Controllers `XxxController` y DTOs siguiendo nombres/contratos locales existentes.
- Mantener respuestas HTTP y codigos de error actuales; no inventar un envelope universal ni ocultar errores.
- Preferir manejo centralizado cuando exista; usar 400/401/403/404/409/500 segun contrato.
- Leer implementacion y pruebas relevantes antes de editar; mantener scope y agregar regresiones cuando cambien logica o contratos.

## Validacion y seguridad de persistencia

Desde la raiz del monorepo, CI utiliza:

```bash
dotnet restore backend/Pos.Backend.Api/Pos.Backend.Api.sln
dotnet build backend/Pos.Backend.Api/Pos.Backend.Api.sln --configuration Release --no-restore
dotnet test backend/Pos.Backend.Api/Pos.Backend.Api.sln --configuration Release --no-build
```

Seleccionar pruebas adicionales por riesgo; usar PostgreSQL efimero con datos sinteticos cuando importen semanticas de persistencia. Verificar migraciones/modelo solo cuando corresponda y con configuracion de prueba que no acceda a UserSecrets ni DB persistentes.

`dotnet ef database update` NO es un comando rutinario de validacion. Los agentes solo pueden usarlo en DB desechables creadas para pruebas/CI dentro del alcance autorizado. Nunca contra la DB persistente de Fernando, shared o production. Una operacion real requiere runbook y autorizacion humana especifica R4; los agentes no la ejecutan autonomamente, ni aunque reciban un generico "continua".

No usar datos reales, endpoints SRI reales, certificados, claves o secretos reales. Limpiar solo recursos efimeros propios cuando sea seguro, respetando dependencias entre frentes.
