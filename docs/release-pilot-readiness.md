# DEV-531: HF One Release Candidate / Pilot Readiness

Issue #133, alcance activo: comentario 6001881768. Base de implementacion:
`616af30442f19f923e6c1828611dd2efef314c0b`. FEATURE FREEZE. R3 preparacion;
R4 NO AUTORIZADO. Este documento no declara piloto activo ni RC publicado.
Una rama/PR, un writer; los advisors no sustituyen la revision final independiente.

## Inventario y clasificacion

| Area | Clasificacion | Evidencia / limite |
| --- | --- | --- |
| Recovery app sobre DB/keyring restaurados | BLOCKER PILOT | Rehearsal base reiniciaba origen; correccion y negativos en este ticket |
| Recorrido unico enlazado | BLOCKER PILOT | Escenarios previos separados; nuevo escenario en host fake existente |
| Quiescence/deny-egress restore-startup | BLOCKER PILOT | Runbook DR explicita escritores, SRI TEST/Production, SMTP, worker local |
| Cleanup CI Linux | BLOCKER PILOT | ff227 completo funcionalmente pero job rojo por glob nativo de `--profile '*'`; fix minimo migration/proof, regresion real de argv Linux |
| Recovery estado de secuencias | BLOCKER PILOT | Filas iguales no prueban siguiente insert; fingerprint incluye schema/name/last_value/is_called, negativos independientes en tercer destino desechable |
| EF/PostgreSQL assurance | BLOCKER PILOT | Evidencia local completada: 2 tests empty/poblada compatible; sin migracion nueva ni promesa de upgrade arbitrario |
| Dependencias runtime/config/no secretos assurance | BLOCKER PILOT | Evidencia local completada: API con 65 transitivas y npm runtime sin advisories conocidos; controles log/config existentes |
| .NET 8 lifecycle | POST-PILOT | EOS 10-Nov-2026; revisar fecha antes del piloto, nunca iniciar sobre runtime sin soporte |
| Actions con tags mutables | POST-PILOT | Hardening diferido; no compromiso demostrado, sin churn bajo freeze |
| Matriz legacy adicional | POST-PILOT | Solo snapshots compatibles validados; datos ambiguos requieren decision humana, no autorepair |
| Overflow UI preexistente | POST-PILOT | QA tecnica readonly: dashboard +26px y report 2148px frente a viewport1440; no full UX PASS ni fix bajo freeze |
| Mantenimiento OS PCRE2/Perl/SDK/TLS | POST-PILOT | Advisor de reachability no demostro blocker; CVEs presentes y riesgo TLS residual documentados abajo |
| OS/SDK sin camino alcanzable identificado | POST-1.0 | zlib contrib/minizip Bookworm, herramientas locales/DTLS/decoders no usados, Crypto.Xml del dotnet-format no invocado; no inmunidad universal |

Funcionalidades nuevas y cosmeticos permanecen fuera de scope; no discovery ni DEV-532.

No se ha demostrado un BLOCKER PILOT de producto por los advisors de seguridad y
migraciones. No se modifican controllers, servicios de negocio, DTOs, schema ni
semanticas de dinero/stock/auth. La evidencia se delimita por commit, comando y
entorno, no por declaraciones generales de seguridad.

## Escenario sintetico unico

`Hfpos.FiscalSmokeHost --release-pilot --initialize` usa API/auth reales y
PostgreSQL propio: platform login -> provision -> admin tenant session -> cinco
imports (categorias/productos/clientes/proveedores/stock inicial) -> fresh login
tenant -> caja -> Ticket/cambio/replay/lectura para print -> Invoice/background
worker/XAdES/SOAP fake/PDF -> compra -> stock/transfer -> NC autorizada fake ->
inventory return/refund -> cierre -> settlement del dia finalizado con reloj de
prueba -> valor SMTP protegido. Ningun certificado fiscal/SMTP/SRI real.

