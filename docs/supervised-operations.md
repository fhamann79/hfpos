# DEV-531: operaciones supervisadas

Estado: **HUMAN_CONTROLLED mediated**, instalacion real NO realizada. Riesgo de codigo
R3; instalacion/ejecucion con datos reales R4 humana separada. NO merge, NO DEV-532.
No cambia AGENTS.md. No constituye autorizacion para que un agente lance el puente,
firme, lea claves/datos reales, acceda al VPS o apruebe una operacion como Fernando.

## Flujo operativo

1. El coordinador solicita `Supervised operations request` con SHA exacto integrado
   en `main`, accion, ID opaco y SHA256 del ciphertext verificado por el humano.
   `workflow_dispatch` solo genera `request.json`: nunca conecta por SSH, despliega,
   descifra ni tiene permisos/secretos operativos. Un intento distinto de 1 se rechaza.
2. Fernando abre el launcher instalado en su perfil separado. Sigue en foreground,
   sin servicio/listener ni actualizaciones automaticas. Consulta solicitudes cada
   30 segundos, verifica workflow/repositorio/main/ancestria/SHA/hash instalado.
3. Antes de leer age o descifrar, muestra la solicitud y pide `AUTORIZAR <run_id>`.
   Para restore, copia ciphertext a staging privado mientras comprueba SHA256;
   age lee esa copia, nunca reabre el original. Solo tras exit 0 y stream completo
   filtra/exporta DB, keyring y metadata. Nunca extrae ni transfiere snapshot.
4. Muestra el plan final, incluido hash/tamano del paquete y hash del manifest;
   pide `FIRMAR <nonce>`. Firma SSH namespace `hf-one-r4` con clave humana separada
   de transporte y age (FIDO con presencia humana opcional). Caduca en 300 segundos,
   maximo aceptado 600. Firma vincula accion/SHA/bundle/ciphertext/ID/run/intento/nonce.
5. El puente envia una linea JSON firmada y un blob binario de longitud fijada por
   stdin a `hfpos-supervised-v1`. SSH no acepta hosts nuevos: known_hosts fijado y
   verificado fuera de banda, sin configuracion global, agente, forwarding ni PTY.
6. El wrapper root toma lock exclusivo antes de iniciar Python/leer stdin. El receptor
   verifica firma con publica instalada, bundle y permiso bajo otro lock exclusivo;
   ledger persistente (fsync) consume run y nonce ANTES de leer el blob o hacer efectos.
   Transferencia limitada a 128 MiB/60 segundos; corte/EOF incorrecto queda UNCERTAIN
   con permiso consumido. No scripts,
   comandos, argv, rutas ni destinos arbitrarios. Un permiso = una operacion.
7. El puente publica automaticamente SOLO un reporte de schema exacto en issue #133.
   Agentes pueden leerlo. Ni stdout/stderr nativo, dump, XML, metadata privada,
   claves, firma, TAR ni plaintext aparecen en GitHub/artifacts/reportes.

`preflight` comprueba checkpoint existente root:root/0700 y bundle/firma; no restaura
ni afirma health/estado de produccion. `isolated-restore` recibe el paquete firmado,
revalida cada hash/ruta, reutiliza `postgres-restore.sh`, `verify-restored-db.sh` y
`keyring-restore.sh`. No sobrescribe incoming; acepta reenvio identico para resume.
Nunca modifica origen, usa `--clean`, aplica migraciones ni borra recursos operativos.

## Bootstrap Humano Unico

**Pendiente: instalacion/custodia/conectividad efectiva.** No ejecutar desde agentes.
Una sola accion de preparacion Windows: crear/usar un principal humano dedicado y
entrar en ese perfil, fuera de acceso de herramientas del agente. SID distinto NO
prueba aislamiento si el agente posee admin/escalacion: verificar bloqueo efectivo
de lectura/ejecucion del perfil/claves/staging por esas herramientas, o una frontera
equivalente aprobada con presencia humana. ACL del mismo usuario nunca basta.
No copiar claves reales a repositorios, GHA, chat, sandbox o paquetes bootstrap.

En ese perfil, con Python 3, OpenSSH con `-Y`, age y gh revisados, iniciar el wizard:

La credencial `gh` humana debe ser fine-grained para SOLO este repositorio:
`Contents: read`, `Actions: read`, `Issues: write` (requests y reporte en issue #133).
No necesita admin, packages ni deployments. Guardarla en credential manager privado,
nunca en codigo, bootstrap, reportes o herramientas del agente. La credencial del
coordinador que solicita workflow_dispatch es separada y no se instala en el puente.

```powershell
.\supervised-launcher.ps1 -Setup -AgentSid <SID-del-principal-del-agente>
```

El wizard pide bindings una vez, comprueba main integrado y hash revisado del workflow,
instala codigo fijado en `%LOCALAPPDATA%\HfposSupervised`, ACL solo humano/SYSTEM,
rechaza reparse points en todos los ancestros, principal compartido y claves iguales.
Recursos nuevos tienen owner SID humano explicito, incluso con Python elevado;
no cambia owner ni adopta archivos/keys preexistentes que fallen la comprobacion.
Genera `host-bootstrap/`: SOLO codigo revisado y dos claves publicas.
La privada age existente permanece externa. Verificar custodia/recovery copies
segun [handoff](r4-human-execution-handoff-2026-10-09.md), sin trasladarla al servidor.

En Ubuntu, humano transfiere ese paquete revisado por su canal admin existente
y ejecuta UNA entrada: `sudo sh bootstrap.sh`. Requiere autorizacion R4 separada,
consola/rollback disponibles y Python3, Docker, OpenSSH, sudo previamente instalados.
El instalador crea cuenta dedicada sin password usable, wrappers root-owned/0755,
bundle root/0700 (archivos 0600), authorized_keys publica root/0644, sudo exacto sin
argumentos, Match SSH forced-command sin PTY/forwarding/RC. Valida visudo y sshd
efectivo antes de reload. Si falla, puede quedar instalacion parcial: DETENER,
inspeccion humana; no rollback/borrado automatico ni repetir como si no existiera.
No cambia firewall, grupos Docker, rutas de produccion ni instala self-hosted runner.

El humano debe confirmar fingerprint SSH fuera de banda, cuenta sin shell/SFTP/
forwarding, sudo limitado, signer correcto, rechazo de firma/replay, separacion real
del agente, capacidad/disco y acceso SSH desde su IP existente. NO abrir rangos GHA
ni prometer costes/IP fija. Tras esas comprobaciones, abrir el launcher instalado
sin `-Setup`; cada solicitud sigue requiriendo aprobacion y firma individuales.
Versiones nuevas requieren otro cambio/bootstrap humano revisado, nunca auto-update.

## Restore Y Limites

- Usa el workspace existente `/srv/hf-one/pilot/recovery-r4/<ID>` y sus tres directorios
  root/0700; no los recrea ni confunde existencia con restore completado.
- DB NUEVA/VACIA en PostgreSQL 16 Alpine por digest, usuario OS 70, `--network none`,
  sin mounts host/socket Docker, puertos, privilegios/caps. Limites: 768 MiB RAM/swap,
  0.5 CPU, 128 pids. El dump ejecuta como owner SQL no-superuser; runtime separado
  sin DDL, grants/default privileges de public revisados. No grants de origen.
- DB es **volatil, tmpfs de ensayo**: se pierde al reiniciar/eliminar el contenedor.
  `postgres-data/` se comprueba vacio pero NO es storage de esa DB. No es recuperacion
  durable/cutover ni prueba completa DR. No hay cleanup destructivo operativo.
- Inspecciona configuracion real, solo loopback/sin rutas externas IPv4/IPv6, prueba
  PG positiva en 127.0.0.1; ::1 positiva SOLO si IPv6 loopback esta configurado.
  Distingue kernel/namespace IPv6 deshabilitado y loopback sin direccion de IPv6
  operativo, inspeccionando proc/flags/interfaces/rutas. Siempre prueba negativas
  a direcciones TEST-NET IPv4/IPv6 con el MISMO
  cliente funcional, antes de cualquier posible app-start. Si no se puede probar,
  falla cerrado. No contacta SRI/SMTP/hostnames reales para probar egress.
- Scripts oficiales y dump entran a tmpfs mediante `docker exec -i tar`, TAR USTAR
  generado con allowlist fija y hashes verificados; nunca `docker cp` hacia/desde
  tmpfs ni scripts arbitrarios recibidos. El fixture saca pg_dump por stdout privado.
- Checkpoint atomico/fsync registra intencion de crear y despues ID exacto del
  contenedor, permitiendo resume de fallo parcial ANTES del SQL. `restore-started`
  exige investigacion humana: nunca repite SQL/pg_restore automaticamente.
  Checkpoint permite continuar despues de DB restaurada solo con import/labels
  identicos, red segura y keyring aun vacio. Completed resume verifica schema,
  ownership/privilegios de tablas/secuencias, default privileges y keyring byte-a-byte.
  Comprueba ID del contenedor, no solo nombre/labels. Estado parcial no reconocido exige
  inspeccion humana; no recrea DB vacia ni sobreescribe keyring.
- **APP START NO DISPONIBLE**. No backend/proxy/login/protected-data real, ni claims
  de servicio restaurado. Futuro backend podria compartir namespace de DB aislada
  (`--network container:<DB>`, loopback, sin puertos), pero exige revision y probes
  efectivos antes del arranque; no queda habilitado por este ticket.
- Archive: max 128 MiB total/128 miembros/64 MiB por archivo; keyring 4 MiB y 1 MiB
  por XML. TAR sin compresion/PAX/links/special files/traversal/duplicados. Raiz GNU
  `./` solo como directorio. Exactamente un dump oficial y un keyring con companions;
  internal-sha256 debe cubrir exactamente todos los archivos salvo a si mismo.
  Si el backup historico usa otro manifest/formato, falla cerrado: adaptar/testear
  codigo revisado, nunca omitir hashes ni seleccionar el primer basename.

Desconexion/timeout produce `UNCERTAIN`: puede haber efectos remotos. No reintentar
ese run, nonce ni operacion automaticamente; revisar audit/checkpoint humano y
crear NUEVA solicitud/aprobacion. Fallo reportando se reintenta SOLO como reporte,
nunca vuelve a ejecutar. El audit retiene solo IDs/hashes/estado booleano app_started=false.

## Validacion

`python scripts/ci/test_supervised_operations.py -v`: pruebas sinteticas nativas SSH,
ACL Windows con creacion owner SID explicito, archive, auth tardia age simulada con subprocess, TOCTOU y rechazo antes
de descifrar. Linux root agrega lock/replay/receiver/rutas/wrappers. Containers
activa `HFPOS_SUPERVISED_DOCKER_TESTS=YES`: signed fixture -> fixed serve -> PG real
sin red -> restore oficial/keyring/grants de secuencias/defaults/probes -> resume
parcial pre-SQL/corruption/replay, con
fingerprint de DB sintetica origen intacto. Conserva el smoke de negocio existente.
Sin motor local, E2E Linux es **CI PENDIENTE**, no evidencia de ejecucion local.
`HFPOS_SUPERVISED_NATIVE_AGE=YES` exige age/age-keygen instalados en ese paso CI:
round-trip criptografico NATIVO SINTETICO y ciphertext truncado, sin publicar plaintext.
No prueba backup/clave humana real ni bootstrap en host real en esta entrega.

Validacion humana pre-merge requerida para el mecanismo de privilegios/custodia;
operacion real permanece gate R4 posterior. Sin datos/certs/SRI/secretos reales.

Alternativa elegida: puente Windows humano; evita custodiar SSH en GHA y resolver
egress de runners. GHA remoto con environment requeriria proteccion LIVE y firma
humana igualmente, pero NO esta implementado. Referencias oficiales:
[GitHub workflow dispatch](https://docs.github.com/en/actions/writing-workflows/choosing-when-your-workflow-runs/events-that-trigger-workflows#workflow_dispatch),
[OpenSSH restricted keys/signatures](https://man.openbsd.org/sshd.8),
[SSH signatures](https://man.openbsd.org/ssh-keygen.1),
[Docker none network](https://docs.docker.com/engine/network/drivers/none/),
[Docker cp: limites tmpfs](https://docs.docker.com/reference/cli/docker/container/cp/#corner-cases),
[age](https://github.com/FiloSottile/age).
