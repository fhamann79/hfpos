# DEV-531: Restore Humano Aislado

**PENDIENTE. R3 codigo; VALIDACION HUMANA PRE-MERGE REQUERIDA sobre el codigo
final exacto. NO merge por implementer. La ejecucion real es R4 humana separada,
todavia NO autorizada por esta guia. APPBLOCKED. NO DEV-532.**

Base de preparacion: main `5f6f527127028d45f8076bc6535b8a1518cc4f51`, issue #133.
No cambios de app/backend/frontend/EF, migraciones, API, cuentas, auth, servicios,
instalaciones, firewall, produccion ni receptor firmado. No `serve`/`restore_core`.
Esta entrada manual con `sudo` normal NO recibe privilegios del canal supervisado,
NO instala sudoers ni forced commands y NO es un broker sin firma.

## Gates y Alcance

Antes de usar los bloques: coordinador publica/fija el SHA final; reviewer independiente
revisa exactamente ese codigo; Windows nativo y E2E PostgreSQL/keyring CI pasan en
ese HEAD; Fernando acepta la validacion pre-merge. Usar luego SHA integrado y aceptado,
no el SHA historico de base. Ni CI ni merge autorizan R4. Registrar por separado una
ventana humana finita que incluya exportacion local, transferencia, restore aislado y
**retirada del staging Windows NUEVO incluso ante fallo**. No inferirla de aprobaciones
historicas. Sin app start, login, secretos de config/TLS, SRI, SMTP ni trafico.

Cerrar Codex, chats, agentes, automatizaciones y herramientas con acceso al mismo
principal/admin ANTES de conectar USB/descifrar. ACL del mismo usuario/admin NO es
frontera de aislamiento. Si no puede demostrarse esta ventana humana, STOP. No iniciar
otro executor, servicio o usuario. Python3, age, OpenSSH y Docker deben existir ya;
si falta alguno, STOP, sin instalar. No dar claves/passwords al chat.

Elegir padre privado **existente** NTFS en disco fijo, owner SID humano y Allow SOLO
humano/SYSTEM, sin reparse points en ningun ancestro. No usar raiz del perfil con
Administrators/Users/Everyone permitidos, carpeta sincronizada/compartida ni USB para
plaintext. El helper verifica ACL/owner; no modifica ni adopta ese padre. No promete
borrado forense: retirar archivos no elimina copias de memoria/pagefile/antivirus.

Custodia USB cifrada y backup cifrado se conservan originales y nunca se eliminan.
Identidad age **CIFRADA** se copia como ciphertext al staging privado; age la usa
directamente y solicita su passphrase en consola nativa. Nunca exportar una identidad
privada sin cifrar. Password SSH/sudo se escribe solo en sus prompts nativos.

## 1. Preparar Codigo No Secreto

Este bloque prepara SOLO codigo desde checkout del SHA integrado, limpio/revisado.
No conecta USB ni lee backups/keys. El `seal` verifica todos los helpers contra Git,
normaliza LF y produce bundle no secreto. La ruta de salida debe ser NUEVA.

```powershell
$Sha = Read-Host 'SHA integrado y aceptado (40 hex)'
$Bundle = Read-Host 'Ruta NUEVA para bundle de codigo no secreto'
python -I scripts/ops/human-recovery.py seal --source-sha $Sha --output $Bundle
if ($LASTEXITCODE -ne 0) { throw 'STOP seal' }
$CodeHash = Read-Host 'Copiar CODE_SHA256 mostrado (64 hex)'
```

Transferir ese directorio **solo codigo** al home del usuario SSH habitual mediante
su canal existente. No modificar auth/config del servidor ni abrir nuevo acceso.
Preferir el metodo SSH habitual: password/keyboard-interactive si YA esta permitido;
si usa archivo de clave existente, pasarlo expresamente a OpenSSH, no leerlo en Python
ni desproteger otra llave. Ambos exigen known_hosts previamente fijado/verificado fuera
de banda, sin aceptar hosts nuevos ni `ssh-keyscan` como prueba de identidad.