El host esta fuera del artefacto production. Conserva el cliente SOAP/worker del
producto; fake HTTP solo acepta `synthetic-sri.invalid`, fake SMTP solo payload
sintetico. El reloj mutable es fixture de prueba, no cambio al reloj productivo.
El rehearsal detiene backend/web de origen durante el flujo fake para que SOLO
su worker reclame jobs; mantiene quiescence durante backup/recovery y los reinicia
solo despues de comprobar jobs terminales y recovery completo.
El print de navegador no puede acreditarse por HTTP: CI frontend conserva sus
regresiones de recibo/80mm y el smoke humano verifica preview/print/reprint.

`start-deployment-smoke.ps1` enlaza ese escenario con dump verificado y backup de
keyring real sintetico. Arranca un backend y edge nuevos sobre restore, autentica
ambos planos y arranca ademas una app Production test-only sobre DB+keys restaurados
para descifrar mediante servicios SMTP/certificado del dominio y comprobar negocio/EF.
El repositorio DP real y su ApplicationDiscriminator se comprueban, no solo config.
Sentinelas: SaleItems costo/linecost/profit 3/3/7 conservados tras compra/costo actual
4; NC/refund y snapshots del documento original, XML signed/autorizacion presentes;
hash de contenido JSON canonico ordenado de todas las tablas y estado logico de
todas las secuencias no-system (schema/name/last_value/is_called) source/restore
igual antes de arrancar destino, sin
imprimir filas/XML. El certificado sintetico se carga en memoria con private key
y vigencia validas, sin firma/envio en recovery ni export de bytes.
Wrong key, empty key, ApplicationName y purpose fallan cerrados; ready no sustituye
decrypt. Dump data-only completo del origen antes/despues (staged/fail-closed) y
keyring iguales acreditan origen intacto durante recovery. No se comparan dumps
textuales entre bases: el orden OID/fisico puede variar sin perdida de contenido.
Jobs terminales
antes del backup: no se promete destino intacto con trabajo fiscal pendiente.
El fingerprint no usa `log_cnt` (WAL/preallocation interno), ni ejecuta nextval/setval
en source/restore principal. Un tercer destino `hfpos_ops_sequence_negative` se
restaura desde el mismo backup: alterar solo last_value y luego solo is_called
debe cambiar el fingerprint con filas intactas. Cada caso restablece estado antes
del siguiente; se comprueba source readonly y se elimina SOLO esa tercera DB.

## Migraciones y datos

Empty: las 60 migraciones efectivas de la assembly, applied=defined, pending=0 y modelo consistente.
Populated: dos companies/users sinteticos desde InitialSchema, asignacion explicita
compatible de establecimientos al llegar a AddUserEstablishment, y cada migracion
restante aplicada por orden. La asignacion es fixture, no repair en migraciones.
Una DB legacy con users sin establecimiento puede fallar en AddEmissionPoints;
duplicados de RUC/username/email se rechazan por controles historicos existentes.
Preflight humano compara lista EF/snapshot, contexto operativo, duplicados y datos
compatibles. No inferir que esto certifica cualquier DB legacy o negocio historico.
No EF Down, database update persistente ni datos reales en pruebas autonomas.

## Evidencia Tecnica Local

Preparacion desde base `616af304` con codigo productivo/frontend y lockfiles sin
cambios. El SHA final y CI exacto se registran en el PR, nunca se relabela CI de base.

- `dotnet test ...Pos.Backend.Api.Tests -c Release --filter FullyQualifiedName~MigrationSmokeTests`:
  2/2 pass, empty y poblada compatible; PostgreSQL 16 efimero del coordinador.
- `dotnet build ...Hfpos.FiscalSmokeHost -c Release --no-restore`: pass.
- `dotnet run ...Hfpos.FiscalSmokeHost -c Release --no-build -- --release-pilot --initialize`:
  recorrido completo y valor protegido pass en `hfpos_test_531`.
