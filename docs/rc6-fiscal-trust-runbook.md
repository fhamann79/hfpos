# DEV-531 RC6: confianza fiscal y preflight multitenant

Issue #162, relacionado con #133/#127 y exclusivamente preflight fiscal de #158.
R3. NO MERGE, RELEASE, DEPLOY, CONTABO ni SRI real por agentes.
Este documento prepara una operacion humana R4 separada; no la ejecuta ni la autoriza.

## Arquitectura y diferencias frente a RC5

- RC5 diagnosticaba confianza del sistema operativo con `NoCheck`. RC6 usa un registro publico aprobado, independiente de TLS, `CustomRootTrust` y auxiliares `ExtraStore`; AIA/descargas estan deshabilitados. `NoCheck` solo construye la cadena: Produccion exige ademas revocacion offline GOOD.
- El manifiesto es inmutable durante la vida del proceso. Cambiarlo requiere una nueva version/pin aprobado y reinicio supervisado; no hay CRUD CA, nuevos roles, permisos tenant globales ni migraciones EF.
- `FiscalCertificateEvaluator` separa cadena, identidad, vigencia, aptitud y revocacion. Identidad reutiliza `SriCertificateIdentityReader`: MATCH, MISMATCH, NOT_CONFIRMED. MISMATCH bloquea; NOT_CONFIRMED conserva el warning de RC5 y NO acredita identidad. No se infiere RUC desde nombres comerciales.
- `SriPfxReader` importa colecciones efimeras: exactamente un certificado con clave privada, hasta 32 auxiliares, 2 MiB y cuatro importaciones simultaneas. Subida valida antes de desactivar el certificado anterior. Se conserva el PFX original cifrado, Data Protection, proposito, ApplicationName y keyring. Bytes temporales se limpian; las cadenas password administradas no son borrables de forma garantizada.
- Nueva factura de Produccion: replay comprometido primero; despues settings/permiso operativo de Produccion y confianza, antes de caja/secuencial/stock. Orden de locks Company -> CashSession -> DocumentSequence -> ProductStock intacto.
- Firma manual/worker: evalua el certificado concreto usado y RUC del snapshot. No hay constructor legacy que permita Produccion sin guard. Ambiente desconocido no equivale a Pruebas.
- Recepcion: hash persistido, un solo certificado embebido, firma RSA-SHA256, referencias internas y transforms del perfil XAdES existente, clave/RUC/snapshot, cadena y revocacion actuales, todos antes de `ReceptionStartedAt`. XSD y resolucion de contexto tambien preceden la intencion. Una falla local no es recepcion incierta.
- Recepcion nunca descifra PFX. Auxiliares publicos provienen del registro o cache del monitor/firma, vinculada por CompanyId y thumbprint. Rotar el certificado activo no cambia el firmante persistido. Se conserva el control operativo preexistente de certificado activo para recepcion/worker; no se autoriza nueva semantica de desactivacion.
- Consultas/autorizaciones e historicos no vuelven a firmar ni pasan por el nuevo evaluador. Conservan controles operativos ya existentes. Retirar CA no reescribe XML, claves, numeros, RIDE ni historia.
- NC reutiliza los mismos guards de firma/recepcion sin reescribir su concurrencia. Riesgo preexistente: recepcion NC realiza transporte antes de su transaccion de persistencia y no tiene el mismo lease/fence durable que facturas. Debe revisarse como trabajo separado; RC6 no afirma resolverlo.

## Perfil soportado y limites

`rsa-full-crl-v1`: RSA 2048..8192; firmas de certificados/CRL RSA PKCS#1 SHA-256/384/512; firma XML existente RSA-SHA256. CA con BasicConstraints y keyCertSign; emisor CRL tambien cRLSign; firmante no CA con digitalSignature. X509Chain aplica path length, firmas, vigencia y restricciones criticas con NoFlag. Autofirma de raiz verificada explicitamente.

