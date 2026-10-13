# DEV-531 / R4: traspaso de ejecucion humana (09-Oct-2026)

> **Estado: evidencia de sesiones humanas, NO una certificacion automatica ni un permiso de operaciones R4 para agentes.**
> Ambito exclusivo: DEV-531 / issue #133; feature freeze; NO iniciar DEV-532. El estado del issue de 06-Oct-2026 era historico (antes del despliegue) y queda supersedido para las fases R4 documentadas aqui.

## 1. Identidad, fuente de verdad y alcance

- Repositorio: fhamann79/hfpos. Main confirmado por GitHub el 09-Oct-2026 en 99b92e0e0f6f34e5dd3921016f787e73ad1c441d. El checkout del VPS reporto el mismo SHA y git status --porcelain vacio.
- RC inmutable para la ejecucion: v0.1.0-rc.1; GHCR backend sha256:5654a239a032076e72306e1d7057377eaddf30674872195a89068cae6f40cbe1; web sha256:a11cf734359ed29a11716d9e2bc2e6040b84f134e2e10010734c6bb96f1878fc; migrations sha256:2bd3193423ae552dd0b77b68359529e28414c359d992c61441e28829b370197a.
- La evidencia operativa aqui descrita procede de salidas de consola y capturas enviadas por el operador durante las sesiones R4 del 08-09 Oct, revisadas conversacionalmente. No son logs firmados ni verificaciones independientes continuas. GitHub conserva codigo/runbooks, pero NO los datos, secretos o archivos reales del VPS. Revalidar estado live antes de actuar.
- Documentos canonicos: docs/disaster-recovery.md; docs/pilot-hosting-runbook.md; docs/deployment-runbook.md; docs/production-configuration.md; docs/observability-runbook.md; scripts/ops/postgres-backup.sh, verify-postgres-backup.sh, keyring-backup.sh, postgres-restore.sh, verify-restored-db.sh, keyring-restore.sh.
- Este handoff no introduce cambios de codigo funcional, de esquema, ni concede autorizacion de merge/deploy. Su PR de documentacion es continuidad del maestro #133. El implementer Codex debe continuar en la misma rama/PR o acordar expresamente dependencia para evitar PRs paralelos innecesarios.

## 2. Registro de decisiones y autorizaciones humanas (NO transferibles a Codex)

| Decisión / alcance | Constancia y estado |
| --- | --- |
| DEV-531 preparacion NO-R4 | Aprobada previamente, PR145/PR146 integrados. Autorizacion separada del tag y publicacion GHCR tras verificar main y checks, documentada en el historial del issue #133. No concedia autorizacion R4. |
| Eleccion infraestructura | Operador contrato y configuro VPS Contabo y hfone.app/Cloudflare durante R4 humano. Acciones ejecutadas interactivamente, no autoridad general para agentes. |
| Migracion real DB | Autorizacion humana textual previa: "AUTORIZO MIGRACIÓN R4". Se aplico one-shot usando imagen de migraciones exacta, nunca migracion automatica al iniciar. |
| Cloudflare R2 | Operador eligio y activo R2, creo bucket privado Standard, credencial restringida al bucket y realizo pruebas, luego upload real durante la operacion humana. No implica permiso de borrado/rotacion por agentes. |
| Cifrado age | Operador decidio expresamente continuar generando clave age y ejecuto los pasos; NO consta que respondiera literalmente "AUTORIZO AGE PARA CIFRADO OFFSITE R4" a la frase sugerida. La clave privada se genero y conservo en Windows, solo la publica llego al VPS. |
| Ventana para backup real | Autorizacion textual exacta: "AUTORIZO VENTANA DE BACKUP REAL R4" (08-Oct-2026); se ejecuto el 09-Oct. Incluia parar temporalmente web/backend, capturar y reanudar. Ya concluida. |
| Ensayo de restauracion aislada | Autorizacion textual exacta: "AUTORIZO ENSAYO DE RESTAURACIÓN AISLADA R4" (09-Oct-2026). SOLO ensayo controlado, separado y supervisado; NO autoriza restaurar sobre produccion, acceder por agentes a VPS/secretos, borrar datos, habilitar SRI/SMTP o arrancar app de recuperacion sin egress deny verificado. |
| Registro GitHub / continuidad | Operador ordeno expresamente documentar todo el R4 y las autorizaciones en repo/issue para que Codex trabaje con contexto fiable. Es autorizacion documental, NO para ejecutar automaticamente R4. |

