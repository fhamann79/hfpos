# DEV-531: Respaldo Online y Recuperacion Compatible

Preparacion R3, no instalacion ni validacion real. Refs #159/#133. NO MERGE por
implementer. RPO **una hora** ya aprobado. RTO, retencion/capacidad, avisos externos,
responsable y periodicidad de ensayo siguen pendientes; no asumir 4h ni 7/4/3.
DEV-531 sigue abierto; DEV-532/535 no activos.

## Procedencia

Entrada local leida/decodificada, nunca ejecutada: instalador39947bytes SHA256
`e483b0d5d9e10e063c4e82747118ea08da0e1fcc2235836cf10213b09c749a0b`.
Los siete hashes estan en `scripts/ops/hourly-backup/installed-provenance.json`;
worker original `188824b320cde2f464bf0f8e89f4924d920baa2db63a31a0f4676af493e6bb29`.
Los helpers PG son byte-identicos a main LF y se reutilizan, sin copias nuevas.
Esto identifica SOLO el archivo recibido, **no la instalacion remota actual**.

Worker, install y units de `scripts/ops/hourly-backup/` son la propuesta legible
auto-v2, NO byte-identica a la instalada. Cambian configuracion externa, formato,
limites y retirada de staging tambien al fallar. No se versionan IP/config/secretos
operacionales del instalador original. `source-hashes.json` identifica los bytes
propuestos preparados. El nuevo instalador SOLO prepara: no inicia servicios,
timers, backup ni reemplaza units existentes. No se ha instalado/activado aqui.

## Contratos

| Fuente | Identificacion | Adaptacion |
| --- | --- | --- |
| Historica | `metadata/internal-sha256.txt`, keyring timestamp | Valida TODO; filtra metadata extra y snapshot |
| auto-v1 instalada | `internal-sha256.txt` raiz, `keyring/keyring.tar`, captura/roles/schema/inspect/snapshot conocidos | Normaliza keyring al timestamp/ID del dump despues de validar TODO |
| auto-v2 propuesta | `metadata/format.json` exacto `{"format":"hfpos-auto","version":2}`, manifiesto en metadata, keyring timestamp | Version explicita, sin fallback ni heuristicas |

No reescribir historicos. Rechazar manifiestos/dumps/keyrings ambiguos, version
desconocida, hashes duplicados/incompletos, miembros duplicados, links, traversal,
PAX/sparse inesperado o limites excedidos. El hash del ciphertext ORIGINAL y el
Recovery ID permanecen en `transfer.json` version1; contrato core sin cambios.
Los checksums internos se regeneran sobre bytes canonicos filtrados; no se
presentan como hashes del paquete original. Validar tambien lo que NO se exporta.

Limites: archivo64MiB; TAR/total128MiB; 128 miembros de paquete; keyring4MiB,
XML1MiB, 128 miembros. Host-config64MiB/4096 archivos regulares relativos sin links.
Superar limites => STOP y dimensionamiento humano, nunca truncado silencioso.
Worker1100s, dump<=600s con margen antes del timeout externo, systemd20min.
Age decrypt tiene watchdog120s completo, incluida interaccion humana. Timeout o
fallo tardio de autenticacion nunca publica transfer/config; no retry automatico.

## Preparacion Humana Futura

SOLO tras review/CI exactos y autorizacion R4 separada. NO ejecutar desde agentes.
`/etc/hf-one-backup.json` root0600 admite exactamente estas claves sin secrets inline:

```json
{
  "version": 2,
  "base": "/BACKUP_APROBADO/automatic",
  "state": "/ESTADO_APROBADO",
  "keys": "/KEYRING_APROBADO",
  "sources": {"config": "/CONFIG_APROBADA", "tls": "/TLS_APROBADO"},
  "recipient": "/RECIPIENT_APROBADO.pub",
  "rclone_config": "/RCLONE_APROBADO.conf",
  "bind_ip": "IP_GLOBAL_APROBADA",
  "remote": "REMOTE_APROBADO:BUCKET_APROBADO/recovery",
  "postgres": "PG_APROBADO",
  "containers": ["BACKEND_APROBADO", "WEB_APROBADA", "PG_APROBADO"],
  "maintenance_lock": "/run/lock/hf-one-manual-backup.lock"
}
```

Plantilla invalida hasta sustitucion HUMANA. Base/state root0700,
recipient/rclone y config operador root0600; ancestros externos root sin escritura
grupo/otros, sin links. Keyring conserva UID/GID1654 del servicio; TLS conserva
UID/GID101 de web (tambien se admiten archivos root en esas raices). Solo dentro
de cada raiz fuente se permite ese owner explicito, no UID arbitrario ni escritura
grupo/otros. Captura compara identidad/owner/grupo/modo antes/despues, sin chown ni
chmod de fuentes. Las raices de limpieza siempre son root, nunca las fuentes.
Fuentes disjuntas entre si y respecto de base/state; secrets fuera de limpieza.
Copiar codigo revisado primero a snapshot root privado; ejecutar humanamente
`python3 -I -B .../hourly-backup/install.py --prepare-only` prepara destino NUEVO
`/usr/local/lib/hf-one-hourly-backup-v2`, nunca pisa otro. Units existentes => STOP:
operador reconcilia hashes, guarda version anterior y plan rollback bajo permiso
separado. No asumir reemplazo aprobado. Validar config, PG16/age/rclone/docker,
espacio, R2 y exclusion compartida antes de primera ejecucion/activacion.
Primer backup humano verificado antes de decidir activar timers; no se activa aqui.