EKU no se presume serverAuth/codeSigning. Si cualquier certificado de la ruta declara EKU, al menos un mismo proposito aprobado por el perfil/ancla debe sobrevivir a TODAS las restricciones de la ruta, no solo a las del firmante. EKU ausente no restringe; anyExtendedKeyUsage no elimina las restricciones de los demas niveles ni aprueba propositos no configurados. Sin interseccion compatible se bloquea. `AllowedExtendedKeyUsages` y `RequiredCertificatePolicies` son listas del perfil Root: valores no vacios en Intermediate/CrlIssuer INVALIDAN el manifiesto, no se ignoran. `RequiredCertificatePolicies` configura OID exigidos al construir cadena; cualquier reconstruccion debe conservar exactamente la ruta y ancla evaluadas. `RequiresOcsp` aplica a TODAS las autoridades de la ruta, incluido CrlIssuer; cualquier requisito no implementado bloquea. Estos perfiles NO constituyen compatibilidad universal con emisores ecuatorianos.

CRL DER v2 completa/directa: firma del TBSCertList original, algoritmos interior/exterior iguales (parametros NULL/ausentes), unusedBits=0, issuer DER y AKI keyIdentifier/SKI, numero de CRL aprobado, tiempos y todas las entradas verificadas antes de GOOD. Seriales positivos normalizados; certificado high-bit usa serial sin signo. certificateHold = SUSPENDED. Revocacion comprobada de firmante e intermediarios; raiz por ciclo de vida del registro.

Se rechazan delta, freshestCRL (tanto en CRL como en cualquier certificado de la ruta), IDP/particiones, indirect CRL/certificateIssuer, removeFromCRL, extensiones no soportadas, entradas duplicadas, fechas de revocacion futuras, evidencia falsificada y DER sobrante. No hay fallback a otra CRL favorable. OCSP NO implementado; un perfil con `RequiresOcsp=true` queda UNKNOWN/NO-GO.

GOOD significa evidencia autentica vigente bajo esta politica, NO prueba criptografica de ser la ultima publicacion del emisor. La ultima publicacion debe observarse y aprobarse operativamente. Se separan `MaximumAgeHours` de emision (1..8784, aprobado por perfil CRL/ARL) y observacion `ProvisionedAt`/`ApprovedCurrentUntil` (maximo 24 horas). `nextUpdate`, ambas frescuras, vigencia CA y manifiesto limitan la decision por cada uso. Una ARL semestral puede estar vigente sin considerarse fresca una observacion antigua.

Registro: 8 MiB, 64 certificados/64 CRL, certificado 32 KiB, CRL 1 MiB/16384 entradas, cadena maximo 10, auxiliares maximo 32, cuatro evaluaciones simultaneas. No HTTP/DNS/OCSP/CRL client en evaluador. Limites nativos de importacion PKCS#12 de .NET/OS siguen aplicando; las APIs sincronicas no ofrecen cancelacion dura a mitad de criptografia. Monitor pagina 1..50 empresas, cache hasta 10000 empresas, maximo 32 certificados historicos por tenant; excesos son bloqueos concretos, nunca PASS parcial. Cache publica: DER maximo 32 KiB por certificado, 1 MiB por entrada y 64 MiB globales, con validacion/reserva/reemplazo atomicos y reclamacion de entradas vencidas al leer/escribir. Material fuera del presupuesto no se retiene; falta de auxiliares nunca equivale a confianza. Cache maximo cinco minutos: un positivo nunca sobrevive la vigencia de evidencia; un rechazo reciente conserva su causa hasta el TTL de observacion sin convertirse en GO. Reinicio pierde cache y produce UNKNOWN hasta nueva observacion.

Pruebas SRI ambiente 1 conserva la politica sintetica previa (autofirmados permitidos y sin elegibilidad de Produccion); los nuevos guards criptograficos de ejecucion son obligatorios para ambiente 2. No se reutilizan sentinels de pruebas como XML de Produccion. Historicos ya firmados no se regeneran.

## Registro operativo protegido

