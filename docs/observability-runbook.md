# BE-FE-520: observabilidad

R3: pruebas/configuracion sintetica. Configurar o modificar Production es R4 y exige
operador autorizado, entorno/ventana confirmados, backup verificado y rollback plan.
No existe proveedor SaaS ni alertas SMS/email/WhatsApp integradas en este ticket.

## Logs y health

JSON stdout es la fuente primaria; no duplicar logs en OTLP. Evento unico
`HTTP request completed`: TraceId, SpanId, RequestMethod, RequestPath SIN query,
StatusCode, ElapsedMilliseconds. Activity W3C del request; fallback TraceIdentifier
si no hay Activity. Health exitoso a Debug, fallo no ocultado. Logs automaticos de
Hosting.Diagnostics con URL/query completos se reducen a Warning. No habilitar
HttpLogging de bodies/headers, EF sensitive logging ni dump de configuracion.
No copiar JWT, Authorization, password, cookies, XML, connection string, certificados.
El edge registra un evento JSON seguro con metodo/status/duracion, sin URI/query.
Los error events nativos de nginx se descartan porque contienen request lines crudas;
los errores HTTP siguen visibles por status en completion events (413/5xx), probes
y logs backend. Diagnostico TLS/proxy adicional requiere entorno controlado, nunca
habilitar logs crudos en Production sin evaluar redaccion.
Startup registra release (`HFPOS_RELEASE_VERSION`, fallback AssemblyInformationalVersion).
No publicar version ni detalles internos en health.

`/health/live`: proceso vivo, NO DB. `/health/ready`: DB y lifecycle. Iniciando o
ApplicationStopping -> not-ready; graceful drain dentro de timeout. Respuesta minima
status/nombre/status de checks, sin excepciones ni hosts. SRI/SMTP/OTLP/backup scheduler
NO son readiness dependencies: un fallo de esos sistemas no debe detener ventas.

## Traces y metrics OTLP

Disabled por default. Configurar `Observability__Enabled=true`, ServiceName,
OtlpEndpoint absoluto (base del collector, ejemplo sintetico `http://collector:4318`),
TraceSampleRatio 0..1, default 0.1. Protocolo explicito HTTP/protobuf; agrega
`/v1/traces` / `/v1/metrics` a la base. No credenciales/userinfo/query/fragment en URL;
baseline sin headers de autenticacion de exporter. Si el hosting necesita OTLP auth,
usar collector interno protegido y configurar su salida en infraestructura separada.
TLS para collector remoto; HTTP solo red interna autorizada.

ASP.NET/HttpClient y .NET Runtime, paquetes estables compatibles net8. Process
instrumentation omitida porque el paquete oficial no tiene release estable adecuada
en la version seleccionada; no introducir prerelease. No instrumentar EF/Npgsql/SQL.
Traces allowlist method/status/route/protocol; elimina URL/path/query/server address
y payload tags. Nombre de span usa metodo/route template, nunca URL arbitraria.
Sin captura de excepcion payload. Resource: service.name/version/deployment.environment,
sin CompanyId/UserId/RUC/Establishment/EmissionPoint ni credenciales.
HTTP histogram labels reducidos a method/status/route; outbound method/status.
Nunca agregar SaleId/ProductId/RequestId/username como metric labels.

Batch de traces acotado, metric export periodico, timeout de export 3 s; no exporter
sincrono en request path. Caida OTLP no afecta readiness ni disponibilidad; sampling
reduce overhead. No retener traces de negocio en memoria ilimitada. Disabled no crea
exporter de red. No enviar telemetria fuera de red de prueba en tests.

## Senales y alertas

Vigilar availability/readiness, tasa HTTP 5xx, latencia/volumen por route template,
reinicios, DB readiness failures, outbound HTTP/SRI failures, rate-limit 429,
pipeline OTLP, accesibilidad del keyring, edad del backup y del restore rehearsal.
Alertar por fallos sostenidos, no por cada probe: readiness/5xx persistentes,
reinicios repetidos, backup mas antiguo que RPO aprobado, ensayo vencido o collector
caido. Monitoring/scheduler externos, nunca cron/backups en ASP.NET runtime.
Umbrales deben ajustarse/aprobarse por humanos; NO constituyen SLA contractual.
Correlacionar un incidente con TraceId/SpanId y release, sin exponer datos de tenant.

CI verifica options, resource, logging seguro, Activity, HTTP probes, transport,
lifecycle y OTLP local inaccesible. Smoke verifica TLS/proxy/login sintetico y logs
sin secretos. No requiere Grafana ni SaaS. Integrar collector/alertas reales sigue
PENDIENTE de decision y validacion humana del hosting.
