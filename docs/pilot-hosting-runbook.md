# DEV-531: preparacion RC y piloto cohost

PREP NO-R4, no piloto activo. Cadena aprobada en issue #133,
comentario 6005809322: PR145 -> esta continuacion. Base
`d9939e1c4f2d62dfc071f68b344d4cc33ec099ef`. PR145 fue aceptado sobre
`9e2111d53f59ccab4d2703f2442788261ff927c0` y esta integrado; CI de main d993
verde. Esa aceptacion NO se extiende a release/PGops de esta continuacion.
Coordinator + reviewer independiente determinan el nuevo timing/gate humano.
NO MERGE, tag, publicacion GHCR, compra, deployment ni R4 por el implementer.

## Decisiones y limites

Fernando aprobo preparar Contabo Linux US East, Docker Compose cohost
web/reverse HTTPS + backend + PostgreSQL16 y backups fuera del VPS.
Dominio desconocido: `<PILOT_DOMAIN>`, nunca inventar DNS/TLS/cuenta.
Techo USD10-15/mes excluye dominio; cotizacion final, plazo, impuestos y costes
de backup/egress pendientes ANTES de contratar. STOP si supera USD15 o requiere
compromiso no aprobado. Core4 es candidato tentativo, no garantia de capacidad.
Medicion real de RAM/CPU/IO/disco/restore bajo carga es gate R4, no SLA inferido.

Evidencia publica entregada por coordinador, 05-Oct-2026 (no checkout/compra):

