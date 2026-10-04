# BE-FE-528: emision autonoma y recuperacion

Issue #130, alcance aprobado #127. Base `5455ec32dd60d5fa0ad347a5b25acbbfb73a5903`.
R3. **VALIDACION HUMANA PRE-MERGE REQUERIDA**, aceptacion pendiente.
No merge por el implementer. No R4, datos/certificados reales ni endpoints SRI reales.

## Contrato y operacion

- Una Invoice nueva crea su job PostgreSQL dentro de la transaccion comercial,
  exclusivamente si la delegacion Company esta habilitada. Default OFF, sin
  backfill legacy. Replay 527 devuelve la misma venta/job/stock/numero/draft.
- La autoridad es la delegacion Company explicitamente habilitada por un usuario
  con FISCAL_SETTINGS_WRITE + SIGN + SUBMIT efectivos. No proviene del vendedor,
  JWT/sesion del cajero, IsSystemAdmin ni una sesion abierta del habilitador.
  Revocar requiere FISCAL_SETTINGS_WRITE; cambio y auditoria monotona son atomicos.
  Suspender tenant revoca la delegacion; reactivarlo no la restablece.
- Settings DTO agrega automaticProcessingEnabled y automaticProcessingRevision.
  El DTO del centro agrega issuingJob nullable (state, phase, attemptCount,
  nextAttemptAt, leaseExpiresAt, safeError, updatedAt, receptionAdmitted).
  Legacy/null/desconocido no se presenta como queued. Estado fiscal original,
  recepcion y autorizacion siguen siendo campos independientes.
- POST /api/ElectronicDocuments/invoices/{id}/resume requiere SUBMIT y contexto
  actual. Devuelve 202; no significa Authorized. Reanuda un documento no terminal
  con delegacion vigente, sin resetear una recepcion ambigua para reenviar.
  SIGN/SUBMIT/check manual conservan sus policies. CASHIER no gana permisos.
- Check antes de una recepcion admitida devuelve 409 FISCAL_RECEPTION_NOT_ADMITTED
  sin alterar fase/intentos. Rechazo definitivo devuelve 409 FISCAL_DOCUMENT_TERMINAL;
  no reabre jobs ni bloquea VOID compatible. Recepcion legacy conocida sigue consultable.
- Migracion aditiva 20261004161731_AddAutonomousElectronicIssuing: jobs,
  delegaciones/auditoria, settings OFF/revision cero y referencia de autoridad en
  intentos. Sin migrar bases persistentes ni transformar documentos legacy.
- Claim job-only SKIP LOCKED, batch maximo 8 (worker 1 justo antes de ejecutar), owner UUID, fence y lease
  UTC 2 minutos. Scopes separados por claim/ejecucion, cancelacion y shutdown.
  Ningun segundo job gasta su lease/intentos esperando el HTTP del primero.
  Mutaciones cortas Company -> Sale -> Job; nunca locks DB alrededor del HTTP.
  Cierre condicional por owner/fence/lease: cero filas revierte sale/attempt/job.
- Cada etapa nueva valida delegacion, tenant/contexto y defensas fiscales vigentes.
  El guard serializa settings/certificados/suspension/admission. Una operacion ya
  admitida puede conservar su resultado tras revocacion; no inicia otra etapa.
- Intencion ReceptionInFlight durable antes del HTTP. Expiracion/crash/timeout
  ambiguo recupera consultando MISMA clave/ambiente, nunca blind resend.
  NOT_FOUND/pendiente/respuesta vacia no prueban no recepcion. Backoff exponencial
  acotado a 900 segundos, maximo 12 etapas/intentos antes de atencion manual.
  La recuperacion puntual legacy importa intentos previos de recepcion y fases
  irreversibles al marcador durable; check/resume/regrant no habilitan reenvio.
- VOID cancela/fence trabajos que no llegaron al remoto. En vuelo/incertidumbre
  bloquean void. Rechazo DEFINITIVO corroborado por el ultimo intento de la misma
  clave/ambiente conserva la semantica main de void/stock/caja; AdmittedAt no es
  una prohibicion eterna. No se reimplementa lifecycle NC ni algoritmos fiscales.
- XAdES/XML, transporte SOAP, validadores, intentos, RIDE y email existentes se
  reutilizan. Email automatico se excluye deliberadamente: opcional, evita ampliar
  efectos SMTP; un error SMTP no altera resultado fiscal. No auto-NC.
- UI: tags en lista/detalle existentes, refresh manual, sin polling ilimitado.
  Error de accion inline conserva detalle/historial. POS termina venta y permite
  siguiente cliente sin esperar emision; no se modifica ticket printable ni totales.

