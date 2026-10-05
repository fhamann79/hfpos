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
equivocado/vacio, ApplicationName y purpose incorrectos. Con `-Cleanup` destruye contenedores,
red, volumenes, cert y archivos runtime incluso al fallar; nunca sube dumps/keys
a GitHub artifacts. No contacta DB Development/Production ni SRI/SMTP reales.
Repetir ensayo periodico externo; frecuencia, alertas y hosting requieren decision humana.

La app de prueba/fakes residen en `Hfpos.FiscalSmokeHost`, imagen `pilot-proof:synthetic`
separada, sin referencia/publicacion desde la API o las tres imagenes de release.
Su HTTP permite solo SOAP sintetico in-process; SMTP es un fake in-process. Los
servicios restaurados no montan el volumen keyring original. Las claves, signing
fixture y TLS son exclusivamente sinteticos, nunca certificados/secretos reales.