```powershell
$HostSsh = Read-Host 'usuario@host habitual'
$KnownHosts = Read-Host 'Archivo known_hosts con pin YA verificado'
$Port = [int](Read-Host 'Puerto SSH habitual')
$TransportKey = Read-Host 'Ruta clave SSH habitual o vacio para password existente'
$SshOpt = @('-F','none','-o','StrictHostKeyChecking=yes','-o',"UserKnownHostsFile=$KnownHosts",
  '-o','GlobalKnownHostsFile=none','-o','UpdateHostKeys=no','-o','IdentityAgent=none',
  '-o','ForwardAgent=no','-o','ClearAllForwardings=yes','-o','IdentitiesOnly=yes')
if ($TransportKey) { $SshOpt += @('-i',$TransportKey) }
else { $SshOpt += @('-o','PubkeyAuthentication=no','-o','PreferredAuthentications=password,keyboard-interactive') }
# El nombre remoto es nuevo, sin sobrescribir un directorio previo.
$CodeName = 'hfpos-human-code-' + $Sha
ssh @SshOpt -p $Port -T $HostSsh "test ! -e '$CodeName' && mkdir -m 700 '$CodeName'"
if ($LASTEXITCODE -ne 0) { throw 'STOP code collision/access' }
scp @SshOpt -P $Port -r "$Bundle/*" "${HostSsh}:$CodeName/"
if ($LASTEXITCODE -ne 0) { throw 'STOP partial code; no retry' }
ssh @SshOpt -p $Port -t $HostSsh
```

No streaming de TAR por stdin mezclado con prompt sudo. La transferencia de datos usa
SCP separado; `sudo` posterior es humano en la consola Linux habitual. SSH/SCP stderr
va solo a consola humana para prompts nativos; stdout de protocolo se captura. No
publicar esa consola. Timeout SSH90s/SCP120s: cancelar implica STOP y finally local,
no retry ni certeza de ausencia de efectos remotos. No crear claves
ni cambiar sshd si el metodo/pin falla. Code bundle parcial: inspeccion humana, no
sobrescritura/retry automatico. No instalar el launcher/broker legado.

## 2. Linux: Codigo Privado y Preflight Humano

En la consola SSH habitual, introducir SHA/hash mostrados en Windows. El usuario no
root debe tener HOME sin escritura de grupo/otros. El bundle root es NUEVO y temporal,
no una instalacion/servicio. Copia solo archivos regulares concretos y rechaza links;
no ejecuta Python root desde el home mutable. `python3 -I` evita imports de cwd/PYTHONPATH.

```sh
read -r -p 'SHA integrado aceptado: ' SHA
read -r -p 'CODE_SHA256 aprobado: ' CODE_HASH
case "$SHA" in *[!a-f0-9]*|'') echo STOP; exit 1;; esac
test "${#SHA}" = 40 || exit 1
case "$CODE_HASH" in *[!a-f0-9]*|'') echo STOP; exit 1;; esac
test "${#CODE_HASH}" = 64 || exit 1
USER_CODE="$HOME/hfpos-human-code-$SHA"
ROOT_CODE="/root/hfpos-human-code-$SHA"
# HUMAN_ROOT_SNAPSHOT_BEGIN: bloque stdlib probado extrayendolo de esta guia.
sudo python3 -I - "$USER_CODE" "$ROOT_CODE" "$SHA" "$CODE_HASH" <<'PY'
import hashlib, json, os, re, stat, sys
from pathlib import Path
def require(value):
    if not value:
        raise ValueError("STOP")
def unique(items):
    result = {}
    for key, value in items:
        require(key not in result)
        result[key] = value
    return result
try:
    source, target, sha, expected = sys.argv[1:]
    require(os.geteuid() == 0)
    require(re.fullmatch(r"[a-f0-9]{40}", sha) and re.fullmatch(r"[a-f0-9]{64}", expected))
    target = Path(target)
    require(target == Path("/root") / ("hfpos-human-code-" + sha))
    require(not target.exists() and not target.is_symlink())
    parent = target.parent.lstat()
    require(parent.st_uid == 0 and stat.S_ISDIR(parent.st_mode) and not parent.st_mode & 0o022)
    source = Path(source)
    require(source.is_absolute())
    # Anchor every directory with NOFOLLOW; snapshot regular bounded files by fd.
    descriptors = [os.open("/", os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)]
    try:
        for component in source.parts[1:]:
            fd = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=descriptors[-1])
            descriptors.append(fd)
            require(not os.fstat(fd).st_mode & 0o022)
        names = ("human-recovery.py", "human-recovery-windows.py", "human-recovery-transfer.py", "human-recovery-local.ps1",
                 "supervised-operations.py", "supervised-archive.py", "supervised-local-acl.ps1", "postgres-restore.sh",
                 "verify-postgres-backup.sh", "verify-restored-db.sh", "keyring-restore.sh")
        require(set(os.listdir(descriptors[-1])) == set(names) | {"source.json", "docker-empty"})
        snapshot = {}
        for name in (*names, "source.json"):
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=descriptors[-1])
            with os.fdopen(fd, "rb") as stream:
                require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode))
                data = stream.read(512 * 1024 + 1)
                require(len(data) <= 512 * 1024)
                snapshot[name] = data.replace(b"\r\n", b"\n")
        fd = os.open("docker-empty", os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=descriptors[-1])
        try:
            require(not os.listdir(fd))
        finally:
            os.close(fd)
    finally:
        for fd in reversed(descriptors):
            os.close(fd)
    digest = hashlib.sha256()
    for name in names:
        digest.update(name.encode("ascii") + b"\0" + hashlib.sha256(snapshot[name]).digest())
    require(digest.hexdigest() == expected)
    require(json.loads(snapshot["source.json"], object_pairs_hook=unique) == {"source_sha": sha, "code_sha256": expected})
    # No target write before ALL hashes/schema/paths/collision checks passed.
    os.umask(0o077)
    target.mkdir(mode=0o700)
    for name, data in snapshot.items():
        fd = os.open(target / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, "wb") as output:
            output.write(data)
            output.flush()
            os.fsync(output.fileno())
    (target / "docker-empty").mkdir(mode=0o700)
    print("ROOT_CODE_SNAPSHOT_PASS")
except (Exception, KeyboardInterrupt):
    print("ROOT_CODE_STOP_NO_RETRY")
    sys.exit(1)
PY
# HUMAN_ROOT_SNAPSHOT_END
test "$?" = 0 || exit 1
```

