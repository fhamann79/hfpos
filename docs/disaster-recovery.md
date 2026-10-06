# BE-FE-520: recuperacion ante desastre

Runbook humano R4: NO ejecutar sobre entorno real desde agentes. Requiere incidente
declarado, aprobacion explicita, destino confirmado, backup verificado, rollback/restore
plan y ventana de cambio. CI/ensayo R3 solo DBs efimeras, datos/keys sinteticos.

## Conjunto consistente de recuperacion

A PostgreSQL backup; B keyring Data Protection; C secretos/config externa;
D release/digests exactos; E estrategia TLS del hosting. DB sola NO basta.
JWT key perdida invalida sesiones existentes, no datos. Keys DP perdidas pueden
impedir descifrar SMTP/certificados protegidos. Preservar ApplicationName/purposes.
Single-host puede usar keyring persistente local; varias replicas deben compartir
ApplicationName y keyring en storage/provider aprobado, no volumen distinto por replica.
No se implementa cloud KMS ni reencriptacion de datos en este ticket.

## Backups

Ejecutar tooling desde cliente PostgreSQL 16 en entorno autorizado; usar PGHOST,
PGPORT, PGDATABASE, PGUSER y PGPASSFILE/PGPASSWORD runtime. Nunca imprimir env ni URI.

```sh
sh scripts/ops/postgres-backup.sh /secure/backups
sh scripts/ops/verify-postgres-backup.sh /secure/backups/<artifact>.dump
sh scripts/ops/keyring-backup.sh /secure/keyring /secure/keyring-backups
```

pg_dump custom/no-owner/no-privileges, umask 077, temporal y rename, UTC, SHA256 y
manifest no secreto (version cliente/release/database identifier). No PII/passwords
en manifest. Verify comprueba checksum, consistencia y pg_restore --list, no basta
exit 0 del dump. No borrar backups antiguos automaticamente. Programar externamente;
fallo/edad de backup genera alerta externa, NO bloquea health/ventas.
Dump contiene negocio; keyring ES material secreto. Ambos cifrados en reposo, ACL
restrictiva, fuera de repo/artifacts publicos, almacenamiento preferiblemente versionado
e inmutable/object lock cuando hosting lo ofrezca, rol de backup separado, app sin
permiso de borrar backups. No implementar vendor-specific storage en esta baseline.
Daily/weekly/monthly son categorias posibles; retencion concreta requiere aprobacion.

## Offsite Cifrado Del Cohost

Preparacion Contabo cohost: un snapshot/backup en el mismo VPS NO sobrevive su
perdida. Antes del piloto, aprobar herramienta de cifrado de cliente autenticado,
destino offsite, owner/custodio y credencial restringida de upload. Ninguna cuenta,
clave ni integracion vendor real se crea por esta entrega. SSE remoto es adicional,
NO sustituto del cifrado cliente: la clave privada de descifrado OFFSITE y su recovery copy
permanecen fuera del VPS/repositorio/bucket, con acceso autorizado y custodia probada.
El VPS recibe solo material publico de cifrado si la herramienta lo permite.

Job externo al runtime, con lock para no solaparse y timeout/exitcode/alerta segura:

1. Crear conjunto consistente autorizado: dump PostgreSQL16, keyring completo,
   manifests/checksums, release/tres digests, snapshot de config/secret-store externo
   y registro de roles/grants. Detener escritores/rotacion de claves durante la
   ventana acordada; comprobar quiescence y no borrar claves DP antiguas. No inferir
   atomicidad entre dump/keyring/config simplemente porque cada archivo tenga hash.
2. Reusar backup + verify y keyring-backup existentes; no imprimir contenido. Staging
   fuera Git con umask077, ACL/encryption local restrictivos y espacio suficiente.
3. Cifrar el conjunto ANTES de upload con integridad autenticada. Config con secretos
   y keyring se incluyen dentro del ciphertext; manifest exterior solo IDs opacos,
   created UTC, tamano y checksum del ciphertext, sin PII/secretos ni rutas privadas.
4. Upload off-VPS mediante credencial de alcance minimo, preferentemente append-only,
   sin delete de app. Verificar almacenamiento remoto/checksum/tamano y conservar
   evidencia segura; un exit0 del upload no acredita descifrado/restore.
5. Pull-back de una copia REMOTA a un entorno de recovery distinto y autorizado,
   con custodia de clave externa. Verificar autenticidad/hash, descifrar en staging
   protegido, verificar dump/keyring y ensayar NUEVA DB/NUEVO keyring + app como abajo.
   Registrar edad, tiempos, logins/protected-data/negocio y fail-closed sin plaintext.
