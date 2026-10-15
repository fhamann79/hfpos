# DEV-531: Verificacion Humana de Aplicacion Restaurada

**PENDIENTE. R3 codigo. VALIDACION HUMANA PRE-MERGE REQUERIDA. NO MERGE por
implementer. Preparacion y CI sinteticos NO autorizan arrancar una aplicacion real.
Se necesita una NUEVA autorizacion R4 humana, finita y expresa despues de review,
CI exact-HEAD y aceptacion pre-merge. NO piloto activo ni DEV-532.**

Esta guia continua el core YA restaurado, no repite backup, exportacion, transferencia
de datos, restore, custodia, USB ni retirada Windows. El coordinador conserva por
separado los reportes del operador y la desviacion SCP. No se deducen resultados
actuales del historico 09-Oct (entonces Empresas=0). No consultar produccion.

## 1. Codigo Publico y Gates

El coordinador debe fijar el HEAD revisado, CI y artifact
`dev531-public-application-probe` del job Containers. Contiene SOLO el helper Python,
DLL de consola, manifest y hashes/public source SHA. El proyecto usa .NET8 y
`Microsoft.AspNetCore.App`, sin paquetes, fuera de la imagen productiva. CI sella
los sources exactos mediante `seal`; la DLL queda ligada por SHA256 al artifact
del build revisado. Esto no es firma ni prueba de reproducibilidad binaria.
Si checkout de CI usa un merge SHA, el coordinador acredita su equivalencia de
sources con el HEAD/integracion aceptados, sin relabelar el SHA del artifact.

Usar SOLO ese bundle publico aprobado. Prepararlo/transferirlo por el canal habitual
como codigo, sin datos/config/keys. No instalar herramientas ni otro executor.
No habilitar SSH/usuarios/servicios nuevos. Verificar host/pin mediante el canal
humano ya establecido. Copiar a un directorio NUEVO `~/hfpos-app-code-<SOURCE_SHA>`;
no sobrescribir el bundle core antiguo. Si falta el bundle core root existente,
la imagen local o el PG tmpfs original, STOP: no recrear/restaurar/pull/restart.

Antes de la ventana: aceptar este procedimiento exacto y autorizar por separado
SOLO el nuevo contenedor app efimero, copia tmpfs del keyring, nuevo rol lector
del destino aislado y su retirada acotada tras exito. El rol es DDL de control
SOLO en el destino; NO cambia usuarios/tenants/datos/config de negocio. La app
no recibe permiso de DDL/DML/lease/secuencias. No autorizar cambios productivos.

Cerrar Codex, agentes, automatizaciones y herramientas del mismo principal/admin
antes de abrir la ventana. No dar passwords, JWT, key XML ni config al chat.
No usar recording/transcript/shell tracing. La ACL del mismo admin no es frontera.
USB permanece desconectada; no abrir backups, snapshot, env ni UserSecrets.

### Bootstrap Root (Solo Codigo)

Reusa el patron stdlib/root snapshot del procedimiento core, con lista nueva fija.
Introducir valores publicados/aprobados por el coordinador, no valores de un bundle
desconocido. Si SCP dejo modos inseguros, STOP; solo el humano puede corregir a
0600 los TRES archivos nuevos de codigo, nunca keys/originales. No relajar checks.