1. Operador obtiene certificados **publicos** y CRL/ARL desde fuentes oficiales autenticadas; confirma DPC, perfil aplicable, jerarquia completa y cadena por cada emisor real, sin suponer que dos empresas comparten CA. Nunca recibe PFX/password para aprobar confianza global.
2. Segundo aprobador verifica procedencia, SHA-256, vigencias, rol Root/Intermediate/CrlIssuer, politica/EKU y necesidad de OCSP. `CrlIssuer` aprueba el emisor exacto de evidencia sin suministrarlo a la construccion de cadena; necesita auxiliar PFX/cache para esa funcion. Nunca ancla un certificado tenant.
3. Crear manifiesto con `SchemaVersion=1`, `Version`, `ValidFrom`/`ValidUntil` UTC, `Authorities` y `Crls`. Cada autoridad: `Sha256` en hex mayuscula, `CertificateBase64` DER, `Role`, `Provenance`, `Profile`, `RequiresOcsp`, `AllowedExtendedKeyUsages`, `RequiredCertificatePolicies`. Cada CRL: `IssuerSha256`, `Sha256`, `DerBase64`, `Provenance`, `MaximumAgeHours`, `ProvisionedAt`, `ApprovedCurrentUntil`, `ApprovalReference`, `CrlNumberHex` (INTEGER normalizado hex mayuscula).
4. Mantener ledger operativo independiente de versiones/pins y CRLNumber por issuer. Verificar ultima CRL **completa**/ARL de cada nivel y que no hay publicacion mas reciente/delta obligatoria. Rechazar rollback de numero/fecha/version; conservar evidencia de consulta/aprobacion. Refrescar antes del deadline; alarmar y bloquear si no puede probarse actualidad. No existe downloader automatico en RC6.
5. Aprobador publica SHA-256 esperado por canal operativo independiente, no mediante un script que calcule y apruebe cualquier archivo presente. Conservar firma/cambio/aprobadores/fuente en el sistema operativo de cambios. El pin autentica **todo** el manifiesto, incluidos tiempos y CRLNumber.
6. Montar archivo y configuracion aprobada read-only, propietario protegido, sin escritura del usuario de app. Configurar `FiscalTrust:RegistryPath` y `FiscalTrust:ExpectedSha256` por configuracion operativa. No usar SSL_CERT_FILE ni modificar TLS global. Registro no aprobado/corrupto/ausente/caducado impide nuevas acciones de Produccion pero no tumba todos los tenants ni reescribe historia.
7. Integridad se verifica al cargar el singleton, antes de evaluar el primer documento/ciclo; el endpoint muestra AVAILABLE o causa concreta. Archivo modificado despues del arranque no altera el snapshot aprobado vivo. No copiar CA reales a este repositorio.

## Preflight candidato ANTES de sustituir RC5

No arrancar el entrypoint de la API RC6 sobre DB real para obtener este reporte: podria iniciar workers y sus probes actuales. Existe un entrypoint aislado `Hfpos.FiscalPreflight`, sin WebApplication/seed/auth/fiscal workers/migraciones/write probes. Solo usa consultas, monitor y Data Protection con `DisableAutomaticKeyGeneration`; no fabrica JWT ni contexto Platform.

Ejecucion exclusivamente HUMANA con autorizacion R4 separada y el aislamiento del runbook de recuperacion existente:

1. Conservar RC5 sirviendo; preparar candidato exacto, aprobado por digest/commit. No sustituir contenedores, DB, configuracion ni keyring de RC5. Validar backup cifrado de PostgreSQL **y** keyring, integridad/custodia y restore aislado antes de un corte posterior.
2. Provisionar rol temporal lector dedicado sin superuser/createdb/createrole/bypassrls, sin membresias/escrituras, solo SELECT necesario; `default_transaction_read_only=on`. CLI verifica modo y privilegios de tablas antes del monitor; no hace prueba de escritura. Aislar red: solo DB autorizada, sin SRI/Internet; no usar conexion persistente de Development. No exponer credenciales/keyring/PFX a agentes.
3. Montar keyring existente **read-only** y fijar el ApplicationName real previamente confirmado. No crear keyring ni permitir generacion automatica. Falta de claves/DP incompatible produce UNKNOWN por tenant, no regeneracion de material.
4. Publicar host con `dotnet publish backend/Pos.Backend.Api/Hfpos.FiscalPreflight -c Release -o <salida-controlada> /p:UseAppHost=false`. En el contenedor/entorno aislado establecer `HF_RC6_READONLY_CONNECTION`, `HF_RC6_READONLY_KEYRING`, `HF_RC6_APPLICATION_NAME`, `HF_RC6_REGISTRY_PATH`, `HF_RC6_APPROVED_SHA256`. No imprimir secretos en comandos/logs compartidos. Ejecutar `dotnet Hfpos.FiscalPreflight.dll --read-only-cycle`. No llamar `Pos.Backend.Api.dll`.
5. Esperar ciclo completo: recorre por CompanyId **todas** las paginas, incluidas empresas suspendidas/sin settings. Salida JSON por pagina hasta TotalPages, con TotalCompanies, version, LastCompletedCycleAt, AsOf, CertID, SHA-256 evaluado, RUC snapshot, expiracion y token SHA-256 del ciphertext (calculado en SQL sin retornarlo a Platform). Limite diez minutos; timeout/limite/cobertura incompleta => NO-GO.
6. Conciliar conjunto de CompanyId y conteos, no solo primera pagina/TakeN. Snapshot cambiado (incluidos bytes cifrados/password, certificado activo, RUC/settings) invalida resultado. Ausente, stale, error de un tenant o historial fuera de limites => UNKNOWN/NO-GO para ese tenant; otros siguen evaluandose. Repetir ciclo si RC5 cambia datos entre paginas y corte. La observacion no es un snapshot transaccional global.
7. Capturar baseline RC5 por procedimiento humano read-only ya autorizado, con version/fecha y limitaciones. Columna `Rc5Baseline=UNKNOWN_OPERATOR_BASELINE_REQUIRED` permanece separada: RC6 NO inventa RC5 PASS ni ejecuta validacion global OS con descargas. Comparar por CompanyId/certificado/RUC los flags RC5 reportados y decision RC6. Cadena GOOD no implica readiness comercial ni permiso de envio.
8. Revisar delegaciones activas, jobs/leases pendientes, recepciones ambiguas y firmantes historicos que deben conservar auxiliares publicos. No cambiar delegaciones ni reprocesar trabajos en preflight. Cache publica de auxiliares se llena con los 32 certificados retenidos por tenant; historia mayor requiere resolucion operativa, no eliminar historia.
9. Guardar reporte en almacenamiento controlado (contiene RUC/metadatos), con hash/version/fechas y aprobacion. Retirar solo rol/host temporales propios segun procedimiento humano; no borrar DB/keyring ni alterar RC5. No se ejecuta este procedimiento por agentes.

## Platform despues de activacion autorizada

`GET /api/platform/fiscal-trust` y `GET /api/platform/fiscal-trust/preflight?page=1&pageSize=20` usan AppPolicies.PlatformAdmin + RequirePlatformContext y accessor vigente. Solo metadatos y cache; sin dependencia de provider privado, protector o evaluador en el servicio HTTP. No CRUD/wake/evaluacion viva. Tenant/admin no puede escribir autoridades; token Platform no sirve en endpoints tenant.

Monitor servidor descifra efimeramente material existente, extrae cadena publica y descarta claves; nunca cachea PFX/password/privatekey. Cache de decision liga CertID/SHA256/version/RUC/expiracion, UploadedAt, hash del ciphertext/password y settings. Un error se registra UNKNOWN y no impide recorrer otras empresas. Reinicio/rotacion/frescura vencida exige esperar observacion nueva, no activar a ciegas.

## GO/NO-GO, corte y rollback (no ejecutar)

GO requiere CI/review del HEAD exacto, validacion humana pre-merge, aprobacion R4 separada, runtime soportado/parcheado, backups/restore verificados, pin/procedencia/ledger anticompromiso, evidencia fresca autentica en todos los niveles de cada CA, cobertura TOTAL del preflight y baseline RC5 real comparado. NOT_CONFIRMED queda visible para revision, no se presenta como MATCH. Toda decision material/perfil desconocido requiere resolucion humana; OCSP obligatorio/no disponible = NO-GO. No afirmar compatibilidad real con Security Data u otro emisor sin material publico/perfil/preflight operativamente verificados.

Corte humano: ventana supervisada y cierre de admision segun runbook vigente; dejar terminar/reconciliar leases/recepciones sin reenviar ambiguos; fijar imagenes por digest; montar registro y claves protegidas; arrancar y comprobar health/readiness y cobertura monitor; validar humanamente cada empresa critica con procedimiento separado. Abrir operaciones solo tras GO explicito. La factura real 437 NO es experimento ni prueba automatizada de RC6.