NO aprobados: RPO 24 h / RTO 4 h (propuestas, no SLA), retencion 7 diarias + 4 semanales + 3 mensuales, frecuencia/horario de tareas, borrados/lifecycle, custodio formal y copias recovery externas, presupuesto definitivo de offsite, alertas y sus contactos/escalacion, cambiar DNS proxy, SRI Production, operaciones fiscales reales, provisionar tenant de negocio, limpiar staging o cerrar #133. NO iniciar #134/DEV-532.

## 3. Infraestructura real y evidencia historica de ejecucion

- Contabo VPS Ubuntu 24.04 LTS, Docker/Compose; proyecto Compose **hf-one-pilot**, obligatorio usar -p hf-one-pilot en comandos. Una consulta Compose sin -p produjo ps vacio aunque tres contenedores estaban running. No inferir caida desde esa salida.
- En el chequeo del 09-Oct tras backup: hf-one-pilot-web-1 running (publico 443 HTTPS), hf-one-pilot-backend-1 healthy, hf-one-pilot-postgres-1 healthy. GET https://hfone.app/health/ready = HTTP 200. Esto es un checkpoint, NO monitoreo en tiempo real.
- Web/backend no exponen puertos host de API/DB; PostgreSQL solo privado. UFW permite SSH/HTTPS, 80 temporal en hooks ACME. Cloudflare DNS solo, sin proxy. Certificado Let's Encrypt para hfone.app expedido; Certbot renew dry-run y hooks open80/deploy/reload/close80 probados, pero alertas de expiracion aun pendientes.
- PostgreSQL 16.15-bookworm por digest sha256:0ea6700a3b4f0ae6ce746519073558aed4d88a79d8d07622a9a644946c7319c4; datos persistentes en /srv/hf-one/pilot/storage/postgres. Roles admin/bootstrap, migrator (owner), runtime (login separado sin privilegios de DDL): se reportaron pruebas efectivas. Migraciones one-shot autorizadas: 60, ultima 20261005131215_AddAssistedPasswordRecovery; 83 objetos owned by migrator; runtime DML 43/43 tablas y no CREATE schema. Revalidar al restaurar.
- Keyring de produccion en /srv/hf-one/pilot/keyring, owner numerico 1654:1654, modo 700; un archivo XML, modo 600. Backup local keyring previo verificado; secreto de bootstrap temporal y override eliminados despues de crear 1 platform admin. PlatformBootstrap__Enabled=false; login de plataforma verificado por humano en /platform/tenants (Empresas=0 en ese momento). No divulgar identidad ni contrasena del admin.
- Config externa /etc/hf-one/pilot/config/pilot.env, root:root modo 600. Directorio de secretos /etc/hf-one/pilot/secrets root:root 700; NO imprimir env, connection strings, tokens, privada SSH ni TLS keys. SRI_ALLOW_PRODUCTION=false; no asumir que eso por si solo bloquea SRI TEST o trabajadores en background.
- Backups locales previos verificados: pre-migration, pre-live y keyring. No se han limpiado. Sin retencion/remocion automatica aprobada.

## 4. Proveedor offsite, errores resueltos y cifrado

- Cloudflare R2, bucket **hf-one-pilot-backups**, clase Standard, acceso publico desactivado. Token Object Read & Write limitado al bucket y filtrado por IPv4 de salida autorizada. El token puede escribir/leer objetos y **NO es append-only**: riesgo pendiente de compensar con Object Lock/Bucket Locks/politica separada y role de limpieza, previa decision humana.
- Credenciales root-only en /etc/hf-one/pilot/secrets/r2-credentials.json (0600); rclone en /etc/hf-one/pilot/secrets/rclone-r2.conf (0600). No documentar sus valores. Version rclone Ubuntu 1.60.1-DEV. Config validada contiene type=s3, provider=Cloudflare, no_check_bucket=true, no_head=true; **sin acl=private**, bucket permanece privado por politica R2.
- Problemas hallados: 403 AccessDenied al listar sin fijar IP (VPS tambien tiene IPv6); fijar --bind a la IPv4 autorizada produjo LIST EXIT=0. Subida a veces 501 NotImplemented/retry; eliminar acl no soluciono; usar --s3-no-head/ no_head=true evito 501 en prueba con un solo intento. **No inferir causa tecnica exacta del 501 sin trazas adicionales.** Por omitir HEAD, exigir pull-back y hash externos, nunca fiarse solo de exit=0.
- age Windows v1.3.2 y Ubuntu VPS v1.1.1; clave privada creada en Windows en carpeta HFONE-Recovery, ACL verificada solo usuario, sin copiar al VPS/R2/GitHub/chat; archivo publico age copiado a /etc/hf-one/pilot/backup/hfone-offsite-recovery.pub (root 0644), CRLF normalizado a LF, formato validado. **La clave privada actualmente tiene una sola copia conocida en el PC**. Copias USB cifradas + Google Drive cifrado y prueba de custodia aun NO ejecutadas. No formatear esa PC antes de completar recovery copies.
- Pruebas sinteticas completadas: cifrar en VPS con publica -> SCP al Windows -> descifrar con privada -> SHA256 exacto; cifrar -> R2 -> pullback -> SHA256 ciphertext -> Windows descifrar -> SHA256 plaintext exacto. Se guardaron objetos sinteticos en _synthetic, **sin autorizacion de limpieza**.

