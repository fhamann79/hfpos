# DEV-532 / RC11: artefactos revisados del corte humano

Este directorio conserva los archivos locales validados de la ventana RC11. No
despliega nada por CI. Ver [runbook de ejecucion y recuperacion](Runbook-DEV532-RC11-ejecucion.md)
y [evidencia del corte preparado](https://github.com/fhamann79/hfpos/issues/134#issuecomment-7026136882).
La publicacion de este directorio no autoriza R4.

## Inventario

| Archivo | Uso |
| --- | --- |
| `Ejecutar-DEV532-RC11.ps1` | Entrada humana en PowerShell; no ejecutar en CI. |
| `Sondeo-DEV532-RC11.ps1` | Observador externo y navegador privado del operador. |
| `Empaquetar-DEV532-RC11.py` | Arma el payload local; obtiene las huellas de los helpers del commit fijo RC11. |
| `Inspeccion-DEV532-RC11.py` | Fuente de las constantes, consultas y funciones que incorpora el empaquetador. |
| `Corte-DEV532-RC11.py` | Controlador remoto con autorizacion humana y modo de reconciliacion de solo lectura. |
| `Guard-DEV532-RC11.py` | Proteccion dedicada y fuente de `boot-check.py`, ejecutado obligatoriamente antes de iniciar Docker. |
| `Probar-Corte-DEV532-RC11.py` | Prueba sintetica del payload: red, Docker, EF y backup simulados. |
| `rc11-compose-fixture*.yml` | Prueba del modelo Compose privado con una imagen ficticia; no se arranca. |
| `Test-Launcher.ps1` | Adaptador adicional de CI: SSH, empaquetado y procesos simulados. |

El lanzador revisado busca su checkout Git en la ruta de la estacion del
operador documentada en el runbook, y espera sus archivos auxiliares al lado.
Conservar los archivos juntos y verificar SHA-256 antes de usar una copia del
PR. No sustituir la ruta ni relajar la comprobacion de procedencia en una ventana R4.

Dependencias locales: Windows PowerShell 5.1, OpenSSH, Git, Python 3, curl y
Microsoft Edge. Dependencias remotas ya contempladas por el runbook: Python 3
y sudo, nftables, Docker Engine 28 o posterior con Compose que soporte
`!override`, PostgreSQL del servicio existente y el productor de respaldos
activo con age/rclone y su lock. El empaquetador ancla
`backup.py`, `supervised-archive.py`, `supervised-operations.py`,
`postgres-backup.sh` y `verify-postgres-backup.sh` en
`8c80d8468834d2e8cdf6353a443ec35fa51e70ae`; no los duplica ni instala.

`SHA256SUMS` fija los bytes de los artefactos del payload, incluyendo
el runbook y los fixtures. Git no normaliza sus finales de linea. La huella del
controlador original se conserva en el [PR #180](https://github.com/fhamann79/hfpos/pull/180).

El preflight acredita la topologia y el perfil de filtrado antes de preparar una
tabla propia que bloquea las rutas directas. El proceso usa solamente
`unix:///run/docker.sock` para Docker, Compose y el adaptador de respaldo,
sin escribir el contexto de root. Se verifica que el socket y la unidad
activada correspondan al proceso local `dockerd`.

El guard persistente se carga antes de Docker. Incluso con el oneshot activo,
el `ExecStartPre` obligatorio vuelve a cotejar el snapshot permanente y, si
hay marcador, el token y la tabla temporal exacta. El perfil UFW aceptado es
el stock Noble 0.36.2-6, sin hooks personalizados, con IPv6, alternativas
iptables-nft, reglas en filter y sysctl stock o desactivado; cualquier perfil
no acreditado detiene el corte. No se ejecuta un reload de prueba en el host.

La publicacion privada sigue requiriendo loopback sin NAT publico. Antes de
retirar el cierre, se acredita por familia el DNAT exacto o el `docker-proxy`
propietario del socket y dirigido al endpoint web real. Solo el proxy probado
recibe un permiso INPUT limitado al origen de esa familia.

Pruebas locales sin despliegue: `python -B Probar-Corte-DEV532-RC11.py <repo>`
y `Test-Launcher.ps1`. Estas pruebas enmascaran Docker, nftables, systemd, SSH,
EF y respaldo; no acreditan la activacion real, el reload ni un corte VPS.

El modo `-VerificarBridge` solo lee y comprueba la regla propuesta o la proteccion
ya instalada. No inicia el corte ni sustituye el monitoreo independiente del
cierre IPv4/IPv6 y del SSH fresco.

Este directorio no contiene archivos env, claves, respaldos ni configuracion
privada de PG, SSH, age o rclone.