## Exclusion de Mantenimiento

Lock existente `/run/lock/hf-one-manual-backup.lock`, root0600, inode estable.
Se crea exclusivo SOLO si falta; **nunca unlink/truncate/recrear**. Worker exige
existencia y toma `LOCK_EX|LOCK_NB` ANTES de capturar. El operador mantiene el MISMO
flock exclusivo durante TODO deploy/migracion/corte de writers/restore concurrente,
incluidos preparacion y controles finales. Parar timer no para un job ya vivo.
Preparacion contractual: comando humano aprobado como hijo de
`flock -n /run/lock/hf-one-manual-backup.lock`, manteniendo descriptor entre pasos.
Si un flujo no puede cumplirlo, STOP a auto-v2 para esa ventana y coordinar primero.
No se introduce un framework global que fuerce todos los deploys.

Conflicto => no comienza captura. Comparaciones before/after NO sustituyen lock.
Dump transaccional PG; keys-before subconjunto identico de keys-after (rotacion
solo agrega). Config/containers/schema/mapa roles sin passwords estables. Roles
`pg_dumpall --roles-only --no-role-passwords` y schema ownership/grants son mapa
HUMANO de rebuild, nunca SQL ejecutado contra ensayo ni restauracion de roles o
passwords productivos. Core conserva roles aislados propios.

## Configuracion Offline y Retirada

Snapshot host-config contiene config/TLS/recipient/rclone SOLO dentro de age.
Raw `containers.private.json` Env: hash validado, NUNCA transferido. Transfer SOLO
DB/keyring/metadata allowlist release/capture/manifiesto canonico; sin SQL roles/schema.
Nunca importar config real a la app aislada.

Opcion humana adicional `supervised-archive.py --offline-config-output RUTA_NUEVA`
separada del `--output` transfer: auto-v1/v2, autenticacion age y TODOS hashes/members
antes de escribir, ACL privada. Emite host-config TAR, mapas roles/schema y
source.json ligado a ciphertext. No extrae, ejecuta, instala ni transmite snapshot.
Abrir solo offline en ventana humana autorizada con agentes cerrados; nunca
contenido en chat/logs/env del probe. Windows normal sigue sin exportar snapshot.
El wrapper `human-recovery-windows.py` no solicita esta opcion. El CLI sellado
`supervised-archive.py` la expone junto a sus argumentos existentes de ciphertext,
identity, staging, output, recovery-id y ciphertext-sha256. Elegirlo al planear UNA
exportacion futura autorizada permite producir transfer y snapshot en el mismo
descifrado; no se instruye repetir el descifrado del core ya restaurado. No hay
restore automatico de config ni evidencia de restore real de config en este PR.

Exito/fallo retiran SOLO staging NUEVO de corrida, parent/owner/inode/realpaths y
arbol sin links comprobados, y stage PG generado. Identidad/limpieza no demostrable
=> FAIL/HUMAN_CONTAINMENT_REQUIRED, nunca borrado general/PASS. Preservar historicos,
ciphertext/manifiesto y ultimo exito. Diagnosticos enumerados, sin stderr/Env/SQL.
No borrado forense ni memoria cero global; root sigue siendo frontera humana.
Sin purga automatica. Check systemd cada10min: ultimo intento fallido o snapshot>=1h
no acredita RPO. Aviso externo/ack/escalacion sigue pendiente de decision.

## Evidencia y Gate

Runners Containers existentes: legacy/auto-v1/v2 negativos, age tardio/timeout,
lock/cleanup/fallos con dobles sinteticos y E2E real dump PG + age + config/keyring
offline + nuevo restore + RC6 exacto. Mocks no acreditan R2 ni recuperacion real.

**VALIDACION HUMANA PRE-MERGE REQUERIDA**, tras CI y review: aceptar contratos,
limites, exclusion compartida, separacion config/transfer e identidad/estados
RC6 de [la guia app](human-restored-application-verification.md).
Aceptar procedimiento no ejecuta R4. Futura ventana real requiere autorizacion
expresa aparte y core compatible existente; no repetir backup/descifrado/transfer/
restore ya reportado ni ensayo previo por esta preparacion. Datos ausentes o
incompatibilidad => PARTIAL/STOP, sin sembrar datos ni fallback RC1. DEV-531 no DONE.