## 5. Backup real 09-Oct-2026: alcance, evidencias y limites

1. Ventana humana iniciada 2026-10-09T16:56:46Z. Se detuvo solo web y backend via Compose -p hf-one-pilot; postgres permanecio healthy. HTTPS dejo de responder de manera esperada. Chequeo pg_stat_activity sobre DB activa: **0 client backends** distintos de la consulta, antes de capturar.
2. Captura en staging root-only /srv/hf-one/pilot/backups/offsite-staging/20261009T170756Z (subdirectorios db, keyring, snapshot, metadata). Scripts oficiales produjeron: BACKUP CREATED, BACKUP VERIFY PASS, KEYRING BACKUP PASS. Dump /db/hfpos-20261009T170757Z-1.dump (251810 bytes), keyring tar (10240 bytes), snapshot TAR config/secrets/TLS (30720 bytes), release/roles/grants, hashes/manifests. El pgpass.tmp y client.env temporales se comprobaron ausentes despues. El dump es custom/no-owner/no-privileges; grants y ownership no vienen en pg_restore, deben reconstruirse/validarse.
3. Snapshot incluyo configuracion/secretos y TLS **en claro DENTRO del staging root-only**, no subir ni compartir esos archivos. Manifest checksum del snapshot fue normalizado a nombre relativo, verificado; checksum keyring e internal-sha256.txt completos PASS.
4. Backend/web reiniciados. Backend HEALTHY, HTTPS READY PASS. Fin reportado 2026-10-09T17:08:14Z. Operacion finita; no mantener ventana abierta ni repetir captura sin razon.
5. Cifrado en streaming tar | age, sin TAR combinado intermedio. Archivo opaco local /srv/hf-one/pilot/backups/offsite-ready/68eedc0f1cef4dfcbed48189ece3b9ce.age (307464 bytes) y manifest exterior solo ID/fecha/tamano/SHA256. El staging plaintext permanece en VPS, root-only, pendiente de limpieza **aprobada** posterior.
6. Subida real a R2 bajo recovery/68eedc0f1cef4dfcbed48189ece3b9ce/ de ciphertext y manifest. Pull-back nuevo bajo /srv/hf-one/pilot/backups/offsite-pullback/68eedc0f1cef4dfcbed48189ece3b9ce. Ambos download, ciphertext SHA256/size y manifest bit-a-bit PASS.
7. Copia de pull-back cifrado a Windows via SCP. SHA256 ciphertext coincidente; age -d con clave privada externa transmitio flujo a tar -tf - para listar sin extraer secretos: **REAL R2 CIPHERTEXT WINDOWS PASS** y **REAL R4 OFFSITE DECRYPT + TAR VALIDATION PASS**. Miembros listados: db/, keyring/, metadata/, snapshot/. Esto demuestra descifrado y listado, **NO** pg_restore ni restore de datos/aplicacion.
8. Recovery ID **68eedc0f1cef4dfcbed48189ece3b9ce**; ciphertext SHA256 **6188f0a2f60dd648fe6ccfb030cfcb4493dd5fc4388a87fef52b73579e616d89**, 307464 bytes. Son identificadores/integridad, NO credenciales.

## 6. Punto EXACTO de parada (09-Oct, ultimo output del operador)

