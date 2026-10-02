# BE-FE-520: despliegue de referencia

Artefactos y ensayos sinteticos: R3. Desplegar, migrar o restaurar un entorno real:
R4, exclusivamente un operador humano autorizado. Este runbook no autoriza ejecucion.
Requiere aprobacion explicita, entorno confirmado, ventana de cambio, backups
verificados y plan de rollback/restore. No hay migracion nueva en BE-FE-520.

## Topologia single-host

TLS externo -> web non-root (8443) -> Angular estatico + `/api/*`, `/health/*`
al backend non-root HTTP 8080 -> PostgreSQL externo autorizado.
El backend no publica puerto al host. La red privada usa subnet/IP del proxy
explicitas; no significa que Docker provea aislamiento de un operador del host.
Compose Production NO crea PostgreSQL, migra, inicializa usuarios ni despliega solo.
`migrations` tiene profile separado; `compose up` normal no lo ejecuta.

Imágenes: backend, web, migrations. Bases fijadas por digest verificado; actualizar
estos pins mediante un ticket/revision, no asumir que un pin permanece seguro.
Node 24 compila Angular 21 con `npm ci`; runtime nginx unprivileged, no `ng serve`.
.NET SDK solo en build/migrations; backend runtime ASP.NET 8, sin herramientas EF.
Imagen de migracion incluye fuentes necesarias pero no User Secrets, certificados,
dumps ni claves. `.dockerignore` excluye artefactos locales y el directorio no
versionado `frontend/pos-frontend/backend/`.

Produccion usa imagenes `vX.Y.Z` + digest confirmado, o `sha-<commit>` + digest.
Registrar tres digests, SHA, lista EF, configuracion no secreta y ventana por release.
El workflow publica GHCR con SBOM/provenance al crear un tag humano; NO despliega,
no tiene credenciales cloud/SSH/Production y no publica desde PRs.

## Pre-deploy humano

1. Confirmar destino, aprobacion R4, tag/commit/digests, CI verde y compatibilidad de schema.
2. Revisar `dotnet ef migrations list` usando artefacto de esa release. Nunca EF Down automatico.
3. Backup PostgreSQL + verify + snapshot keyring; almacenamiento cifrado/ACL restrictiva.
4. Revisar `production-configuration.md`: secretos runtime en secret store, hosts,
   issuer/audience, keyring existente RW con ApplicationName estable, bootstrap deshabilitado.
5. Preparar keyring montado con propietario UID/GID 1654, permisos solo servicio;
   TLS externo `certificate.pem` / `private.key` legible exclusivamente por web UID 101.
   No usar certificados smoke en Production. No incluir secrets en ARG/layers/labels.
6. Confirmar subnet no solapada, IP del proxy confiable y TLS. En cloud/Kubernetes
   adaptar allowlist a topologia real; nunca wildcard/trust-all ni automatic forwarded headers.
7. Confirmar estado SRI: gated por default; cualquier habilitacion real es decision humana separada.
8. Confirmar rollback/restore, controlar escrituras/trafico antes de migrar.

## Comandos de referencia (NO ejecucion autonoma)

Usar copia externa de `.env.production.example`, completar valores en infraestructura
autorizada. `compose config` completo muestra secretos: no imprimir ni adjuntar;
usar `config --quiet`. Variables criticas vacias rechazan arranque del ejemplo.

```sh
docker compose --env-file /secure/.env.production -f deploy/compose/compose.production.example.yml config --quiet
# SOLO despues de aprobacion, backup/keyring verificados y trafico controlado:
HFPOS_MIGRATION_APPROVED=YES docker compose --env-file /secure/.env.production -f deploy/compose/compose.production.example.yml --profile migration run --rm migrations
docker compose --env-file /secure/.env.production -f deploy/compose/compose.production.example.yml up -d backend
# Verificar readiness, luego habilitar/switch web y smoke:
docker compose --env-file /secure/.env.production -f deploy/compose/compose.production.example.yml up -d web
```