- [Contabo US](https://contabo.com/en-us/locations/united-states/): CoreVPS4
  4vCPU/8GB/100GB SSD USD6.58/mo; CoreVPS6 6vCPU/12GB/200GB USD9.20/mo,
  anunciados con plazo24meses. Ese plazo NO esta aprobado. La
  [pagina VPS](https://contabo.com/en-us/vps/) muestra otras cifras EUR, no una
  cotizacion mensual US East. Setup, impuestos/pais, prepago y renovacion sin verificar.
- [Location fee](https://help.contabo.com/en/support/solutions/articles/103000269774/)
  varia por servicio/region; [Contabo Ago2026](https://contabo.com/blog/contabo-eu-data-centers-latency-gdpr/)
  identifica US East/New York y fee, no su precio final. Plus4 USD16.85 fuera del
  techo, no candidato. Paginas object storage conflictivas/stale NO son cotizacion.
- Alternativa offsite NO contratada/aprobada: [Backblaze B2](https://www.backblaze.com/cloud-storage/pricing),
  USD6.95/TB/mo, primeros10GB gratis, egreso gratis3x almacenamiento promedio,
  luego USD0.01/GB; ClassD USD0.004/10k llamadas, primeras2500/dia gratis.
  Inferencia ilustrativa: 100GB promedio TOTAL retenido -> 90GB facturables ->
  USD0.6255/mo; 6.58+0.6255 ~=7.21. NO garantiza volumen, moneda/impuestos,
  US East, factura mensual ni presupuesto. Confirmar cargos/plazo antes de compra.

## Packaging limitado

Version RC aprobada: `v0.1.0-rc.1`, todavia sin tag/publicacion. Gate permite SOLO
`vX.Y.Z` o `vX.Y.Z-rc.N`, componentes sin leading zero, N>=1; no alpha/build metadata.
Workflow conserva fetch de main + merge-base ancestry INLINE en job contents:read,
ANTES del helper del checkout. Incluso un helper de feature que sale0 no habilita
publish. Helper comprueba semver/tag/SHA/main; publish separado packages:write
depende de exito, solo push tag. PR/main solo prueban, nunca publican/despliegan.
Mantiene tres imagenes, tags version y sha, labels, SBOM/provenance declarados.

Un actor del repo puede editar tambien el workflow: estos gates no prueban
inmutabilidad frente a ese actor. Revisar permisos de tags/branch protection y
workflow exacto antes del tag humano. No nuevo privilegio, bypass ni firma inferida.
Tras publicacion autorizada, agentes/CI verifican readonly los tres digests,
labels/SHA, SBOM/provenance efectivos y audit exacto; declarar capacidad no acredita
artefactos publicados. Conservar findings/audits historicos de [readiness](release-pilot-readiness.md).

## Configuracion cohost opcional

Base `compose.production.example.yml` sigue permitiendo PostgreSQL externo.
Se conserva EXACTAMENTE main d993, junto con `.env.production.example` y fixture
`start-deployment-smoke.ps1`. Baseline LEGACY comparte conexion app/migrator: NO
representa el target piloto ni prueba roles seguros. No se cambia ese contrato.
Target externo = BASE + `compose.production.migrator.example.yml`, sin PG y con
conexion migrator propia obligatoria. Target cohost = BASE + overlay PG siguiente.
Overlay `compose.production.postgres.example.yml` agrega PG16 sin puerto host,
red database internal SOLO postgres/backend/migrations; web no entra a esa red.
Persistencia bind absoluta exigida, create_host_path:false; crear/verificar ruta
exacta y ownership en R4 antes del primer arranque. No volumen anonimo ni path typo.
Digest PG16 obligatorio y revisado, no latest. Validacion de tag/digest sintactica
NO acredita contenido/arquitectura/patch de una imagen: inspeccion/audit de digest
exacto sigue siendo gate antes de uso.

Copiar contrato BASE y el NUEVO `.env.production.postgres.example` (cohost) o
`.env.production.migrator.example` (externo)
FUERA Git. Configurar hosts/dominio, digests, keyring, TLS y conexiones externas.
Password bootstrap mediante archivo externo ACL exclusivo, montado como secret;
connection strings runtime/migrator fuera Git/argumentos/logs. Compose config JSON
contiene secretos: nunca imprimir/adjuntar. Validar en memoria y reportar solo gates:

```sh
# Solo validacion; ninguna creacion de servicios/DB ni migracion.
python scripts/ci/validate_pilot_compose.py /secure/pilot.env
# Target externo: BASE + optional MIGRATOR, sin servicio PG.
python scripts/ci/validate_pilot_compose.py --external /secure/pilot-external.env
```

El helper comprueba topologia, pin, storage, secreto externo y conexiones distintas,
NO certifica privilegios efectivos, hostname/CA, existencia de rutas ni readiness.
Conexiones distintas no prueban por si solas usuarios ni privilegios distintos.
Los tests prueban las tres composiciones: baseline legacy inalterado, externalpilot
con guard missing-migrator, cohost con guard PG/migrator. No role proof de baseline.
No `compose up` del template desde agentes. Sin init scripts de producto, seed,
bootstrap platform ni migracion automatica. `migrations` sigue profile opt-in,
aprobacion NO, restart:no; es one-shot exclusivamente autorizado con conexion propia.
El entrypoint oficial PG inicializa el CLUSTER vacio/admin/DB, no schema del producto.
`POSTGRES_USER` es superuser y JAMAS debe ser la identidad app.
[Contrato oficial PG](https://hub.docker.com/_/postgres).

## Roles y grants humanos (R4)

Crear/separar tres identidades: bootstrap/admin, migrator propietario de schema/DB
sin superuser, runtime LOGIN NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOREPLICATION.
Runtime no es propietario ni miembro de admin/migrator. Revocar CREATE publico;
runtime recibe CONNECT DB, USAGE schema, SELECT/INSERT/UPDATE/DELETE tablas y USAGE
secuencias necesarias. Configurar default privileges PARA el rol que crea objetos
(migrator), y verificar grants existentes despues de cada migracion.
Conexion `HFPOS_DATABASE_CONNECTION` usa runtime; `HFPOS_MIGRATION_DATABASE_CONNECTION`
usa migrator, no app ni bootstrap. Mantener admin secret solo en postgres, no backend.

Antes de trafico: consultar current_user, pg_roles (rolsuper/rolcreatedb/rolcreaterole/
rolreplication), ownership/membresias y grants con conexion runtime; demostrar DML
permitido en fixture autorizada y DDL/privileged role denegados. No registrar claves.
Estas son pruebas R4 futuras, no resultado de los tests config sinteticos.
Backup custom --no-owner/--no-privileges NO conserva ownership/grants: recrear roles,
restaurar bajo propietario/migrator aprobado, reaplicar grants/default privileges y
comprobar runtime nuevamente ANTES de arrancar destino. No arrancar app como admin
para compensar permisos faltantes. Un migrator no-superuser debe tener privilegios
reales suficientes para DDL/restore; preflight humano, no escalacion automatica.

Futura DB externa: sustituir overlay cohost por overlay MIGRATOR, cambiar conexiones/redes/CA mediante
config/overlay, NO codigo app. Exigir hostname real y `SSL Mode=VerifyFull`, CA
aprobada montada read-only y `Root Certificate` acorde; no Trust Server Certificate
ni sslmode disable para externa. Runtime/migrator distintos y grants recreados.
[Npgsql TLS](https://www.npgsql.org/doc/security.html).

## Lifecycle, cutover y rollback

Solo HTTPS publico; firewall host/cloud parametrizado segun dominio y mecanismo TLS
aprobados. Backend/PG sin host ports. Docker habilitado tras reboot; overlay reinicia
postgres/backend/web unless-stopped, NO migrator. Health no repara automaticamente
un proceso unhealthy: alertar y diagnosticar, no prometer restart de health.
Logs json-file bounded10m x3 por servicio: propuesta tecnica para limitar disco,
no retencion legal aprobada. Monitorizar disco/inodos, RAM/OOM, CPU/IO y reinicios.
No backups/keyring en capas de imagen ni logs, no Docker socket en app.

TLS externo requiere issuance/renovacion humana automatizada fuera app y alertas
antes de expiracion. Validar hostname/cadena/fecha, permisos UIDweb101, reemplazo
atomico y reload/recreate controlado; luego probe HTTPS. No certificado synthetic
en piloto real. Dominio/issuer/metodo/ventana y ruta alerta siguen pendientes.

Cutover: detener todos los escritores/worker/replicas/tareas origen, inventariar
leases/jobs/conexiones, comprobar quiescence y mantener origen/keyring intactos.
Restore SIEMPRE a NUEVA DB vacia y NUEVO keyring/directorio, destinos nombrados
inequivocamente diferentes a origen; mismo ApplicationName/purposes.
Deny-egress Linux ANTES de arrancar: [procedimiento DR](disaster-recovery.md#deny-egress-linux-docker).
ONE writer activo; probar login/protected value/negocio, entonces gate humano
para trafico/integraciones. Jobs pendientes pueden reclamar localmente aunque no
tengan egress: inventariar mutaciones, no afirmar destino intacto.

Rollback a imagen/config anteriores solo si schema compatible y sin escrituras
nuevas incompatibles. Con nuevas escrituras, no apuntar ciegamente a backup viejo:
quiesce, comparar divergence, evaluar perdida/reconciliacion y preferir forward fix.
Todo restore real requiere autorizacion humana separada; nunca EF Down automatico.

## Offsite y gates restantes

Conjunto cifrado, schedule/retention y pull-back verify:
[DR offsite](disaster-recovery.md#offsite-cifrado-del-cohost).
Propuestas para discutir, NO aprobadas: RPO24h para backup diario consistente;
RTO4h medido de incidente a app restaurada/validada. No son adecuados por defecto
para ventas reales: Fernando debe aceptar perdida/ventana o exigir otra estrategia.
Retencion propuesta7daily/4weekly/3monthly, sujeta a volumen/coste y aprobacion.

Owners, frecuencia final, retencion, RPO/RTO, contactos/ruta/ack/escalacion de alertas,
custodia/recovery de claves, hostname/TLS, presupuesto/contratacion y R4: PENDIENTES.
Seleccion de provider/RC no implica aprobacion de esos gates ni piloto activo.