- Ultimo preflight: git HEAD exacto, git status limpio, production web/backend/PG activos y healthy, HTTPS READY PASS; offsite pull-back sha PASS; no recovery previo ni contenedores recovery; imagen PostgreSQL por digest linux/amd64 local; disco 91 GB disponible.
- Se creo solamente el workspace **/srv/hf-one/pilot/recovery-r4/68eedc0f1cef4dfcbed48189ece3b9ce**, con subdirectorios **incoming, postgres-data, keyring-restored**, todos **root:root, modo 700**; salida final exacta: **RECOVERY WORKSPACE PREPARED PASS**.
- **NO** se ejecuto el siguiente bloque propuesto para crear /home/hfadmin/.hfone-r4-transfer-...; no asumir que existe. **NO** se creo DB nueva, contenedor recovery, red, ni se ejecuto pg_restore/keyring-restore ni se arranco app restaurada. Produccion NO se paro en esta etapa.
- No rehacer los pasos validos, no intentar recrear workspace con script que exija ausencia total (fallaria), no extraer todo el backup real en Windows, no imprimir secretos ni subir material real a GitHub.
- Se acordo pasar a Codex para **preparar automatizacion y ensayos con datos sinteticos**, preservando las operaciones reales R4 como gate humano. La autorizacion de ensayo aislado sigue limitada a su alcance, pero la creacion/modificacion de scripts NO autoriza su ejecucion por Codex en VPS.

## 7. Pendiente: automatizacion y true restore, con gates

1. Codex: RECONCILE main/issue #133/runbooks/scripts; elegir diseno minimo, fail-closed, para transferir desde Windows *solo* db/keyring/metadata necesarios del archivo cifrado, sin divulgar private age ni volcar todos los secretos. Preferir scripts pequenos auditable/testeables sobre paste masivo en terminal.
2. Testear con fixtures sinteticos y sin tocar VPS real. Preflight de rutas/UID/ACL/symlinks, backup SHA256, tar safe extraction y medidas que eviten interaccion con prod. Usar DB recovery **nueva y vacia** y keyring recovery **nuevo y vacio**, nombres/rutas y redes inequivocamente separados del servicio original.
3. En ejecucion HUMANA posterior (pasos supervisados), reproducir el flujo de recuperacion a nueva DB y keyring usando scripts oficiales y HFPOS_RESTORE_APPROVED=YES **solo** para destino controlado, recrear roles/grants y secuencias logicas, verificar schema/EF y conteos. No conectar nunca al origen, no usar --clean ni EF Down.
4. Para arrancar **aplicacion** recuperada, exigir denegacion saliente comprobada para SRI TEST/Production, SMTP y cualquier externo, aplicada a redes/interfaces Docker REALES y probada antes del start, segun docs/disaster-recovery.md. No usar solo flag SRI production=false; controlar jobs/leases. No arrancar app real restaurada con egress sin bloquear.
5. Verificar verdadero restore de aplicacion/lectura de datos/login/desencriptacion DP y origen intacto, con test isolated/datos autorizados. Si no se puede demostrar safe egress/no side effects, DETENER y describir blocker; nunca cambiar production como atajo.
6. Pendientes adicionales de cierre antes de declaracion de piloto: copias externas de clave age y SSH + custodia; politica aprobada RPO/RTO/retencion/horario; automatizacion programada y presupuesto R2/alertas; backups continuos y prueba periodica restore; SMTP/recovery acordados; observabilidad real y alertas (TLS/backup/uptime/5xx/restarts/DB/disk); revisiones vigencia TLS y runtime .NET 8 EOS Nov-2026; capacidad/latencia bajo carga; smoke humano de negocio. No provisionar tenant ni operar SRI real hasta gates.
7. Codigo/PR de esta continuacion: trabajar exclusivamente DEV-531, una rama/escritor + review independiente y CI proporcional segun AGENTS.md. NO merge autonomo sin validacion especifica; NO DEV-532, NO claims de "PRODUCTION PILOT ACTIVE" mientras R4 no termine.

## 8. Evidencia ausente / que no afirmar

- Las salidas operativas aqui transcritas fueron recibidas en chat; **no** estan anexadas como logs forenses firmados ni existe un checksum de toda la sesion. El manifiesto y hashes reales viven fuera del repo. Codex debe solicitar probes de lectura minimamente necesarios al operador, no fingir acceso.
- No hay implementacion offsite diaria ni retencion aprobada, object lock probado, backup creado desde contenedor recovery, login de app restaurada real, test de egress del entorno recovery ni medicion RTO.
- No divulgar: private SSH / private age, secretos R2, JWT, passwords, env completo, dump, keyring, TLS private key, archivado host-config. Mantener las pruebas R4 en infraestructura humana, nunca fixtures de CI con datos reales.

**Estado de cierre:** Backup real offsite cifrado y descifrado externamente VALIDADO; workspace aislado PREPARADO; true restore real PENDIENTE; DEV-531 ABIERTO; DEV-532 NO INICIADO.