`apply-migrations.sh` rechaza falta de aprobacion antes de conectar; muestra solo
ambiente/release/lista EF y suprime salida potencialmente sensible de EF ante error.
Investigar fallos en entorno seguro; no reenviar logs crudos. Nunca se ejecuta desde
backend ni automaticamente en compose Production. Migracion CI usa solo DB efimera.

Filesystem read-only, cap_drop ALL, no-new-privileges, tmpfs /tmp y keyring persistente;
sin socket Docker, privileged, host network/PID/IPC. Runtime puede salir a DB/OTLP/SRI
segun politica de red del hosting, no incluidos como health dependencies adicionales.
SIGTERM: readiness deja de aceptar, requests disponen de 30 s (5..120 configurable);
gracia compose 40 s. Si cambia timeout, aumentar stop_grace_period por encima del limite.

## Proxy y cache

El edge directo TLS SETEA Host, XFF=remote_addr, XFP=https; ignora XFP/XFF arbitrarios
del navegador. Authorization solo viaja al backend interno API. No confiar otro LB
delante sin adaptar y validar topologia. HSTS API lo controla backend (30 dias,
sin preload/subdomains); edge no impone una politica conflictiva. Static agrega
nosniff/DENY/no-referrer, sin CSP nueva agresiva.
`index.html` no-cache/must-revalidate; assets con hash immutable; assets inexistentes
404; SPA deep links fallback index; API/health nunca fallback ni cache. Login conserva
no-store backend. Health publico HTTPS; HTTP interno solo bypass exacto live/ready,
ningun otro endpoint evade HTTPS. API no enruta a terceros.

## Post-deploy

Comprobar live/ready, logs correlacionados/release, pipeline OTLP si habilitado,
login platform/tenant humano, operaciones focalizadas, 5xx/latencia y reinicios.
Solo entonces habilitar trafico. No automatizar operaciones comerciales reales/SRI.

## Rollback

A: falla app, schema compatible -> anterior imagen/digest y config compatible.
B: migracion aplicada, anterior app incompatible -> NO rollback ciego; preferir
forward fix; restore autorizado solo tras evaluar perdida de escrituras.
C: corrupcion/perdida DB -> `disaster-recovery.md`, NUEVA DB vacia, autorizacion R4.
Nunca automatizar `database update <old migration>`/EF Down ni borrar datos.

## Smoke humano simple (solo sintetico)

Docker Desktop funcionando. En PowerShell:

```powershell
git switch codex/be-fe-520-deployment-observability-dr
git pull --ff-only
pwsh -File scripts/ops/start-deployment-smoke.ps1
```

El script crea TLS localhost de un dia, JWT/passwords aleatorios, DB/keyring/backup
efimeros; build/migracion aprobada SOLO smoke; imprime MIGRATION PASS, BACKEND READY,
WEB PASS, API PROXY PASS, BACKUP VERIFY PASS, RESTORE PASS y KEYRING PASS.
Abrir `https://localhost:8444/platform/login`, aceptar SOLO ese certificado sintetico,
usuario `smoke-platform`, password generado en archivo ignorado
`deploy/compose/smoke-runtime/smoke.env` (`SMOKE_PLATFORM_PASSWORD`). Ver lista tenants.
No requiere User Secrets/DB/certificados Development. Si falla, limpia automaticamente.
CI usa `-Cleanup`, no conserva credenciales ni publica dumps/keys como artifacts.

```powershell
pwsh -File scripts/ops/stop-deployment-smoke.ps1
```

Limpia solamente proyecto `hfpos-520-smoke`, volumenes/red y runtime temporal.
No borra otros contenedores ni backups externos. Si cleanup Docker falla, conserva
runtime para reintento, nunca declara limpieza exitosa. Requiere puertos 8084/8444
y subnet 172.30.53.0/24 disponibles. No es plantilla ejecutable para Production.