- `pwsh ...start-deployment-smoke.ps1 -SkipBuild`: ejecucion preservada tras
  interrupcion, source/restore quedaron activos; stdout/exitcode de esa llamada
  no recuperables, NO se relabelan como PASS completo. Recheck enfocado de funciones
  del script sobre el mismo proyecto: exit0, contenido canonico source/restore
  igual, ready ambos, host restored con ambos logins/SMTP/certificado/costos/snapshots
  PASS, wrong/initially-empty/ApplicationName/purpose fail-closed, full source dump
  antes/despues igual, keys iguales y marcadores sensibles ausentes en logs.
  Se conserva source8444/restore8445 para humano. CI final ejecuta el script
  completo con stdout/exitcode verificables y cleanup de todos los perfiles.
- CI de `ff2270f2e6640ff358a2a7a305982f97ba11ce09`,
  [Containers run37373847997](https://github.com/fhamann79/hfpos/actions/runs/37373847997/job/111977342225):
  21:11:12Z TRUE RECOVERY PASS, 21:11:21Z REHEARSAL PASS 230.1sec; JOB FAIL en
  cleanup y always-clean. NO CI verde: Unix PowerShell expande wildcard splatted
  hacia archivos del cwd al cruzar native binder. Perfiles Compose reales son
  migration/proof y ahora se pasan explicitamente, sin cambiar wrapper global.
- Batch autorizado post-review ff227: Noether M1 secuencias, Cicero B1 cleanup/M1
  secuencias y GitHub P1 secuencias. Correcciones requieren delta/integrationreview
  independiente y CI Linux completo del nuevo HEAD; no se hereda aprobacion.
- `pwsh -NoProfile -File scripts/ops/test-smoke-support.ps1`: 14/14 pass en
  Windows PS7.6.5 y Linux PS7.5.0 (contenedor oficial standalone sin red/socket/DB).
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...`: 14/14 pass,
  PS5.1.26100.9444. Regresion nueva RED con wildcard / GREEN migration+proof usa
  un proceso nativo real para comprobar argumentos, no solo mock del helper.
  Cleanup Compose end-to-end y version Linux del runner se validan en CI final.
- `test-recovery-fingerprints.ps1 -Container <owned hfpos-dev531-sequences-* >`:
  pass PS7.6.5 y PS5.1.26100.9444, PostgreSQL16 aislado sin puertos/red/volumen
  persistente. RED anterior detectado: last_value corrupto pasaba con filas iguales.
  GREEN: igualdad inicial/tras reset, last_value-only e is_called-only rechazados,
  filas intactas/source readonly, nombres quoted/no-public inventariados; ademas
  helper integrado con backup/restore guardado y tercer destino PASS. Cada prueba
  uso contenedor nuevo propio, eliminado exactamente; preview no tocado.
  CI full rehearsal ejecuta los mismos negativos sobre dump del escenario completo.
- QA tecnica readonly del coordinador en source8444/restore8445: Playwright con
  nuevos contextos, TLS synthetic ignoreSSL solo localhost propio; tenant/platform
  login UI PASS, dashboard/inventory/salesreports/electronicdocuments/cashsessions
  cargan/rutas correctas/pageErrors0. 12 screenshots fuera del repo; datos coherentes
  stock22/costo4/netSales10/netProfit7. NO full UX PASS: overflow preexistente arriba.
  Logins UI reales sinteticos pueden registrar auditoria/metadatos de auth: la
  igualdad de fingerprints documentada corresponde al recovery previo a esa QA,
  no al estado actual del preview ni a su backup antiguo. No se fuerzan filas iguales.
  NO aceptacion humana inferida; smoke humano/print y gates siguen pendientes.
- API exacta: `dotnet restore ...Pos.Backend.Api.csproj --force-evaluate
  -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low` pass sin advisory;
  `dotnet list ... package --vulnerable --include-transitive --format json`
  sin paquetes vulnerables conocidos; inventario 16 directos/65 transitivos.
- `npm audit --omit=dev --audit-level=high --package-lock-only --json`:
  21 entradas runtime, 0 vulnerabilidades conocidas. No instala ni actualiza paquetes.
- Inspeccion backend candidato: ASP.NET/.NET 8.0.31, ausentes DLL de host/tests.
  Version de patch vigente y EOS segun [Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
- Inspeccion versionada por coordinador: extensiones pfx/p12/pem/key/env/dump/bak
  sin matches; patrones private-key/AWS/GitHub sin archivos coincidentes.
  Complementar diff/review y log tests; NO certifica toda la historia Git.

IDs LOCALES (no digests de registry, no firma ni attestation comprobada), contexto
`desktop-linux`, namespace `hfpos-dev531-cef61a90fb87`:

| Imagen | ID local |
| --- | --- |
| backend:smoke | sha256:437cb4514bd53b874bb542b2d48f16e62404ff08bf3fa8e4c272c99251bc76ce |
| web:smoke | sha256:162b3a96daeec4086d2fcc036ec66b46973117fb69920d31264fd870b5c6e6cf |
| migrations:smoke | sha256:a6b6268aac5798ac713f75a81f11a6f9e44bf84c4e8c2fc047be9e5bdff8e0cf |

Scans actuales del coordinador sobre IDs exactos: backend OS262 (igual baseline),
0 findings managed de Trivy sobre 3 archivos .deps, secrets0; web OS3 (igual
baseline), secrets0. El bundle FE no contiene package manifests, por eso npm
runtime se audita separadamente. Migrations exacta: 470 findings = 449 OS baseline
+ 21 managed de herramientas SDK (no API): dotnet-format Crypto.Xml 8.0.3, 5 HIGH;
MSBuild Tasks.Core 17.11.48/CVE-2025-26646, 6 HIGH; NuGet 6.11.2-rc1, 10 LOW
defense-in-depth. Secrets0 en las tres imagenes. Delta: 21 fixable, 11 HIGH/10 LOW,
6 CVEs/1 GHSA. Advisor readonly confirma NO BLOCKER PILOT demostrado; no autofix
por conteos ni claim de vulnerabilidad del package API Crypto.Xml 8.0.4.
Trivy no reemplaza NuGetAuditMode=all.

POST-PILOT: MSBuild [CVE-2025-26646](https://github.com/dotnet/msbuild/security/advisories/GHSA-h4j7-5rxr-p4wc)
aplica a DownloadFile; `--no-build` de EF aun usa MSBuild/GetEFProjectMetadata,
pero no se encontro DownloadFile ni proyecto externo controlable en entrypoint.
NuGet Packaging/Protocol [GHSA-g4vj-cjjj-v7hg](https://github.com/NuGet/NuGet.Client/security/advisories/GHSA-g4vj-cjjj-v7hg)
requiere mantener validacion de ID/version/descarga: entrypoint no hace restore,
el build SI restaura. Conciliar SDK8.0.425 vs fixes anunciados 8.0.410/420 y
deps antiguas POST-PILOT; NO suprimir ni marcar falso positivo automaticamente.
POST-1.0: Crypto.Xml8.0.3 del dotnet-format (5 HIGH) no se invoca por entrypoint
ni EF; API conserva Crypto.Xml8.0.4 auditado separadamente. Estas son conclusiones
de alcance actual, no garantia ante proyectos/feed/inputs/topologia nuevos.

CycloneDX 1.7 SBOM local de los tres candidatos, coordinator-owned, NO firmado,
NO publicado y NO provenance; hashes comunicados (artefactos en temp externo):

| SBOM | Componentes | Hash |
| --- | --- | --- |
| backend | 140 | 3f863674f1b87f537ff114540f798ef6b259c6ffa48167ebd2e20feaf5368c17 |
| web | 71 | a11da9f048427d47d6d9f5d08ea904a1612e495da463ea8afd48305fc4aae50c |
| migrations | 772 | 6964a545e257bceaa7af9c6d8fde4d571f9e98385c15e08491b74f4978a2243d |

Binding: estas imagenes son precommit con runtime productivo unchanged desde
base616; solo scripts/tests/harness/docs cambiaron. No declararlas build del SHA
final: metadata git de assemblies puede variar tras commit. CI required construye
y prueba el HEAD exacto; tras tag humano agentes/CI verifican readonly artefactos
publicados, digests, labels y SBOM/provenance efectivos. No hubo push OCI local.

### OS Baseline y Reachability

Reporte del coordinador: Trivy 0.75.0, DB publica 05-Oct-2026 19:07Z, sin
credenciales. Estos conteos son BASES, no certificacion del artefacto final:

| Base | Entradas / CVEs unicos | Critical / High |
| --- | --- | --- |
| ASP.NET Debian 12.15 | 262 / 120 | 4 / 55 |
| nginx Alpine 3.24.2 | 3 | 0 / 1 |
| SDK migrations | 449 / 183 | 13 / 94 |
| Agregado (deduplicado por CVE) | 714 / 184 | 17 / 150 |

106 entradas fixable (33 CVEs unicos); 15 critical fixable (3 unicos). No declarar
"0 CVEs" ni inferir defectos explotables por severity/count. El advisor readonly
Popper sobre base `616af304` no encontro BLOCKER PILOT demostrado; no es review
final ni aceptacion humana del riesgo, y no justifica pin churn bajo freeze.

POST-PILOT: PCRE2 CVE-2026-103111 requiere patron controlado por atacante y JIT;
nginx usa patrones fijos versionados, puerto validado, y regex .NET no usa PCRE2.
Es una inferencia de alcance del repo, no una mitigacion desplegada ni prueba
de todas las topologias. [Advisory PCRE2](https://github.com/PCRE2Project/pcre2/security/advisories/GHSA-r9hj-j2rw-4q3m).
Perl no tiene invocacion/parse desde app identificado; seguir mantenimiento
Bookworm (fix deb12u4) y SDK Perl/PCRE2/Expat. OpenSSL SI se usa para TLS:
CVE-2026-35189 conserva riesgo de memoria ante certificado de peer malicioso;
non-root no lo elimina. [OpenSSL 29-Sep-2026](https://openssl-library.org/news/secadv/20260929.txt).

POST-1.0: zlib CVE-2023-45853 afecta contrib/minizip no compilado en binarios
Bookworm, segun [Debian tracker](https://security-tracker.debian.org/tracker/CVE-2023-45853).
No se encontro camino de app para util-linux/mount/nsenter, systemd-homed, ncurses,
gzip LZH, ACL ni DTLS CVE-2026-84782; nginx no incorpora nghttpx/image_filter ni
decoder libpng en el uso inspeccionado. Cambios de paquetes, modulos, transporte
o hosting requieren reevaluar alcance. No ejecutar exploit ni cambiar production.

El coordinador conserva reportes `aspnet-base.json`, `nginx-base.json` y
`migrations-sdk-base.json` en su runtime de infra; la evidencia segura del PR
debe distinguir estos resultados de scans posteriores de candidatos exactos.

## Candidato vs Release

No hay release/tag RC publicado al iniciar DEV-531. No se elige version humana.
El workflow existente permite solo `vX.Y.Z` (sin sufijo `-rc`); valida pertenencia
a main y publica backend/web/migrations con OCI revision/version, SBOM y provenance.
Eso es CAPACIDAD DECLARADA, no una attestation comprobada para main/candidato.

El PR registra HEAD exacto, required CI, build local sintetico e IDs/digests
disponibles sin push. No reclamar firma criptografica: provenance/SBOM no equivalen
a firma ni scan de vulnerabilidades OS. Registrar explicitamente lo no ejecutado.
Tras merge/aceptacion, el humano elige version compatible con la politica; despues
de publicacion se verifican tres digests inmutables, labels SHA/version, SBOM y
provenance efectivos. Nunca desplegar solo con tags mutables ni artefactos locales.

## Gates Humanos

1. VALIDACION HUMANA PRE-MERGE REQUERIDA: CI exacto + revision fresca B0/M0, luego
   smoke sintetico corto del candidato congelado. No merge del implementer.
2. Decision hosting: proveedor/topologia, dominio/DNS/TLS, PostgreSQL administrado
   o propio, storage/keyring compartido, secret store, backups externos cifrados,
   retencion, observabilidad, alert routing y responsables. Sin defaults reales.
3. Aprobar RPO/RTO con medicion de volumen/recovery point y ventana. El tiempo del
   rehearsal CI/local no es RTO ni SLA real.
4. Tag/version requieren decision humana. Scan OS del candidato y comprobaciones
   readonly de tres digests/SBOM/provenance despues del tag son trabajo tecnico
   NO-R4 del coordinador/CI, sin credenciales reales: pendientes hasta evidencia.
   NuGet/npm no certifican capas OS; no convertir este control tecnico en ceremonia humana.
5. R4 HUMAN EXECUTION REQUIRED - DEV-531: runbook/backup/rollback/aprobacion de
   ejecucion independiente para hosting/deployment/DB/migrations/certs/SRI/secrets/
   DNS/TLS/backup-restore reales. Nada de esto se autoriza al aprobar codigo.

Opciones portables pendientes: single-host con proxy y PostgreSQL externo, o
plataforma administrada con LB/storage/keyring compartido. Elegir por ownership,
restore verificable, deny-egress, cifrado/ACL, capacidad, coste y RPO/RTO aprobados;
no se infiere proveedor, dominio, credenciales ni topologia cloud.

## Smoke Humano Pre-Merge

Solo datos generados del rehearsal en worktree congelado, origen 8444 y restore
8445. Credencial platform aleatoria en runtime ignorado; tenant `pilot-tenant`,
password `Synthetic-only-531-tenant`. Nunca reutilizar fuera de smoke.

1. Confirmar HF One visible en login tenant/platform; ambos logins funcionan en
   origen y restore, y tokens no cruzan planos.
2. Tenant: revisar batches iniciales, stock origen/destino y compra sintetica;
   comprobar Ticket total 10, recibido 20, cambio 10 y reimpresion 80mm sin venta nueva.
3. Revisar Invoice/PDF Authorized (fake), NC/refund 10, caja cerrada esperada 20
   y settlement Card gross 10/refund 10/net 0. No intentar envio real ni email.
4. Restore: mismos documentos/totales, login ambos planos; revisar evidencia segura
   `TRUE RECOVERY ... PASS` y negativos criptograficos, nunca abrir/dumpear claves.
5. Confirmar B0/M0 y CI sobre ese HEAD; registrar VALIDADO OK o defectos concretos.

## Rollback / Observabilidad

Rollback solo imagen previa si schema/config compatibles; en caso contrario
forward fix o restore humano a NUEVA DB, evaluando perdida de escrituras, nunca
EF Down automatico. [Deployment](deployment-runbook.md) y [DR](disaster-recovery.md)
mantienen orden, deny-egress y quiescence. [Observabilidad](observability-runbook.md)
cubre live/ready, 5xx/latencia, reinicios, DB, edad/resultado backups y jobs fiscales.
Antes del piloto el humano prueba alertas y confirma responsable, umbrales y ruta.
No se simula que un proveedor o sus alertas ya estan conectados.

Estado: PENDIENTE. NO MERGE. R4/hosting/RPO-RTO/tag/publicacion/piloto reales pendientes.