Exigir `ROOT_CODE_SNAPSHOT_PASS`. Este bloque inline stdlib, revisado como parte del
runbook, verifica los bytes LF contra CODE_HASH externo y source.json de shape exacto
antes de escribir. No importa/ejecuta siblings para comprobar el primer codigo root.
No requiere comparar 12 hashes manualmente. Error previo a verificacion: ningun write;
error de disco posterior: bundle parcial preservado, STOP, nunca ejecutar/reintentar.
La copia forma parte de la ventana autorizada; manifest autoeditado NO da autoridad.

El workspace existente reportado por el operador es
`/srv/hf-one/pilot/recovery-r4/68eedc0f1cef4dfcbed48189ece3b9ce`, root:root/0700,
con `incoming`, `postgres-data`, `keyring-restored` vacios. No recrearlo ni limpiar.
Estos son checkpoints historicos, no confirmacion live por el agente. El preflight
revalida ancestros/owner/modos/vacio/collision y falla si ya hay estado parcial.

```sh
ID=68eedc0f1cef4dfcbed48189ece3b9ce
CIPHER_SHA=6188f0a2f60dd648fe6ccfb030cfcb4493dd5fc4388a87fef52b73579e616d89
sudo python3 -I "$ROOT_CODE/human-recovery.py" preflight --source-sha "$SHA" \
  --code-sha256 "$CODE_HASH" --recovery-id "$ID" --ciphertext-sha256 "$CIPHER_SHA"
```

Esperado: `PREFLIGHT_PASS <token no secreto>`. Copiar el token a Windows. Imagen fija
PG16.15-bookworm digest `0ea6700a...c7319c4`, linux/amd64, entrypoint/Cmd, UID/GID999
efectivos y passwd comprobados en contenedor efimero `--pull=never --network none`.
Si imagen falta, STOP **antes de descifrar**; no pulls/instalaciones host. No consulta
SQL al origen. No hay lectura de config/secrets/certs. Docker usa socket Unix fijo,
config vacia root/0700 y env minimo; ignora DOCKER_HOST/contextos/config global.
Como evidencia opcional de produccion, solo resumen `docker ps` de lectura, sin SQL.

## 3. Windows: Una Entrada Local y Transferencia

Solo despues de la autorizacion R4 de esta ventana y cierre efectivo de agentes,
conectar USB cifrada, elegir identidad cifrada y copia de backup cifrado. El ciphertext
historico tiene 307464 bytes; el hash fijo es la comprobacion autoritativa. No copiar
snapshot/config/TLS en el paquete de transferencia. No repetir backup/custodia.