## Evidencia automatizada

AutonomousElectronicIssuingTests usa PostgreSQL REAL efimero requerido por CI:
cashier solo POS create, outbox/replay/rollback, SKIP LOCKED con fila retenida,
stale owner resultado/error, expiracion al cierre y rollback conjunto, venta POS
mientras HTTP esta retenido, crash reservado/admitido, ambiguedad y NOT_FOUND sin
reenvio, revocacion/admission ambos ordenes con pg_blocking_pids, gates fiscales,
limite/backoff, permisos HTTP 403, VOID/admission ambos ordenes y rechazo definitivo.
Fixtures generan certificado RSA en memoria y transporte sintetico, nunca SRI real.
Frontend focalizado verifica siete estados/legacy, respuestas tardias A/B/contexto,
errores con detalle conservado, cashier sin llamadas fiscales y grant confirmado,
rollback 403/500, permisos, load/save y destruccion/contexto.

CI agrega verificacion del host sintetico separado con PostgreSQL descartable:
auth HTTP real, worker real, XAdES real y cliente/parser SOAP real con HTTP
interceptado exclusivamente a synthetic-sri.invalid; produccion bloqueada.
El arranque usa configuracion test-only completa, sin appsettings del API ni
overrides de ambiente/CLI para gates. --verify-configuration comprueba el mismo
CreateBuilder/HostingStartup y todos los options de seguridad/operacion, sin
Build/Run, listener ni conexion DB; --verify CI usa el mismo content root humano.
La API/container publica SOLO Pos.Backend.Api: no referencia al host/test assemblies,
no control sintetico ni configuracion privilegiada accesible en produccion.
Docker local OFF: PG local NO ejecutado; CI no omite/simula estos tests.
Ver PR/comentarios para HEAD, comandos, conteos y links PASS vs PENDIENTE reales.

## UN smoke humano sintetico practico

Solo Fernando ejecuta cuando review independiente y CI del HEAD congelado permiten
el gate. Es UN recorrido, no una repeticion ceremonial de las pruebas SQL/totales.
Registrar `git rev-parse HEAD`, PR, IDs/claves sinteticas A/B/C/D y resultado.
No ejecutar con DB Development existente, conexiones compartidas o SRI QA/prod.

### Preparacion descartable (PowerShell, HEAD del PR)

Estos comandos son instrucciones HUMANAS; el implementer no lanza API/browser/PG.
Requiere Docker encendido por el humano, .NET 8 y Node; terminal en este worktree.
No usar --initialize al reiniciar: borra SOLO la DB sintetica dedicada.

```powershell
docker run --name hfpos-528-smoke-pg --rm -d -p 127.0.0.1:65428:5432 -e POSTGRES_DB=hfpos_test_528_smoke -e POSTGRES_USER=hfpos_test -e POSTGRES_PASSWORD=synthetic-only-528 postgres:16-alpine
docker exec hfpos-528-smoke-pg pg_isready -U hfpos_test -d hfpos_test_528_smoke
$env:HF_POS_TEST_CONNECTION_STRING='Host=localhost;Port=65428;Database=hfpos_test_528_smoke;Username=hfpos_test;Password=synthetic-only-528'
$env:HF_POS_SMOKE_WORKDIR=Join-Path $env:TEMP 'hfpos-528-synthetic-smoke'
dotnet run --project backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost --configuration Release -- --verify-configuration
dotnet run --project backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost --configuration Release -- --initialize
```

En otra terminal del mismo worktree:

```powershell
Set-Location frontend/pos-frontend
npm ci
npm start
```

Abrir https://localhost:7096/health/live y aceptar SOLO para esta sesion el aviso
TLS del certificado localhost generado en memoria por el host. No instalar ni
importar certificados reales. Abrir http://localhost:4200. Usuarios exclusivos
sinteticos: `fiscal-autonomous-smoke`, `cashier-autonomous-smoke`; password de ambos
`synthetic-only-528-password`. El host muestra IDs tenant/punto. Certificado fiscal
generado en memoria, cifrado con keyring descartable retenido durante restart.
SMTP no configurado. Delegacion inicial OFF.

Control del remoto falso (PowerShell 7; token obtenido del login REAL del admin):

```powershell
$base='https://localhost:7096'
$login=Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$base/api/Auth/login" -ContentType 'application/json' -Body '{"username":"fiscal-autonomous-smoke","password":"synthetic-only-528-password"}'
$headers=@{Authorization="Bearer $($login.token)"}
function Set-SyntheticMode([string]$mode) { Invoke-RestMethod -SkipCertificateCheck -Headers $headers -Method Put -Uri "$base/api/synthetic-fiscal/mode/$mode" }
function Get-SyntheticCounters { Invoke-RestMethod -SkipCertificateCheck -Headers $headers -Uri "$base/api/synthetic-fiscal" }
function Resolve-SyntheticKey([string]$key) { Invoke-RestMethod -SkipCertificateCheck -Headers $headers -Method Post -Uri "$base/api/synthetic-fiscal/resolve/$key" }
```

