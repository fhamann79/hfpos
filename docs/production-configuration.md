# BE-FE-519: contrato de configuracion Production

Esta entrega prepara la aplicacion; no despliega ni configura infraestructura.
Riesgo R3. No hay migracion ni cambio de modelo. No ejecutes `dotnet ef database update`
por este ticket. Las futuras migraciones requieren el runbook y autorizacion humana;
Production nunca aplica migraciones ni seed automaticamente.

## Variables

Usar secret store o variables del entorno para secretos, nunca archivos versionados.
Los marcadores siguientes describen categorias, no valores funcionales para copiar.

| Variable | Contrato |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Obligatoria, `<SECRET_STORE>`; PostgreSQL del entorno autorizado |
| `Jwt__Key` | `<SECRET_STORE>`, al menos 32 bytes UTF-8, alta entropia; nunca registrar |
| `Jwt__Issuer`, `Jwt__Audience` | `<CONFIG>`, ambos obligatorios y no vacios |
| `Jwt__ExpiresMinutes` | 5..1440; default 120 |
| `Jwt__ClockSkewSeconds` | 0..120; default 30 |
| `AllowedHosts` | `<PRODUCTION_HOST>`; hosts explicitos separados por `;`, sin comodines ni puertos |
| `SeedDemoData` | Debe ser false en Production |
| `Cors__AllowedOrigins__0` (y siguientes) | Opcionales; origen HTTPS absoluto sin path/query/fragment/credenciales/comodines; slash final normalizado |
| `DataProtection__ApplicationName` | Nombre estable; default Production HFPOS |
| `DataProtection__KeysPath` | `<PERSISTENT_PATH>` absoluto, existente, con lectura/escritura; sin creacion alternativa automatica |
| `ForwardedHeaders__Enabled` | Default false |
| `ForwardedHeaders__KnownProxies__0` (y siguientes) | IPs explicitas; lista no vacia si se habilita; no confiar en cualquier origen |
| `ForwardedHeaders__ForwardLimit` | 1..3; default 1 |
| `AuthRateLimit__PermitLimit` | 1..100; default 20 intentos por ventana/plano/IP |
| `AuthRateLimit__WindowSeconds` | 1..3600; default 60; queue 0 |
| `Transport__HstsMaxAgeDays` | 1..365; default 30; sin preload/includeSubDomains automaticos |
| `PlatformBootstrap__Enabled` | Default false; activar solo para primer administrador de plataforma |
| `PlatformBootstrap__Username`, `PlatformBootstrap__Email` | Obligatorios y validos solo si bootstrap habilitado |
| `PlatformBootstrap__Password` | `<SECRET_STORE>`, 12..256 caracteres; retirar despues del primer administrador |
| `Sri__Environment` | Solo 1 test o 2 production |
| `Sri__TimeoutSeconds` | 5..120; default 30 |
| `Sri__ReceptionTestUrl`, `Sri__AuthorizationTestUrl` | HTTPS absolutos requeridos para ambiente 1; defaults actuales preservados |
| `Sri__AllowProductionSubmission` | Default false; sigue bloqueando ambiente 2 |
| `Sri__ReceptionProductionUrl`, `Sri__AuthorizationProductionUrl` | Vacios por default; si opt-in habilitado, HTTPS absolutos, distintos de endpoints test |

Production sin overrides criticos falla antes del seed/bootstrap. No usar
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (ni `ForwardedHeaders_Enabled=true`):
se rechaza porque el mecanismo automatico del framework puede confiar en todos
los proxies. Usar exclusivamente las opciones explicitas de la tabla. El proxy
confiable debe sanear headers entrantes; solo se procesan For/Proto, no Host.

## Transporte y frontend

JWT sigue siendo Bearer HMAC-SHA256, con expiracion/firma/issuer/audience obligatorios.
Los planos tenant/platform y sus versiones de sesion/autorizacion no cambian.
Login tiene limite separado por endpoint e IP, 429 `RATE_LIMITED` y `Retry-After`.
El limitador es por instancia; proteccion distribuida/edge pertenece a BE-FE-520.
Los tokens de login tienen `Cache-Control: no-store`; el API agrega nosniff,
DENY y no-referrer. HTTPS redirection permanece; Production agrega HSTS.

El bundle Production usa same-origin (`apiUrl=''`, `/api/...`). Una lista CORS
vacia es valida y no permite lectura cross-origin. No hay cookies CORS/AllowAnyOrigin.
El hosting futuro debe enrutar `/api` al backend; este ticket no implementa ese hosting.
`ng serve` Development conserva `https://localhost:7096`, CORS localhost:4200 y Swagger.

