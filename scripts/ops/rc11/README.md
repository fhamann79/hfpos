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

`SHA256SUMS` fija los bytes de los nueve artefactos de esta revision, incluyendo
el runbook y los fixtures. Git no normaliza sus finales de linea. La huella del
controlador original se conserva en el [PR #180](https://github.com/fhamann79/hfpos/pull/180).

El preflight no deduce el filtrado de fechas o archivos de configuracion. Exige
el perfil iptables-nft reconocido, reglas DROP efectivas para cada endpoint
ruteable de web/backend y una barrera de entrada a la bridge de datos que no
dependa de la direccion de origen. Una cadena previa desconocida, una regla
solo por prefijo, un perfil no reconocido o un cambio de PID, red o reglas
detienen la operacion. La publicacion privada requiere ademas reglas y sockets
compatibles con loopback, sin NAT publico.

El modo `-VerificarBridge` solo lee y correlaciona esos datos desde el host y
devuelve un resultado saneado; no inicia corte, modifica reglas ni sustituye
el monitoreo independiente del cierre IPv4/IPv6 y del SSH fresco.

Este directorio no contiene archivos env, claves, respaldos ni configuracion
privada de PG, SSH, age o rclone.
