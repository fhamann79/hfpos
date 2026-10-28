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

### Custodia del Directorio Offline Nuevo

<!-- OFFLINE_NEW_DIRECTORY_CUSTODY_BEGIN -->
Responsable: Fernando o el operador humano expresamente designado, nunca agentes.
Este directorio sensible queda FUERA del cleanup del wrapper Windows: el CLI no
lo retira al terminar y un fallo/export parcial tambien puede dejar plaintext.
No existe un helper generico de retirada offline ni se debe invocar el cleanup
del wrapper sobre otra ruta. Mantener agentes/automatizaciones cerrados durante
TODO el siguiente procedimiento, tanto en exito como en fallo/interrupcion:

1. Antes del CLI, elegir UNA ruta absoluta NUEVA y unica dentro del parent local
   privado aprobado. Registrar la ruta exacta y parent canonicos; comprobar que
   no existen ni directorio ni reparse point en ese destino. Nunca adoptar un
   preexistente. El destino y su parent no pueden ser originals, backup/ciphertext,
   identity, USB/custodia, core restaurado, keyring, bundle de codigo o transfer,
   ni contenerlos. No copiar secretos al registro; solo identidad de ruta.
2. Al recuperar control del CLI, incluso tras error/export parcial, comprobar si
   esa ruta fue creada. Registrar dispositivo/inode con `os.lstat`, SID propietario
   y ruta/parent exactos, sin leer contenido. Contrastar atribucion a ESTA corrida,
   ausencia de reparse/symlinks en TODOS los ancestros y ACL privada del mismo SID
   (solo operador/SYSTEM). Si no se puede atribuir su creacion, STOP y contencion
   humana, sin borrar ni reabrir agentes. Si no se creo, confirmar ausencia.
3. Usar config solo offline si esa ventana lo autoriza. Si hace falta custodiarla,
   preparar una copia CIFRADA NUEVA mediante el procedimiento humano aprobado,
   verificar integridad/autenticacion y su destino protegido antes de retirar el
   plaintext. No sustituir ni borrar originales custodiados. Si la custodia o su
   verificacion no concluye, mantener agentes cerrados y resolver humanamente.
4. Antes de retirar el directorio NUEVO, volver a comprobar misma ruta/parent,
   dispositivo/inode/SID y ACL, no reparse/symlink, y TODOS sus miembros. Solo se
   admite un subconjunto de `host-config.tar`, `roles.sql`,
   `schema-ownership-grants.sql`, `source.json`, archivos regulares sin hardlinks,
   sin subdirectorios ni otros miembros. Un parcial puede carecer de source.json;
   no inventar autenticacion o custodia por ello. Identidad cambiada, miembro
   ajeno, link o permiso dudoso => STOP, sin borrado recursivo y contencion humana.
5. SOLO tras esas comprobaciones y custodia/retirada autorizadas, el humano puede
   retirar esa ruta literal NUEVA con `Remove-Item -LiteralPath $OfflineExact -Recurse`.
   `$OfflineExact` es el valor absoluto registrado y revalidado, no un glob, parent
   calculado ni otra raiz. No `-Force`, comodines, limpieza del parent, originales,
   backups historicos, USB, keyring o core. Si la retirada falla/parcial, no reabrir
   agentes: revalidar identidad y resolver/contener en la misma ventana humana.
6. Comprobar AUSENCIA del directorio exacto y de reparse point, conservacion de
   originales/ciphertext/custodia y retirada de cualquier otra copia plaintext
   NUEVA autorizada. Registrar solo estado `OFFLINE_NEW_DIRECTORY_REMOVED` o
   `OFFLINE_NOT_CREATED`; antes de reabrir agentes se requiere uno de esos estados
   o una decision humana explicita de contencion con aislamiento verificado.
   Sin prueba de retirada/contencion no declarar cleanup PASS ni borrado forense.
<!-- OFFLINE_NEW_DIRECTORY_CUSTODY_END -->

Exito/fallo retiran SOLO staging NUEVO de corrida, parent/owner/inode/realpaths y
arbol sin links comprobados, y stage PG generado. Identidad/limpieza no demostrable
=> FAIL/HUMAN_CONTAINMENT_REQUIRED, nunca borrado general/PASS. Preservar historicos,
ciphertext/manifiesto y ultimo exito. Diagnosticos enumerados, sin stderr/Env/SQL.
No borrado forense ni memoria cero global; root sigue siendo frontera humana.
Sin purga automatica. Check systemd cada10min: ultimo intento fallido o snapshot>=1h
no acredita RPO. Aviso externo/ack/escalacion sigue pendiente de decision.
El intento se registra ANTES del lock/mkdir: fallo de entrada o conflicto de lock
marca last-run failed, conserva last-success y hace fallar el check aun si la copia
anterior tiene menos de una hora. Solo se retira staging cuya creacion/identidad
fue comprobada; directorios de entrada vacios o ajenos no se adoptan ni eliminan.
Si STATE confiable no permite registrar el intento/fallo, el worker sale1 y falla
el servicio systemd; nunca informa exito ni sobrescribe last-success. El operador
debe atender ese error, no inferir exito del ultimo registro historico.
Pullback usa `rclone cat`: lectura streaming de como maximo longitud local esperada
mas UN byte por objeto; aborta/mata el lector al exceder, verifica longitud/hash
del ciphertext y bytes exactos del manifest, sin descargar objetos arbitrarios a
disco ni cargarlos completos en memoria. Timeout sigue siendo defensa adicional.

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