## Key ring y credenciales

`KeysPath` debe apuntar a almacenamiento persistente y compartido si existen replicas.
Mantener el mismo ApplicationName y purposes para descifrar valores existentes.
Development sin KeysPath conserva el key ring y discriminator implicitos actuales.
No se re-encriptan datos ni se cambian los purpose strings SMTP/SRI.
El almacenamiento de keys requiere ACL restrictivas, acceso exclusivo del servicio
y proteccion en reposo por la infraestructura; persistencia en filesystem no aporta
por si sola cifrado en reposo. Montajes/KMS/volumenes pertenecen a BE-FE-520.

Las nuevas contrasenas de administracion/provisioning/bootstrap requieren 12..256
caracteres, sin reglas artificiales. Las legacy no se invalidan, rehashean ni resetean
automaticamente. Tras el primer platform admin, deshabilitar bootstrap y retirar
su password. Bootstrap nunca resetea cuentas existentes ni migra la base.

Errores desconocidos, FK y DbUpdate no exponen detalles internos al cliente.
Los codigos funcionales legacy permitidos estan en `PublicErrorCodes`; agregar ahi
cualquier nuevo codigo que use InvalidOperationException/KeyNotFoundException.
No agregar body/header logging de passwords, JWT, Authorization, hashes,
connection strings, material protegido o XML firmado.

## Smoke humano posterior (Development)

1. Actualizar rama y conservar los User Secrets locales existentes (no enviarlos al agente).
2. Iniciar backend y `npm start`, hacer login tenant y abrir una pantalla operativa.
3. Hacer login platform y abrir lista de empresas.
4. Crear/cambiar password sintetica: 11 caracteres debe rechazarse, 12 o mas aceptarse.
5. Logout/login y confirmar que las sesiones y permisos siguen funcionando.

No requiere database update. Pruebas de limite/proxy/key ring/errores/SRI son
automatizadas sinteticas, no repetirlas manualmente con servicios reales.
Revision independiente y smoke humano pendientes antes de merge. No merge autorizado.
Deployment, observabilidad, backups/restore y disaster recovery quedan para BE-FE-520.

## Runtime BE-FE-520

La baseline y los procedimientos estan en `deployment-runbook.md`,
`observability-runbook.md` y `disaster-recovery.md`. No autorizan despliegue real.

DEV-531 overlays opcionales: [runbook](pilot-hosting-runbook.md). Target piloto
(BASE+PG cohost o BASE+MIGRATOR externo) usa runtime `HFPOS_DATABASE_CONNECTION`;
one-shot migraciones del target usa obligatoriamente
`HFPOS_MIGRATION_DATABASE_CONNECTION`, separado de runtime/admin. PG bootstrap
`POSTGRES_USER` es superuser: nunca usarlo como app. Overlay PG16 exige digest,
storage absoluto existente, admin password file externo y DB/user explicitos.
Futura DB externa exige hostname/CA y VerifyFull mediante config/montaje, no codigo.
Validar privilegios efectivos fuera del test config, despues de R4 autorizado.
Baseline legacy/base y env existentes siguen intactos con conexion compartida;
no son evidencia de role separation ni la composicion target piloto.

| Variable | Contrato operacional |
| --- | --- |
| `Operations__ShutdownTimeoutSeconds` | 5..120; default 30; gracia del orchestrator mayor que este limite |
| `Observability__Enabled` | Default false; no exporter de red si deshabilitado |
| `Observability__ServiceName` | Default hfpos-api; requerido si habilitado |
| `Observability__OtlpEndpoint` | Base URI absoluta HTTP/HTTPS sin userinfo/query/fragment; requerida si habilitado; HTTP/protobuf explicito |
| `Observability__TraceSampleRatio` | 0..1; default 0.1 |
| `HFPOS_RELEASE_VERSION` | Version no secreta, startup log/resource; fallback assembly version |
| `HFPOS_MIGRATION_APPROVED` | YES solo en one-shot humano; CI exclusivamente efimero |
| `HFPOS_RESTORE_APPROVED` | YES solo tras verificar destino vacio/backup; real restore R4 humano |

Health HTTP interno solo para paths exactos live/ready; resto conserva HTTPS redirect.
Readiness incluye lifecycle y DB, nunca OTLP/SRI/SMTP/backups. JSON stdout es fuente
primaria de logs; no query/body/header/SQL capture. No cambia ningun contrato comercial.