```powershell
$Token = Read-Host 'Token PREFLIGHT_PASS (puede incluir prefijo)'
$PrivateParent = Read-Host 'Padre privado existente NTFS fijo, humano/SYSTEM'
$EncryptedIdentity = Read-Host 'Archivo identidad age CIFRADA en USB'
$Ciphertext = Read-Host 'Copia cifrada del backup age'
$RemoteCode = Read-Host 'Ruta absoluta del bundle NO-root en HOME Linux'
$ArgsHuman = @('--token',$Token,'--private-parent',$PrivateParent,
  '--encrypted-identity',$EncryptedIdentity,'--ciphertext',$Ciphertext,
  '--host',$HostSsh,'--known-hosts',$KnownHosts,'--port',"$Port",'--remote-code',$RemoteCode)
if ($TransportKey) { $ArgsHuman += @('--ssh-key',$TransportKey) }
python -I scripts/ops/human-recovery-windows.py @ArgsHuman
if ($LASTEXITCODE -ne 0) { throw 'STOP: no repetir; comprobar retirada local y parcial remoto' }
```

Pide `VENTANA HUMANA SIN AGENTES`. Age obtiene passphrase por consola nativa; no usar
variables/env/passphrase en argumentos ni logs. El helper interno autentica TODO antes
de exportar; su TAR completo (incluye snapshot) es temporal privado y se retira. Export
es solo db/keyring/metadata+manifest nuevo, hashes exactos, TAR allowlisted sin links,
PAX, traversal, duplicados ni snapshot. No se descifra la identidad a un archivo.

SSH prepara spool NUEVO `~/human-recovery-<nonce>` 0700 humano, copia solo payload y
receipt por SCP, comprueba hash/manifest/archivo exacto. No sobrescribe incoming ni
autoriza root. Se imprime `TRANSFER_VERIFIED PAYLOAD_SHA256=<hash>; Linux --spool ~/human-recovery-<nonce>`.
Guardar ese nombre opaco, no datos. Password SSH puede solicitarse por cada comando;
no cachear/almacenar passwords ni habilitar agentes para reducir prompts.

Exigir `WINDOWS_OWNED_STAGING_REMOVED` tanto al exito como al fallo: finally comprueba
journal de creacion, inode/device, nombre/parent exactos, owner/ACL y todos los reparse
points; borra literalmente SOLO carpeta NUEVA y todo output/payload/temp/copies dentro.
Si falta ese PASS, no reabrir agentes. No borrar originales USB ni cipherbackup.

## 4. Linux: Restore Manual Una Sola Vez

En la consola SSH abierta de la fase2, usar el nonce impreso, sin concatenar contrasenas.

```sh
read -r -p 'Nonce opaco del spool verificado (32 hex): ' NONCE
case "$NONCE" in *[!a-f0-9]*|'') echo STOP; exit 1;; esac
test "${#NONCE}" = 32 || exit 1
read -r -p 'PAYLOAD_SHA256 mostrado en Windows: ' PAYLOAD_HASH
sudo python3 -I "$ROOT_CODE/human-recovery.py" restore --spool "$HOME/human-recovery-$NONCE" --payload-sha256 "$PAYLOAD_HASH"
```

Pide `RESTAURAR <ID>` tras snapshot de bytes/hash/receipt validado; root revalida
source/hash, workspace inode/device, imagen, vacio y nombre de contenedor bajo lock
exclusivo del directorio. Checkpoint durable antes de import/create/SQL/keyring;
cualquier segundo intento/parcial STOP, sin resume automatico, delete o SQL retry.
Incoming requiere vacio, no sobrescribe ni siquiera archivos identicos.

PG destino `hfpos-human-recovery-<ID>`, nombre distinto del legado firmado, none,
sin puertos/binds/red host, read-only, ALL caps drop/no-new-privileges, limites fijos,
tmpfs PG/socket/tmp, UID999. `postgres-data` host sigue vacio. Trust solo en namespace
none/loopback, sin passwords nuevos; no app ni peer. Inspect ID/imagen/labels/recursos
y probes cliente PG IPv4/IPv6 positivos loopback y negativos RESERVED, antes de SQL
y despues. IPv6 no operativo se clasifica, no se declara probe positivo inexistente.