Estos controles solo programan futuras respuestas del remoto falso/counters,
NUNCA escriben estado final de Sale/job. No existen en el artefacto productivo.

### Recorrido A/B/C/D y safety

1. Admin en Configuracion fiscal/SRI: habilitar delegacion separada de SRI enabled,
   guardar y confirmar. Ver estado confirmado tras response; no basta mover toggle.
   `Set-SyntheticMode pending`. Cashier vende Invoice A (producto sintetico, cantidad
   1, efectivo suficiente). La venta termina sin esperar fiscal; siguiente cliente
   disponible, ticket sin job/costo/margen. Registrar numero/clave/ID.
2. Cerrar TODO browser. Detener SOLO el host Ctrl+C, conservar contenedor PG y
   HF_POS_SMOKE_WORKDIR. Reiniciar SIN initialize:
   `dotnet run --project backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost --configuration Release`.
   Reabrir admin/Documentos electronicos y refresh: MISMA A, esperando autorizacion,
   un envio. `Resolve-SyntheticKey '<clave A>'`; esperar el proximo backoff mostrado
   y refresh: Autorizado, misma venta/clave. Abrir historial/XML/RIDE. No vender A
   otra vez ni usar submit para recuperar. Los counters deben mostrar submissions=1.
3. `Set-SyntheticMode ambiguous`. Cashier vende B y cierra browser. Admin ve error
   seguro/reintento y luego espera/atencion: recepcion incierta, nunca Authorized
   inventado. Refresh/historial; counters B submissions=1 aun tras consultas.
   Operador valido consulta manual (o, al agotar presupuesto, Reanudar con grant
   vigente): sigue query-only. `Resolve-SyntheticKey '<clave B>'`, consultar/refresh
   hasta Authorized SIN segundo submit. Un error de accion no oculta detalle.
   `Set-SyntheticMode reject`; vender C: Rechazado definitivo, sin loop de reenvio.
4. `Set-SyntheticMode hold`; vender D. Esperar Get-SyntheticCounters muestre D
   submissions=1 (recepcion ya admitida, retenida antes de siguiente autorizacion).
   Admin revoca delegacion, guarda/confirma. `Set-SyntheticMode pending` libera el
   remoto; detener/reiniciar SOLO host sin initialize. D conserva resultado/intencion,
   sin nueva admision automatica de autorizacion. Refresh muestra error seguro/
   atencion; query count no crece automaticamente despues de la revocacion.
   Rehabilitar grant NO reenvia D: recuperacion explicita conserva query-only.
5. CASHIER no ve controles fiscales/settings. Con su login REAL repetir los POST
   sign/submit/check para una Invoice: 403, ningun efecto/call. Operador fiscal puede
   consultar B/D con permisos; durante Processing manual no compite con owner.
   No agregar ReportsRead/SIGN/SUBMIT al cashier para este smoke.
6. Admin selecciona ambiente Produccion SOLO en DB sintetica y guarda; grant sigue
   explicito. Cashier vende Invoice sintetica E: el gate real
   AllowProductionSubmission=false bloquea emision; error seguro visible, counters
   E cero (ningun endpoint real). Dejar configuracion de pruebas al terminar.
   Este paso no usa QA/prod, solo prueba el guard antes del HTTP falso.

La retencion hold debe liberarse dentro de 30 segundos; si timeout ocurre, es
ambiguedad segura y el paso D conserva query-only, no reenviar. No se pide repetir
claims/fencing/races/rollback automatizados al humano. Validar UX, operacion tras
browser/restart y safety observable; registrar desviaciones antes de aceptar.
Solo Fernando declara `VALIDADO OK`; CI verde no sustituye este gate.

### Cierre y rollback

Detener host/frontend; `docker stop hfpos-528-smoke-pg` elimina el contenedor --rm
y su DB dedicada, no bases persistentes. Conservar evidencia/IDs antes. Keyring
descartable puede limpiarse despues de aceptacion, exclusivamente esa carpeta.
Operacionalmente revocar delegacion detiene nuevas fases sin borrar documentos/jobs;
no down-migration ni cambio de ambiente/draft para reparar una recepcion incierta.
Incidencia ambigua se reconcilia por la misma clave; nunca borrar job/reception marker.