6. Solo tras verify remoto completo retirar staging local conforme politica aprobada.
   Retention cleanup remoto es job/rol separado, nunca delete amplio desde app ni
   prune global. Conservar recovery points previos hasta verificar el nuevo.

Propuesta NO aprobada: timer Linux diario y despues de cambios de release/config/keys,
pull-back periodico y tras cambios materiales, retention7daily/4weekly/3monthly.
Horario/timezone, frecuencia de prueba, cuotas, coste y owner pendientes; no instalar
cron/timer real desde agentes. Alertar fuera VPS por job fallido/timeout, falta de
copia remota, edad mayor que RPO aprobado, pull-back/decrypt/restore fallido y espacio.
Owner/contacto/ruta/ack/escalacion deben aprobarse y probarse; no alerta integrada
ni tarea humana tecnica ceremonial inventada. Agentes/CI cubren ensayos sinteticos,
el runbook real y su ejecucion requieren R4 separado.

## Procedimiento humano

1. Declarar incidente, registrar hora, detener escrituras/trafico y todas las replicas,
   workers y tareas externas que puedan escribir. Verificar procesos/conexiones del
   origen detenidos y ausencia de escritores en destino; no basta ocultar la UI.
2. Identificar release/digests y conjunto backup/keyring/config compatible.
3. Verificar checksum/listado y recuperar secretos fuera de logs/repositorio.
4. Provisionar NUEVA DB VACIA. No DROP DATABASE/SCHEMA ni --clean automaticos.
5. Confirmar PGDATABASE destino y aprobar restore expresamente:

```sh
HFPOS_RESTORE_APPROVED=YES sh scripts/ops/postgres-restore.sh /secure/backups/<artifact>.dump
sh scripts/ops/verify-restored-db.sh
HFPOS_RESTORE_APPROVED=YES sh scripts/ops/keyring-restore.sh /secure/keyring-backups/<artifact>.tar /secure/empty-keyring
```

6. Restore rechaza cualquier relacion de usuario existente, usa single-transaction,
   exit-on-error/no-owner/no-privileges y verifica schema critico/EF. No imprime filas.
   Antes de arrancar, contrastar contenido del recovery point y estado logico de
   secuencias: schema/name/last_value/is_called. Filas iguales no prueban siguiente
   insert; no ejecutar nextval/setval sobre origen para verificarlo. `log_cnt` es
   WAL/preallocation interno, no estado logico que deba ser igual entre bases.
7. Keyring restore comprueba checksum, archivos flat seguros y destino vacio; no
   sobreescribe. Ajustar propietario/ACL para UID del servicio, mismo ApplicationName.
8. ANTES de arrancar destino, el operador aplica deny-egress verificable para SRI
   TEST y Production, SMTP y otros efectos externos. Permitir solo DB destino y
   dependencias internas aprobadas. Verificar reglas y un probe negativo sin datos
   comerciales; conservar evidencia de denegacion, no credenciales ni payloads.
   Mantener `Sri__AllowProductionSubmission=false`, pero NO confundir ese flag con
   bloqueo de SRI TEST. Revocar delegacion tampoco detiene claims/leases/fences o
   intentos locales del worker. No hay switch nuevo que pause el worker.
9. Restaurar configuracion/secrets/TLS externos y arrancar UNA instancia de release
   exacta, conectada explicitamente a NUEVA DB y NUEVO keyring restaurados, nunca
   al origen. Bootstrap/seed deshabilitados. Preservar ApplicationName y purposes.
10. Comprobar ready, login platform/tenant humano, descifrado de un valor protegido
    por un mecanismo seguro aprobado y datos de negocio. Ready por si solo NO
    acredita las claves. No imprimir plaintext/keyring. Contrastar totales,
    documentos, caja, stock, compras, refunds y settlements con el recovery point.
    Inventariar jobs fiscales pendientes/leases e intentos antes/despues del
    arranque: un worker puede reclamar trabajo local aunque deny-egress bloquee
    efectos externos. No afirmar destino intacto si existen esos cambios.
11. Autorizar trafico y habilitar integraciones SOLO con aprobacion humana posterior.
12. Registrar tiempos, backup recovery point, errores, decisiones y resultado.

Preferir forward fix si app/schema no compatibles; restaurar puede perder nuevas
escrituras. No automatizar EF Down ni restaurar encima del entorno original.

## Deny-Egress Linux Docker