Rollback: cerrar admision, detener candidato supervisadamente, preservar DB/keyring/XML/jobs y evidencia, volver a imagen/config RC5 por digest previamente verificado. Cero migraciones nuevas y formato cifrado/contratos de almacenamiento compatibles. No borrar intenciones durables, numeros, XML ni autorizaciones; reconciliar ambiguos bajo politica previa sin nueva firma. Un rollback de codigo no autoriza rebajar confianza/revocacion ni reenviar documentos; resolver el incidente con humano.

Alta futura de CA: mismo proceso independiente de procedencia/pins/perfil, nueva version de manifiesto, CRL/ARL y observacion aprobada, preflight total y nueva autorizacion de activacion. Sin cambios de producto si el perfil esta soportado. Perfiles no soportados requieren desarrollo/review separado, no cambiar flags hasta aparentar GOOD.

## Validacion humana pre-merge

VALIDACION HUMANA PRE-MERGE REQUERIDA, exclusivamente con datos sinteticos/DB efimera propia:

1. Dos jerarquias y tenants; revisar MATCH/MISMATCH/NOT_CONFIRMED y registro aprobado vs pin incorrecto.
2. GOOD/revoked/suspended/unknown/stale/falsificada, incluida revocacion de intermedio y ARL semestral con observacion vencida. Sin trafico externo.
3. Venta Produccion elegible y bloqueo previo sin numeracion/stock/caja; replay comprometido tras retiro de confianza.
4. Firma real factura/NC; hash/firma/identidad modificados bloqueados antes de recepcion; cero marker ante falla local. Rotacion no cambia XML persistido; consulta historica no firma/evalua otra vez.
5. Platform tenant-token rechazado, sin endpoints privados ni wake; monitor pagina todas las empresas, error aislado, cero writes/settings y UNKNOWN al cambiar snapshot.
6. CLI aislada con rol lector y keyring read-only: cero API/seed/workers y cobertura completa; baseline RC5 ausente permanece UNKNOWN. Aceptacion de Fernando pendiente, nunca inferida de CI.

## Fuentes primarias y limites pendientes

- [Microsoft CustomRootTrust/CustomTrustStore](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.customtruststore?view=net-8.0) y [DisableCertificateDownloads](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.disablecertificatedownloads?view=net-8.0).
- [.NET 8 CRL builder Load](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/CertificateRevocationListBuilder.Load.cs): parsear no equivale a autenticar. ExtraStore no contiene CRL.
- [RFC 5280 secciones 4/5/6](https://www.rfc-editor.org/rfc/rfc5280.html): cadena y CRL. RC6 implementa el subconjunto directo/completo descrito, no OCSP/deltas/indirectas universales.
- [Security Data representante legal V10, 25-Feb-2026, 4.9.6/4.9.7/4.9.10](https://www.securitydata.net.ec/wp-content/downloads/Normativas/P_de_Certificados/politica_certificacion_representante_legal.pdf): consulta por uso, distinta cadencia CRL/ARL y actualidad jerarquica; inferencia de diseno: separar edad de emision y observacion actual. Su aplicabilidad a certificados reales concretos esta PENDIENTE.
- [ARCOTEL: obligaciones CRL/OCSP de las entidades](https://www.arcotel.gob.ec/preguntas-frecuentes-relacionadas-con-las-entidades-de-certificacion/). No convertir la obligacion del emisor en una afirmacion de soporte OCSP implementado.
- [.NET soporte oficial](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): fin .NET 8 el 10-Nov-2026; upgrade a version soportada es prioridad independiente. No framework upgrade dentro de RC6 ni aceptacion automatica de imagen antigua por conservar digest.

Pendientes reales: CA/perfiles/certificados publicos reales, evidencias actuales CRL/ARL y canal de refresco/antirollback operativos, baseline y cobertura empresas reales, runtime de activacion, backup/restore/corte y validacion por empresa. Ninguno se ha verificado usando datos/infraestructura reales en esta implementacion.
