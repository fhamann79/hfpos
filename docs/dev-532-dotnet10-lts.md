# DEV-532: transicion del backend a .NET 10 LTS

Refs #134. Base: `c5103a8b255abf9c06fd15814677334a9a51ab3f`.
Riesgo R3. Esta propuesta no publica una release, no autoriza operaciones R4
y no cambia las imagenes, configuracion ni runbooks fijados para RC6.
La transicion elimina el bloqueo de soporte del runtime para v1.0, sujeto a
validacion; .NET 8 termina soporte el 10-nov-2026 y .NET 10 el 14-nov-2028
segun la [politica oficial](https://dotnet.microsoft.com/en-us/platform/support/policy).

## Versiones y limites

| Superficie | Base | Candidato |
| --- | --- | --- |
| API, pruebas y host sintetico | net8.0 | net10.0 |
| SDK en CI/build/migrations | 8.0.x / Bookworm | 10.0.403 / Ubuntu 24.04 Noble |
| ASP.NET en backend/host sintetico | 8.0 / Bookworm | 10.0.14 / Noble |
| JwtBearer | 8.0.4 | 10.0.14 |
| IdentityModel Tokens JWT | 8.15.0 | 8.19.2, requerido por JwtBearer |
| MVC Testing | 8.0.25 | 10.0.14 |
| EF Core / Design / Relational / dotnet-ef | 8.0.4 | 10.0.14 |
| Npgsql EF y driver resuelto | 8.0.x | 10.0.3 |
| Caching.Memory, Crypto.Xml y Text.Json | referencias 8.0.x | framework compartido ASP.NET 10 |
| Crypto.Pkcs | transitivo | pin 10.0.14, evita el 10.0.0 vulnerable de MimeKit |
| Probe de restauracion de RC6 | net8.0 | net8.0 deliberado |

Se quitan las referencias redundantes de pruebas a Http y Regex. Sin cambios
de Angular, PostgreSQL, endpoints, DTO, politicas JWT, persistencia de claves
ni trust configurado. Microsoft [no publica imagenes Debian para .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu).
Los manifiestos MCR fijados en Dockerfiles incluyen linux/amd64 y linux/arm64.
Se mantienen UID/GID 1654:1654, /keys, HTTP 8080 y el healthcheck existente.
Noble cambia bibliotecas nativas y CA del sistema: verificar firma XAdES,
TLS/proxy y trust sintetico sin instalar raices en la imagen de produccion.
El [binder de .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/configuration-null-values-preserved)
conserva null explicitos. La prueba de CORS sin origen configurado usa la lista
vacia de produccion, y prueba por separado el rechazo de un origen null.
No cambia la politica CORS.

El probe no referencia la API del repositorio: se compila contra referencias
net8.0. Localmente se verifico con el SDK aislado 10.0.403; Containers instala
ese SDK y registra la version efectivamente seleccionada antes de publicar.
El helper ejecuta solo su DLL sellada con el runtimeconfig
de la API en la imagen RC6 exacta. No se usa roll-forward, ni se repinan RC6,
su identidad o el helper. El E2E existente prueba RC6 sobre una restauracion
sintetica originada por el smoke del candidato: HTTP, keyring, worker en solo
lectura y ausencia de payload. Ese ensayo de restauracion cubre candidato ->
RC6. Para la direccion RC6 -> candidato, Containers tambien ejecuta una prueba
focal de Data Protection: el runtime 8 del digest RC6 emite un payload y keyring
sinteticos y el runtime 10 del candidato lo descifra con el mismo ApplicationName
y purpose, sin red ni base de datos y con el keyring de solo lectura en el lector.
Esto no prueba un restore completo de DB RC6 -> candidato. Una futura
recuperacion operativa del candidato requiere su propia evidencia.

## EF y PostgreSQL

No se modifican migraciones, snapshot, modelo ni SQL aplicativo. EF 9 introdujo
el bloqueo ante cambios pendientes, el lock de migraciones y cambios en los
limites transaccionales; conservar el job de migracion desde cero y los tests
de upgrades historicos, sin suprimir avisos. EF 10 cambia nombres y expansion
de parametros; el proveedor PostgreSQL cambia `arrayColumn.Contains` a
`ANY` cuando no hay un GIN modelado. Revisar planes si aparece regresion en
consultas con colecciones; no activar mapeos JSON, columnas virtuales ni
features de PG18. La PK GUID de recuperacion se asigna explicitamente con
`Guid.NewGuid()`, no depende del nuevo generador UUIDv7.
El driver renueva conexiones fisicas por defecto tras una hora, prefiere GSS
si esta disponible y cambia nombres de metricas/tags Npgsql; comprobar
conectividad, TLS y cualquier consumidor externo antes de rollout.
Fuentes: [EF 9](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes),
[EF 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes),
[proveedor Npgsql 10](https://www.npgsql.org/efcore/release-notes/10.0.html),
[driver Npgsql 9](https://www.npgsql.org/doc/release-notes/9.0.html) y
[driver Npgsql 10](https://www.npgsql.org/doc/release-notes/10.0.html).

Validacion offline: EF 10 no detecto cambios pendientes. El diff de los scripts
idempotentes completos EF 8 vs EF 10 solo contiene lineas en blanco y
`ProductVersion` de las filas de historial; no cambia DDL/DML aplicativo ni
IDs historicos. El aviso del sentinel de `Sale.Status` tambien aparece en la
base EF 8. No se aplico SQL ni se genero una migracion.

## Rollout y rollback propuestos (no ejecutados)

1. Exigir CI exacto y review independiente, y ensayar en un entorno aislado
   con PostgreSQL 16 y material sintetico. Registrar SHA, digests de backend,
   web y migrations, SBOM/provenance, configuracion no secreta, historial EF y
   copia coherente de base, keyring y configuracion RC6. Verificar su restore.
2. Recorrido humano R3 pre-merge, tras CI verde: en el entorno aislado, iniciar
   sesion, registrar una compra y una venta con caja, comprobar stock y totales,
   y abrir el XML/PDF de la emision simulada. Aceptacion de Fernando: pendiente.
   El equipo valida TLS, aislamiento, idempotencia y compatibilidad del keyring
   en CI; Fernando no tiene que generar ni restaurar claves.
3. Para un futuro cambio real, un operador humano debe obtener autorizacion
   R4 separada, acordar ventana y quiescer escrituras/worker. Comparar
   `__EFMigrationsHistory` y readiness previa: este ticket no requiere DDL
   nuevo; si falta historial o difiere el esquema, detener el cambio. Cambiar
   al digest aprobado, conservando PostgreSQL, roles, mounts, UID, trust,
   keyring y configuracion. No arrancar dos workers contra la misma base.
4. Ante fallos de readiness, permisos, firma o integridad, detener el candidato
   y preservar evidencia. Revertir imagenes/config al conjunto previo solo
   si DB, keyring y colas siguen compatibles; no revertir DDL ni restaurar sobre
   escrituras posteriores por defecto. Si hubo escrituras o cambios externos,
   reconciliar operaciones/documentos antes de cualquier restore. Si se
   requiere restaurar, usar el backup coherente y un runbook humano separado.
   RC6 es solo un punto de referencia tecnico: con .NET 8 fuera de soporte,
   no autorizar su reactivacion como solucion ordinaria; mantener pausa segura
   o volver al ultimo artefacto soportado y verificado.