```sh
read -r -p 'SOURCE_SHA del artifact aprobado (40 hex): ' SHA
read -r -p 'APP_CODE_SHA256 aprobado (64 hex): ' HASH
read -r -p 'PROBE_SHA256 aprobado (64 hex): ' PROBE_HASH
# APP_ROOT_SNAPSHOT_BEGIN
sudo python3 -I -B - "$HOME/hfpos-app-code-$SHA" "/root/hfpos-app-code-$SHA" "$SHA" "$HASH" "$PROBE_HASH" <<'PY'
import hashlib, json, os, re, stat, sys
from pathlib import Path
def require(value):
    if not value:
        raise ValueError('STOP')
def unique(items):
    value = {}
    for key, item in items:
        require(key not in value)
        value[key] = item
    return value
try:
    source, target, sha, expected, probe = sys.argv[1:]
    require(os.geteuid() == 0)
    require(re.fullmatch('[a-f0-9]{40}', sha) and re.fullmatch('[a-f0-9]{64}', expected)
            and re.fullmatch('[a-f0-9]{64}', probe))
    target = Path(target)
    require(target == Path('/root') / ('hfpos-app-code-' + sha))
    require(not target.exists() and not target.is_symlink())
    parent = target.parent.lstat()
    require(parent.st_uid == 0 and stat.S_ISDIR(parent.st_mode) and not parent.st_mode & 0o022)
    source = Path(source)
    require(source.is_absolute())
    fds = [os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)]
    try:
        for component in source.parts[1:]:
            fd = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fds[-1])
            fds.append(fd)
            require(not os.fstat(fd).st_mode & 0o022)
        names = ('human-application-verification.py', 'RestoredApplicationProbe.dll')
        require(set(os.listdir(fds[-1])) in (set(names) | {'source.json'}, set(names) | {'source.json', 'docker-empty'}))
        snapshot = {}
        for name in (*names, 'source.json'):
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW, dir_fd=fds[-1])
            try:
                info = os.fstat(fd)
                require(stat.S_ISREG(info.st_mode) and not info.st_mode & 0o022 and info.st_nlink == 1 and 0 < info.st_size <= 1048576)
                with os.fdopen(os.dup(fd), 'rb') as file:
                    snapshot[name] = file.read(1048577)
                require(len(snapshot[name]) == info.st_size)
            finally:
                os.close(fd)
        digest = hashlib.sha256()
        for name in names:
            digest.update(name.encode() + b'\0' + hashlib.sha256(snapshot[name]).digest())
        require(digest.hexdigest() == expected)
        manifest = json.loads(snapshot['source.json'], object_pairs_hook=unique)
        require(manifest == {'source_sha': sha, 'code_sha256': expected, 'probe_sha256': probe})
        require(hashlib.sha256(snapshot[names[1]]).hexdigest() == probe)
    finally:
        for fd in reversed(fds):
            os.close(fd)
    # Verify all bytes before the first privileged write; never run mutable source.
    target.mkdir(mode=0o700)
    for name, content in snapshot.items():
        fd = os.open(target / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'wb') as file:
            file.write(content)
    (target / 'docker-empty').mkdir(mode=0o700)
    print('APP_ROOT_CODE_PASS')
except Exception:
    print('STOP_APP_CODE_NO_RETRY')
    sys.exit(1)
PY
# APP_ROOT_SNAPSHOT_END
```

No ejecutar el siguiente paso si falta `APP_ROOT_CODE_PASS`. No repetir sobre un
destino parcial; conservarlo para investigacion humana, sin force-copy/reset.

## 2. Una Invocacion Humana

Solo tras la NUEVA autorizacion, con agentes cerrados y consola privada:

```sh
read -r -p 'Ruta ROOT del bundle CORE anterior ya sellado: ' CORE_CODE
read -r -p 'Recovery ID existente (32 hex): ' ID
sudo python3 -I -B "/root/hfpos-app-code-$SHA/human-application-verification.py" verify \
  --core-code "$CORE_CODE" --recovery-id "$ID"
```

El helper solicita el `DataProtection ApplicationName` confirmado por el operador.
La referencia publica Compose usa **HFPOS**, pero no prueba el valor desplegado.
Confirmarlo de evidencia humana ya disponible o, SOLO si se autoriza expresamente,
extraer manualmente UNICAMENTE esa clave no secreta; nunca importar/mostrar env
productivo completo. Sin confirmacion, STOP. No cambiar purposes ni probar nombres
al azar. Si existen cuentas en el dump, pide usuario y password ocultos mediante
`getpass`, solo memoria/stdin. No se pasan por argv/env, archivos o logs. Si faltan
cuentas, no las crea; informa EMPTY. No convertir un login correcto en prueba DP.

El prompt final exige `VERIFICAR APP <ID>`. Ese texto acredita la decision del
operador de ESTA ventana, no sustituye la autorizacion R4 previa del coordinador.
La preparacion del repositorio no es tal autorizacion. No ejecutar desde agentes.

## 3. Evidencia y Parada

Antes del arranque: lock exclusivo del workspace core; ownership/modes/code hash
del bundle anterior; checkpoint `core-restored-app-blocked`; ID/labels/imagen PG
fijos, Running, network none, sin puertos/binds/caps; imagen backend YA LOCAL por
digest `5654a239...f40cbe1`, revision `99b92e0e...1c441d`, linux/amd64 y UID1654.
No pull/deploy/tag/migracion. El backend RC no es binariamente main: cambia
`System.Security.Cryptography.Xml` 8.0.4 -> 8.0.5, aunque el C# startup/auth/worker
relevante no cambio. CI ejecuta el RC oficial, no asume compatibilidad por main.