Restore usa scripts oficiales, owner no-super distinto de runtime, no-owner/no-privileges,
single-transaction/exit-on-error, destino vacio, sin --clean. Grants tablas DML,
secuencias USAGE/SELECT sin UPDATE, defaults para objetos futuros; runtime LOGIN,
NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOREPLICATION/NOBYPASSRLS, sin CREATE schema.
Schema critico/EF y keyring byte-a-byte verificados; no cambiar ApplicationName/purposes.
No se afirma igualdad con produccion actual: secuencias/datos provienen del dump
autenticado, baseline comparable solo en fixture sintetica. No consultar origen real.

Esperado: `CORE_RESTORE_PASS_APP_BLOCKED`. Verificacion posterior, solo lectura:

```sh
sudo python3 -I "$ROOT_CODE/human-recovery.py" verify --recovery-id "$ID"
```

Esperado `CORE_VERIFY_PASS_APP_BLOCKED`. No declara true app recovery, login/DP,
negocio, servicio restaurado ni piloto activo. El destino tmpfs NO sobrevive stop/reboot;
no detenerlo para "limpiar" ni iniciar backend/proxy. Spool/incoming/keyring/DB y
checkpoint remotos se preservan aun ante fallo para investigacion humana.

## Fallo y Retirada Local Acotada

STOP significa no retry de transferencia ni restore, no SQL manual improvisado,
no borrar parcial remoto, no tocar produccion. `restore-started`/`keyring-started`,
timeout o desconexion requieren investigar ID/checkpoint humano y nueva decision;
no resetear checkpoint. No hay comandos de limpieza VPS en este runbook.

Si el proceso Windows fue interrumpido/kill o retiro fallo, mantener ventana humana.
Usar el nombre exacto `LOCAL_CLEANUP_PENDING human-recovery-<nonce>` y el padre ya
seleccionado. El journal persistente contiene SOLO identidad de la carpeta creada,
no secretos. Aprobar explicitamente retirada SOLO ese staging NUEVO:

```powershell
$FailedRoot = Read-Host 'Ruta EXACTA de la carpeta NUEVA fallida dentro del padre elegido'
python -I scripts/ops/human-recovery-windows.py --cleanup-root $FailedRoot
if ($LASTEXITCODE -ne 0) { throw 'STOP: conservar ventana humana; no force-clean' }
```

Pide `RETIRAR SOLO human-recovery-<nonce>`; valida journal, identidad y arbol antes de
Remove-Item -LiteralPath. Deniega parent/perfil/originales/reparse/ACL ajena. Si journal
falta/corrupto, identidad cambio o existe junction: STOP, inspeccion humana autorizada
del alcance exacto; no force-clean ni borrado amplio. Si falla escribir el journal,
no empieza copiar/descifrar y finally intenta retirada por identidad de creacion en memoria.
Desconectar USB al concluir; no borrar ni modificar sus archivos. Solo reabrir agentes
tras retirada comprobada y medios desconectados. Conservar evidencia booleana/IDs/SHA,
nunca stdout/stderr nativo, env, datos, keys, TAR o receipt en GitHub/artifacts.

## Validacion Tecnica y Revision Pendiente

`python scripts/ci/test_human_recovery.py -v`: checks focalizados y Windows PS5/ACL
nativos, age fake sintetico (exito, fallo tardio y SSH fallido), output/temp retirados,
originales intactos, fallback con aprobacion textual, envelope/hash/unsafe TAR y binding.
CI Containers reusa job Linux con `HFPOS_HUMAN_DOCKER_TESTS=YES`: adquiere imagen SOLO
en runner github-hosted efimero, ejecuta preflight/restore/verify exactos, PG real,
keyring, roles/grants/defaults/sequence baseline, noempty/replay/partial/corruption,
probes y cleanup SOLO recursos sinteticos propios. No ampliar smoke app existente.
Local Docker no iniciado; E2E real **CI PENDIENTE** hasta evidencia exact-HEAD.

Review independiente y Fernando deben revisar el codigo final, frontera temporal,
teardown/fallback, copia root, pasos sudo/SSH, ausencia de R4 y APPBLOCKED. Requiere
`VALIDADO OK` pre-merge, sin inventarlo. Operacion real posterior requiere autorizacion
R4 separada incluyendo alcance/ventana/retirada local y preservacion remota/originales.
Referencias: [disaster recovery](disaster-recovery.md),
[supervised operations](supervised-operations.md),
[handoff historico](r4-human-execution-handoff-2026-10-09.md).
