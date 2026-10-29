# DEV-531: Respaldo Online y Recuperacion Compatible

Preparacion R3, no instalacion ni validacion real. Refs #159/#133. NO MERGE por
implementer. RPO **una hora** ya aprobado. RTO **4 horas como objetivo** y retencion
**30 dias** aprobados el 29-Oct-2026 en
[#159](https://github.com/fhamann79/hfpos/issues/159#issuecomment-6498542802).
Por instruccion posterior de Fernando, **correo diferido**: no dispone de servicio
SMTP y decide continuar con avisos locales de systemd. No se implementa correo,
configuracion SMTP ni otro canal externo; no presentar avisos externos como PASS.
El RTO completo no esta medido. No se introduce la propuesta historica 7/4/3.
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
La configuracion anterior conserva su comportamiento sin purga. La extension
opcional siguiente agrega retencion; no queda activada por merge. Check systemd
cada10min: ultimo intento fallido o snapshot>=1h no acredita RPO. Se conservan
avisos locales de systemd; Fernando difirio expresamente el correo por falta de
SMTP. No se inventa una ruta externa de escalacion ni aviso fuera del servidor.
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


## Retencion — Extension Acotada de #159

Riesgo R3: borrado privilegiado de copias propias. Correo fuera de este cambio.
No cambia DB, ventas, impuestos, SRI, keyring ni formato del paquete. La evidencia
humana de recuperacion RC6 y del primer ciclo programado auto-v2 se conserva en
[#133](https://github.com/fhamann79/hfpos/issues/133#issuecomment-6479950108) y
[#159](https://github.com/fhamann79/hfpos/issues/159#issuecomment-6481374533).
No repetir restore por esta extension. **No declarar DEV-531 cerrado por merge.**

La configuracion version2 anterior sigue admitida sin cambios. El operador puede
anadir SOLO esta clave opcional:

```json
{
  "retention": {"days": 30, "apply": false}
}
```

El primer valor `apply` es **false**: despues del backup se comprueba y muestra la
seleccion, sin borrar. `--retention-preview` hace la misma seleccion/verificacion
sin captura nueva ni cambio del historial. Hace lecturas R2 y temporales cifrados
propios retirados al salir; no descifra nada. `--retention-apply` exige ademas que
`retention.apply` sea true. Solo el humano activa ese valor despues de revisar la
seleccion y el alcance local/remoto. Con true, el backup normal aplica retencion
SOLO despues de una captura exitosa; el checker de 10min nunca borra copias.

### Seleccion y Fallos Parciales

- Base auto-v2 dedicada, root0700, igual a la configuracion aprobada. Nunca adoptar
  el directorio automatico auto-v1, sus conjuntos, respaldos predeploy/primera
  factura, restauraciones, otro bucket/prefijo ni originales offline. La retencion
  puede dejar copias durante mas de 30 dias ante fallo o ambiguedad: 30 dias es el
  umbral aprobado, no una promesa de borrar a un segundo exacto.
- Solo directorios UUIDhex32 con exactamente `recovery.age`, `manifest.json` y
  `INTEGRITY_OFFSITE_VERIFIED.json`, regulares root0600 y sin hardlinks/symlinks.
  Metadatos version2 concordantes, tamanos/hash del ciphertext comprobados y fechas
  de snapshot Y verificacion offsite anteriores a 30 dias completos. Un miembro adicional (por ejemplo un marcador
  humano `PRESERVE`) excluye el conjunto. Formatos viejos, ambiguos o ajenos se
  preservan y cuentan como `preserved_unknown`; requieren criterio humano aparte.
- Exige un ultimo exito fresco coherente con su conjunto local completo. El
  ultimo exito nunca se selecciona, aun si venciera. Una averia de backup impide
  purgar; no se borran copias para ocultar un problema de capacidad.
- Toma el MISMO lock de mantenimiento. Lista solamente el UUID remoto concreto,
  exige dos objetos exactos y autentica sus bytes mediante pull-back acotado antes
  de crear el journal y antes de reanudar un parcial. El listing se lee en streaming
  con limite 16KiB+1, deadline y retirada del proceso al exceder; nunca se captura
  entero antes de comprobar el limite. Nunca `purge`, `delete` de
  prefijo, `rmdirs` ni borrado recursivo local. Remoto exclusivamente escrito por
  este worker bajo el protocolo de lock; R2/rclone no hacen una transaccion global
  con otro escritor externo. Si hay otro escritor o se cambia lifecycle/object
  lock del bucket, mantener apply=false y reconciliar antes de habilitarlo.
- Antes del primer `deletefile` literal conserva un journal root0600 en
  `state/retention-pending/<id>.json`, ligado a base, remoto, inode y metadata.
  Cada reintento solo acepta objetos/archivos restantes autenticados; una respuesta
  de delete perdida permite continuar sin adoptar nuevos bytes. Un cambio o
  miembro nuevo detiene el conjunto. No se borra evidencia para fingir exito.
  Local solo unlink de los tres nombres y rmdir del UUID vacio. Journal completo
  pasa a `state/retention-history/AAAA-MM/`; no se aplica retencion a esta evidencia.
- Escaneo de metadatos acotados; hash completo solo del ultimo exito y de los
  conjuntos seleccionados (incluidos journals a reanudar). Maximo ocho conjuntos
  por ejecucion; limite de 8192 entradas locales y 256
  journals pendientes. Exceder limite => atencion humana/capacidad, sin borrar por
  globs. El mismo timeout de servicio limita backup+retencion, sin retrasar el
  checker. Si falta tiempo, los conjuntos restantes quedan para otro ciclo.
- Antes de aplicar registra `last-retention.json` running; si un timeout/muerte
  lo deja asi durante 20min, el checker alerta RETENTION_OVERRUN. Un fallo
  capturado registra failed y el checker alerta; no altera
  `last-success.json` ni convierte una captura sana en un falso fallo de captura.
  La siguiente retencion exitosa retira ese estado. Observar el resultado propio
  de mantenimiento en systemd. `--status` sigue siendo consulta sin efectos y
  ahora incluye failed/overrun del mantenimiento, sin cambiar el ultimo respaldo
  verificado. El checker existente conserva `--status`, sin nuevo canal externo.

### Supervision Local

El servicio checker conserva `--status` cada10min. Este comando no envia mensajes,
no inicia respaldos ni borra copias. Registra alerta local y salida1 si el ultimo
intento de backup fallo, el snapshot tiene edad >=1h, o retencion esta failed o
running fuera de 20min. Durante retencion running dentro de esa ventana con
backup fresco se mantiene el estado sano; no se confunde un mantenimiento en
curso con un fallo. La misma ventana y deadline abarcan backup+retencion.

Fernando es el responsable inicial. **No hay aviso fuera del VPS**: si nadie
consulta systemd o el servidor cae, esta supervision local no lo notifica por
correo ni constituye un monitor externo independiente. El correo fue diferido
expresamente por Fernando, no esta bloqueado por una tarea SMTP pendiente ni se
incluye en este PR. El cierre acotado debe registrar esa limitacion aceptada,
sin afirmar cumplido el criterio historico de notificaciones externas de #159.

### Entrega y Validacion Humana Minima

**VALIDACION HUMANA PRE-MERGE REQUERIDA** por AGENTS.md: aceptar esta seleccion
restrictiva, preservacion de historicos/ultimo exito, recuperacion de parciales y
limitacion de avisos exclusivamente locales. El gate consiste en revisar el procedimiento/diff y la
seleccion sintetica probada, **sin ejecutar R4 ni borrar reales antes del merge**.
La aprobacion de la politica ya esta registrada; no solicitarla de nuevo.

Tras CI y review, preparar una sola ventana humana acotada, con rollback a los
bytes/config/units actuales. Copiar codigo revisado a un destino root privado
NUEVO, conservar `source-hashes.json`, y reconciliar el instalador/units existentes
mediante snapshot; no sobrescribir ciegamente la instalacion en uso. El instalador
prepara tambien `operations.py`. Pausar solo timers no detiene oneshots activos:
esperar su fin y conservar ultimo exito/RPO, como en el relevo anterior. No tocar
web/backend/PG, las fuentes de capture, los antiguos respaldos ni sus historicos.

Pasos de aceptacion operacional, una vez instalado/configurado humanamente:

1. Con apply=false, ejecutar el worker nuevo con `--retention-preview`. Revisar
   `selected`, `preserved_unknown` y base/prefijo exactos. Si no hay vencidos,
   selected=[] es correcto; no inventar copias reales antiguas ni esperar30dias
   para repetir una prueba que ya tiene regresiones sinteticas.
2. Solo si la seleccion y las exclusiones coinciden, el humano cambia apply=true
   y activa las units revisadas. Observar UN ciclo programado y su checker local;
   no emitir ventas ni restaurar/descifrar otra vez para comprobar mantenimiento.
3. Ante error de seleccion, deshabilitar apply sin borrar journals/evidencia,
   conservando respaldo operativo y avisos locales. Si rollback de codigo,
   conservar estado/copias y
   restaurar las units/config previas sin reiniciar HF One. No confundir rollback
   del worker con restauracion de DB ni garantizar recuperar copias ya vencidas
   y retiradas bajo politica aprobada.

La retencion no queda acreditada como activa hasta esa evidencia humana.
El RTO4h es una meta de recuperacion completa, no una medicion obtenida de estos
checks. El coordinador coteja los criterios pendientes contra evidencias existentes
antes de decidir cierre de #159/#133; no abre auditoria general ni activa DEV-532.

## Evidencia Historica y Gate Ya Completado de PR #165

**HISTORICO: no es un nuevo gate de esta extension.** Los contratos/limites,
exclusion, separacion de configuracion y estados RC6 de PR #165 ya fueron
aceptados e integrados, y la recuperacion real posterior fue reportada PASS en
los enlaces anteriores. Se conserva este texto por su alcance original; no
solicitar otra aceptacion de PR #165 ni repetir su ensayo. El unico pre-merge
pendiente de este cambio es la retencion descrita en la seccion anterior.


Runners Containers existentes: legacy/auto-v1/v2 negativos, age tardio/timeout,
lock/cleanup/fallos con dobles sinteticos y E2E real dump PG + age + config/keyring
offline + nuevo restore + RC6 exacto. Mocks no acreditan R2 ni recuperacion real.

El gate historico exigia, tras CI y review: aceptar contratos,
limites, exclusion compartida, separacion config/transfer e identidad/estados
RC6 de [la guia app](human-restored-application-verification.md).
Aceptar procedimiento no ejecuta R4. Futura ventana real requiere autorizacion
expresa aparte y core compatible existente; no repetir backup/descifrado/transfer/
restore ya reportado ni ensayo previo por esta preparacion. Datos ausentes o
incompatibilidad => PARTIAL/STOP, sin sembrar datos ni fallback RC1. DEV-531 no DONE.
