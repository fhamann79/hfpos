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

1. Declarar incidente, registrar hora, detener escrituras/trafico.
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
8. Mantener `Sri__AllowProductionSubmission=false`. Sin replay SRI ni resend email.
9. Restaurar configuracion/secrets/TLS externos y arrancar una instancia de release exacta.
10. Comprobar ready, login platform/tenant humano, smoke de datos seguro y logs.
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
platform user/tenant sinteticos -> dump/checksum/list -> segunda DB vacia -> restore ->
schema y records sinteticos/source intacta -> keyring fixture snapshot/restore -> restart.
Pruebas negativas de aprobacion, checksum y destino no vacio. Destruye contenedores,
red, volumenes, cert y archivos runtime incluso al fallar; nunca sube dumps/keys
a GitHub artifacts. No contacta DB Development/Production ni SRI/SMTP reales.
Repetir ensayo periodico externo; frecuencia, alertas y hosting requieren decision humana.