El rol lector nuevo es NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOREPLICATION/NOBYPASSRLS,
NOINHERIT, sin membresias ni ownership; solo SELECT y default read-only. Se verifican
privilegios efectivos (incluyendo PUBLIC, columnas, secuencias, funciones definer)
y rechazo SQLSTATE42501 de FOR UPDATE aun desactivando el default read-only.
No permisos de CREATE permanente ni de mutacion. No se cambian grants del rol core.
Los dos logins RC son read-only: **escrituras auth permitidas = NINGUNA**.

**El worker NO esta desregistrado ni tiene un switch Enabled.** Sigue registrado y
sus intentos de claim quedan DENEGADOS por PG antes de leases/procesamiento; default
read-only es defensa adicional. No se afirma que deny-egress impida writes.
No metodos de certificado/firma/SRI/SMTP son invocados por el probe; el worker no
puede llegar al procesamiento. Logs del hijo se drenan/descartan y Docker usa
log-driver none, diagnostics/minidumps desactivados, limites fijos y sin restart.

Se crea el contenedor NUEVO sin arrancar, se inspeccionan identidad/config/limites,
y se copia SOLO la DLL publica sellada a su capa efimera. `--network container:<PG-ID>`
solo se usa para compartir loopback none: antes de arrancar el backend, la consola
exige SOLO lo, ninguna interfaz/ruta externa IPv4/IPv6, PG TCP positivo 127.0.0.1
y ::1, negativos RESERVED IPv4/IPv6 con NetworkUnreachable/HostUnreachable, y 8080
previamente libre. No basta un curl fallido. IPv6 no operativo es STOP, no PASS.

El keyring original root0600 NUNCA recibe chmod/chown/write. Solo sus XML se copian
en memoria a tmpfs NUEVO UID1654, archivos0400; directorio0700 writable para el probe
temporal que exige el startup RC. Bytes se verifican; autogeneracion DP desactivada
en la consola. Tras el backend se exige mismo keyset/bytes, sin nuevos archivos.
Se verifica luego que el original conserva bytes/inode/owner/mode. Sin canary nuevo.

DP usa SOLO `CompanyEmailSettings.SmtpPasswordProtected` existente, incluso disabled,
el purpose real leido del assembly RC `hfpos.company-email.smtp-password.v1` y el
ApplicationName confirmado. Unprotect solo memoria, plaintext nunca publicado;
wrong-purpose y wrong-application deben fallar. No abre/desprotege certificados.
Si no hay SMTP persistido: `NOT_PRESENT`, no recovery DP completo. No garantiza
borrado de strings/memoria/pagefile ni confidencialidad frente al mismo admin.

El backend child ejecuta `/app/Pos.Backend.Api.dll` REAL sin hooks/endpoints nuevos,
Production con guards intactos, JWT sintetico aislado, bootstrap/demo/OTLP apagados,
URLs SRI invalid y loopback HTTP sin puerto publicado/proxy. Se verifican readiness,
tenant/platform login y `/me`. SOLO cuentas existentes. Luego se comparan TODAS
las tablas de usuario y estado logico last_value/is_called de TODAS sus secuencias,
incluidos jobs, leases, auditorias y datos historicos, sin imprimir filas/hashes reales.

Resultados:

- `APP_VERIFICATION_PASS`: HTTP+DP existente presentes y verificaciones completas;
  no afirma equivalencia con produccion actual ni piloto activo.
- `APP_VERIFICATION_PARTIAL_HUMAN_GATE`: evidencia ausente EMPTY/NOT_PRESENT. Mantener
  gate material; no sembrar datos/usuarios/canaries para fabricar PASS.
- `STOP_APP_VERIFICATION_NO_RETRY_PRESERVE_CORE`: fallo/parcial/anomalia. No retry,
  no reset/checkpoint delete ni teardown automatico de recursos parciales.

Solo al exito tecnico (incluye resultado parcial por ausencia), se retira el NUEVO
contenedor ya terminado y su NUEVO rol/grants; nunca PG, core keyring/incoming,
checkpoint anterior ni bundle anterior. Fallos conservan `application-checkpoint.json`
con nombres opacos nuevos para decision humana; no leer logs/env/keys para reportar.
El probe mata SOLO su backend child en finally. El DB tmpfs no se detiene/reinicia.
No operacion sobre produccion/origen. No cleanup general de VPS.

Registrar unicamente estados enumerados, IDs/SHA de codigo/imagen y preservacion,
nunca usernames/passwords/JWT/XML/payload/outputs nativos. Cerrar SSH al terminar;
agentes se reabren solo tras retirada autorizada comprobada o decision humana de
contencion del parcial. La DB/keyring restaurados siguen preservados.