ANTES de startup del destino, confirmar Engine/firewall backend y redes/IPs reales
de TODAS las interfaces del backend restaurado. Con Docker bridge no basta una
regla UFW/host OUTPUT: Docker puede enrutar trafico publicado antes de esas cadenas.
No deshabilitar las reglas internas de Docker para simular aislamiento.
[Docker filtering](https://docs.docker.com/engine/network/packet-filtering-firewalls/).

Con backend iptables, politica propia en DOCKER-USER antes de accepts de Docker,
no append tardio a FORWARD; registrar prioridades y counters.
[Docker iptables](https://docs.docker.com/engine/network/firewall-iptables/).
Con backend nftables, tabla/base-chain propia con hook/priority apropiados y reglas
para las redes restauradas: no asumir que DOCKER-USER existe ni editar tablas
gestionadas por Docker. [Docker nftables](https://docs.docker.com/engine/network/firewall-nftables/).
No se infiere backend/version: coordinador/reviewer deben preparar y validar las
reglas exactas al conocer el host; autorizacion/ejecucion R4 son gates humanos.

Allowlist minima: DB DESTINO/puerto explicitos, DNS interno necesario y respuestas
al proxy aprobado; deny toda otra salida IPv4/IPv6 de ambas redes/interfaces.
No whitelist amplia443/SMTP ni cualquier ESTABLISHED que preserve conexiones viejas:
quiesce/cerrar flujos origen y comprobar conexiones activas antes de startup.
Guardar reglas persistentes, verificar tras reboot/recreate/red/IP cambiados, probar
con probe sintetico desde el namespace/red destino que DB esta permitida y SRI TEST,
Production/SMTP negados; evidencia booleana/counters sin payloads, certs o secretos.
No intentar ningun submission/email real para probar firewall.
Egress negado no evita claims/leases/fences/escrituras locales del worker. Arrancar
solo UNA app sobre destino inequivocamente nuevo y registrar jobs/estado antes y
despues; no habilitar trafico/integraciones hasta aprobacion humana separada.

Restore --no-owner/--no-privileges requiere recrear roles, ownership y grants/default
privileges: runtime no-superuser separado de migrator/admin. Preflight y futura DB
externa con hostname/CA/TLS estan en [piloto cohost](pilot-hosting-runbook.md).
Con escrituras nuevas tras cutover, rollback no es cambiar a origen viejo: detener
el unico writer, medir divergence y decidir forward fix/reconciliacion/perdida.

## RPO y RTO

RPO target: REQUIERE APROBACION HUMANA (dato maximo que se acepta perder).
RTO target: REQUIERE APROBACION HUMANA (tiempo maximo fuera de servicio).
No prometer SLA sin medir/aprobar. Medir edad del recovery point e intervalo
incidente->servicio restaurado durante ensayos. CI imprime duracion del ensayo,
pero esa duracion NO equivale a RTO Production ni acredita capacidad/tamano reales.

## Ensayo automatico

`start-deployment-smoke.ps1 -Cleanup`: DB efimera hfpos_ops_ci -> migrations todas ->
platform user/tenant sinteticos -> recorrido unico de negocio con jobs terminales ->
dump/checksum/list y snapshot keyring -> nueva
DB y keyring restaurados -> backend/proxy separados y nueva app Production de
prueba usando ambos -> login platform/tenant, descifrado SMTP/certificado sinteticos,
costos historicos 3/3/7 frente a costo actual 4, snapshots/XML y negocio.
Se verifica origen intacto durante recovery, no se promete destino inmutable con
trabajo pendiente. Negativos: aprobacion, checksum, destino no vacio, keyring
equivocado/vacio, ApplicationName y purpose incorrectos; last_value-only e
is_called-only en tercera DB desechable, con filas intactas y source readonly.
No se mutan secuencias de source ni restore principal. Con `-Cleanup` destruye contenedores,
red, volumenes, cert y archivos runtime incluso al fallar; nunca sube dumps/keys
a GitHub artifacts. No contacta DB Development/Production ni SRI/SMTP reales.
Repetir ensayo periodico externo; frecuencia, alertas y hosting requieren decision humana.

La app de prueba/fakes residen en `Hfpos.FiscalSmokeHost`, imagen `pilot-proof:synthetic`
separada, sin referencia/publicacion desde la API o las tres imagenes de release.
Su HTTP permite solo SOAP sintetico in-process; SMTP es un fake in-process. Los
servicios restaurados no montan el volumen keyring original. Las claves, signing
fixture y TLS son exclusivamente sinteticos, nunca certificados/secretos reales.
