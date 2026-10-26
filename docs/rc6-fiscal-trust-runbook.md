# RC6 - aptitud de firma y diagnostico X.509 informativo

## Alcance y trazabilidad

DEV-531, issue #162, [PR #163](https://github.com/fhamann79/hfpos/pull/163), R3.
Base de la rectificacion: `c5c8539b478291d74fe2484f8277add3b10ff5b0`.
HEAD anterior: `2d31fbf4bb9ca34691321195982ba4bb1566e758`.
La primera implementacion RC6 contenia infraestructura propia de confianza PKI.
El propietario aprobo retirarla del mismo PR mediante commits correctivos normales.
La clasificacion previa se publico en el [comentario de auditoria](https://github.com/fhamann79/hfpos/pull/163#issuecomment-6417933451).
Este documento sustituye las instrucciones operativas de esa arquitectura descartada.
No autoriza merge, release, despliegue ni operaciones reales.

## Que Comprueba HF One

- Contexto tenant/establecimiento/punto, permisos y delegacion revocable server-side.
- Configuracion operativa, ambiente valido y habilitacion explicita de Produccion.
- Certificado activo: PFX descifrable, password correcta y firmante unico.
- Importacion efimera, PFX limitado a 2 MiB/33 certificados y concurrencia de importacion limitada.
- Clave RSA de 2048 a 8192 bits realmente utilizable: firma un desafio con SHA-256/PKCS#1 y la clave publica del certificado verifica el resultado.
- Vigencia real del certificado y contradiccion de identidad fiscal tipada. `NOT_CONFIRMED` es advertencia, no identidad acreditada.
- No se exige un algoritmo particular de firma del certificado/emisor. La compatibilidad exigida corresponde a la clave que firma el XML.
- Firma XAdES-BES existente, hash persistido, certificado embebido, referencias/transforms permitidos, clave de acceso y RUC coherentes.
- Integridad local de XML antes de admitir recepcion en ambos ambientes. Un fallo local no registra `ReceptionStartedAt` ni se convierte en recepcion incierta.
- Rechazo anterior a efectos comerciales; replay comprometido sin cargar otra vez el certificado. Locks, fencing, leases, caja, stock y numeracion conservan sus reglas.

Los certificados auxiliares importados se liberan; no se retienen para emitir.
No hay tablas ni migraciones nuevas. Ciphertext existente, keyring, `ApplicationName`
y purpose `SriCertificateProtectionPurposes.CertificateV1` no cambian.
No se requiere volver a subir certificados ya almacenados.

## Cadena Local: Solo Informacion

Solo readiness ejecuta `X509Chain.Build()`, despues de comprobar aptitud tecnica.
Usa el almacen estandar del SO, `RevocationMode=NoCheck`,
`VerificationFlags=NoFlag` y `DisableCertificateDownloads=true`.
No agrega raices, no usa AIA/descargas, ni consultas CRL/OCSP, ni cambia TLS global.
La operacion nativa es sincrona; no se afirma que un timeout asincrono la cancele.
No existe worker/cache/monitor de cadena, ni se ejecuta durante ventas, firma o transporte.

| Resultado | Severidad | Bloquea |
|---|---|---|
| `CERTIFICATE_CHAIN_BUILD_OK` | success | NO |
| `CERTIFICATE_CHAIN_BUILD_FAILED`, incluidos PartialChain/UntrustedRoot | warning | NO |
| `CERTIFICATE_CHAIN_DIAGNOSTIC_FAILED`, incluso excepcion | warning | NO |
| `CERTIFICATE_SELF_SIGNED` (codigo compatible; solo Subject=Issuer) | warning | NO |

Subject=Issuer no prueba autofirma. Cadena construida no prueba aceptacion del SRI.
Cadena fallida no se convierte en PASS: la advertencia se muestra honestamente.
`IsUsableForProduction` no contiene condiciones de cadena/autofirma y puede coexistir
con `IsReadyForProductionSubmission=true`, `BlockingErrorCount=0` y advertencias.
El frontend usa el booleano del backend y presenta la advertencia separadamente.
Un fallo de PFX/clave/vigencia/identidad sigue siendo error bloqueante, distinto de
una excepcion del diagnostico informativo de cadena.

Readiness solo lee; si faltan settings, informa `SRI_SETTINGS_MISSING` y no los crea.
Los valores conservadores transitorios (Pruebas/deshabilitado) no acreditan configuracion.

## SRI, Tenants e Historia

El SRI decide recepcion/autorizacion con sus propias reglas. XML bien firmado y
readiness positivo solo permiten intentar el flujo, no garantizan autorizacion.
Se conservan respuesta cruda, codigo/mensaje, fecha, intento y documento asociado.
Un rechazo determinista no crea otra factura ni consume otro secuencial.
Una respuesta ambigua conserva evidencia durable y pasa a consulta, sin reenvio ciego.
Consultar autorizacion/historia no construye cadenas, carga claves privadas ni refirma.
No se reescriben XML, claves o numeros historicos.

Cada empresa selecciona exclusivamente su certificado por CompanyId. Dos CA
diferentes no requieren aprobacion previa en HF One. El rechazo o material corrupto
de una empresa no cambia la otra. Platform conserva su autenticacion y separacion
de tokens/permisos; no tiene endpoints PKI ni acceso a material privado tenant.

## Clasificacion de los 43 Archivos Originales

API = `backend/Pos.Backend.Api/Pos.Backend.Api`; TEST = proyecto hermano `Pos.Backend.Api.Tests`.
Las acciones describen el tratamiento de la implementacion anterior, no los estados
Git finales (los renombres se cuentan desde el diff real).

| # | Archivo original | Tratamiento final |
|---|---|---|
| 1 | .github/workflows/ci.yml | RESTAURAR a main; fuera build CLI PKI |
| 2 | .github/workflows/container-images.yml | SIMPLIFICAR; conservar Debian sin red |
| 3 | Dockerfile.fiscal-trust-tests | REEMPLAZAR por Dockerfile.fiscal-signing-tests |
| 4 | Hfpos.FiscalPreflight/Hfpos.FiscalPreflight.csproj | ELIMINAR |
| 5 | Hfpos.FiscalPreflight/Program.cs | ELIMINAR |
| 6 | TEST/Infrastructure/AutonomousIssuingFixture.cs | SIMPLIFICAR; sin evaluador/cache |
| 7 | TEST/Infrastructure/FiscalTrustSyntheticPki.cs | REEMPLAZAR por SyntheticSigningCertificates |
| 8 | TEST/Integration/FiscalTrustRuntimeTests.cs | REEMPLAZAR por FiscalSigningRuntimeTests |
| 9 | TEST/Integration/PlatformControlPlaneTests.cs | RESTAURAR matriz; agregar no-PKI/no-acceso-certificados |
| 10 | TEST/Unit/FiscalTrustTests.cs | REEMPLAZAR por FiscalSigningTests |
| 11 | API/Configuration/FiscalTrustOptions.cs | ELIMINAR |
| 12 | API/Core/DTOs/FiscalTrustDtos.cs | ELIMINAR |
| 13 | API/Core/Models/FiscalTrustDecision.cs | ELIMINAR |
| 14 | API/Core/Models/SriSigningCertificateMaterial.cs | RESTAURAR ownership del firmante, sin auxiliares |
| 15 | API/Core/Services/FiscalTrustErrors.cs | REEMPLAZAR por FiscalCertificateErrors |
| 16 | API/Core/Services/IFiscalCertificateEvaluator.cs | ELIMINAR |
| 17 | API/Core/Services/IPlatformFiscalTrustService.cs | ELIMINAR |
| 18 | API/Infrastructure/Services/ElectronicIssuingCoordinator.cs | SIMPLIFICAR; verificar antes del marker |
| 19 | API/Infrastructure/Services/FiscalCertificateEvaluator.cs | ELIMINAR |
| 20 | API/Infrastructure/Services/FiscalExecutionGuard.cs | REEMPLAZAR por FiscalDocumentGuard esencial |
| 21 | API/Infrastructure/Services/FiscalPreflightCache.cs | ELIMINAR |
| 22 | API/Infrastructure/Services/FiscalPublicChainCache.cs | ELIMINAR |
| 23 | API/Infrastructure/Services/FiscalSignedXmlVerifier.cs | CONSERVAR y comprobar clave/RUC/ambiente |
| 24 | API/Infrastructure/Services/FiscalTrustMonitor.cs | ELIMINAR |
| 25 | API/Infrastructure/Services/FiscalTrustRegistry.cs | ELIMINAR |
| 26 | API/Infrastructure/Services/FiscalTrustSnapshots.cs | ELIMINAR |
| 27 | API/Infrastructure/Services/OfflineFiscalCrl.cs | ELIMINAR |
| 28 | API/Infrastructure/Services/PlatformFiscalTrustService.cs | ELIMINAR |
| 29 | API/Infrastructure/Services/SalesService.cs | SIMPLIFICAR; aptitud antes de efectos/replay primero |
| 30 | API/Infrastructure/Services/SriCertificateService.cs | CONSERVAR PFX/cifrado/buffers |
| 31 | API/Infrastructure/Services/SriCreditNoteSigningService.cs | SIMPLIFICAR; aptitud sin cadena |
| 32 | API/Infrastructure/Services/SriCreditNoteSubmissionService.cs | SIMPLIFICAR; integridad sin cadena |
| 33 | API/Infrastructure/Services/SriFiscalReadinessService.cs | REEMPLAZAR decision; cadena solo informacion |
| 34 | API/Infrastructure/Services/SriInvoiceSigningService.cs | SIMPLIFICAR; aptitud sin cadena |
| 35 | API/Infrastructure/Services/SriPfxReader.cs | CONSERVAR limites/efimero/firmante unico; liberar auxiliares |
| 36 | API/Infrastructure/Services/SriSigningCertificateProvider.cs | CONSERVAR CompanyId/DP/vigencia |
| 37 | API/Infrastructure/Services/SriSubmissionService.cs | CONSERVAR comprobaciones antes del marker |
| 38 | API/Program.cs | SIMPLIFICAR DI; sin configuracion/servicios PKI |
| 39 | API/Properties/AssemblyInfo.cs | CONSERVAR acceso interno acotado para tests |
| 40 | API/WebApi/Controllers/CreditNotesController.cs | SIMPLIFICAR codigos esenciales |
| 41 | API/WebApi/Controllers/PlatformFiscalTrustController.cs | ELIMINAR |
| 42 | API/WebApi/Controllers/SalesController.cs | SIMPLIFICAR codigos esenciales |
| 43 | docs/rc6-fiscal-trust-runbook.md | REEMPLAZAR contenido por este runbook |

Fuera de esos 43: se actualizan solo expectativas obsoletas de cadena/autofirma del
test heredado `SriFiscalReadinessCertificateIdentityTests`; se preservan todos sus
casos de identidad tipada. Se agrega una regresion de presentacion al spec de
`FiscalSettingsPage`, sin redisenar ni cambiar comportamiento UI.

## Matriz de Evidencia de los 43 Casos del Mandato

U = `Unit/FiscalSigningTests`; R = `Integration/FiscalSigningRuntimeTests`;
A = `Integration/AutonomousElectronicIssuingTests`; P = `Integration/PlatformControlPlaneTests`.
Los nombres abreviados identifican metodos reales. Esta matriz es trazabilidad de
cobertura, no declara PASS por existir un test. Resultados/contadores y SHA exactos
se publican con la evidencia de implementacion/CI del PR, nunca se heredan del HEAD anterior.

| Caso | Evidencia |
|---|---|
| 1 Cadena OK informativa | U.Chain_diagnostic_never_changes_signing_eligibility(success) |
| 2 PartialChain warning | U.Chain_diagnostic_never_changes_signing_eligibility(native) |
| 3 Raiz desconocida warning | U.Self_issued_unknown_root_and_unconfirmed_identity_are_only_warnings |
| 4 Excepcion no bloquea | U.Chain_diagnostic_never_changes_signing_eligibility(exception) |
| 5 Material inutilizable omite cadena | U.Unusable_material_blocks_and_skips_chain_diagnostic |
| 6 Clave privada bloquea | U.Private_key_must_sign_and_match_the_certificate_public_key; U.Unusable_material(public) |
| 7 Vigencia bloquea | U.Unusable_material(expired/future); R.Production_rejects_before_sequence_stock_cash_or_durable_job |
| 8 Contradiccion bloquea | U.Unusable_material(identity); suite SriFiscalReadinessCertificateIdentityTests intacta |
| 9 No confirmada warning | U.Self_issued_unknown_root_and_unconfirmed_identity_are_only_warnings |
| 10 IsUsable independiente | U.Chain_diagnostic_never_changes_signing_eligibility |
| 11 IsReady independiente | R.PartialChain_readiness_is_production_ready_with_consistent_counters_and_no_writes |
| 12 BlockingErrorCount | mismo R: cero |
| 13 WarningCount | mismo R: igual al numero de checks warning; spec FiscalSettingsPage |
| 14 Firmar sin cadena | U.PartialChain_allows_actual_signature_and_mock_SOAP_transport_without_network |
| 15 XML verificable | U.Actual_signer_roundtrip_and_tamper_guards |
| 16 XML manipulado | mismo U; R.Local_reception_failure_has_zero_transport_calls_and_no_false_ambiguity |
| 17 Clave incorrecta | U.Private_key_must_sign_and_match_the_certificate_public_key; PFX password incorrecta |
| 18 Expirado | U.Unusable_material(expired); R.Production_rejects...(expired) |
| 19 Algoritmo incompatible | U.Unusable_material(ecdsa/weak-rsa); R.Production_rejects...(unsupported) |
| 20 Emisor contradictorio | U.Unusable_material(identity); R.Production_rejects...(identity); verifier RUC |
| 21 Sin secuencial al rechazar | R.Production_rejects_before_sequence_stock_cash_or_durable_job |
| 22 Sin stock/caja al rechazar | mismo R |
| 23 Replay idempotente | R.Committed_replay_does_not_load_replaced_certificate_or_require_guard; A.Sale_outbox_stock_failure_rolls_back_all_effects_and_replay_has_one_job |
| 24 Transporte simulado sin PKI | U.PartialChain_allows_actual_signature_and_mock_SOAP_transport_without_network; R.Production_sign_reception_rotation_and_historical_authorization_without_private_material |
| 25 Rechazo firma SRI registrado | R.Mock_rejection_or_ambiguous_reception_preserves_document_and_number(DEVUELTA) |
| 26 Rechazo no duplica numero | mismo R; A.Pending_backoff_is_bounded_rejection_is_terminal_and_disabling_preserves_documents |
| 27 Ambiguedad durable | R.Mock_rejection_or_ambiguous_reception...(AMBIGUOUS); A.Ambiguous_remote_accept_and_eventual_not_found_are_query_only_never_resubmitted_even_manually |
| 28 Autorizacion sin refirma | R.Production_sign_reception_rotation_and_historical_authorization_without_private_material |
| 29 Dos CA/tenants funcionales | R.Two_tenants_with_different_uninstalled_authorities_sign_independently_and_keep_ciphertext |
| 30 PFX aislados | mismo R: seleccion B devuelve exclusivamente B con keyring compartido |
| 31 RUC contradictorio | U.Unusable_material(identity); suite de identidad tipada |
| 32 Contextos/permisos | P.Platform_and_tenant_tokens_never_cross_planes_and_existing_restrictions_remain; A.Cashier_actual_jwt_cannot_manually_sign_submit_query_or_change_company_delegation |
| 33 Platform sin material privado | mismo P: GET/POST/DELETE certificado denegados; endpoints PKI inexistentes |
| 34 Ciphertext compatible | R.Two_tenants...; R.Ambiguous_upload_preserves_active_cipher_and_settings |
| 35 Keyring/purpose compatible | mismo R, purpose sin cambios; Containers/recovery restaura DB/keyring sinteticos y prueba app |
| 36 XAdES factura | U.Actual_signer_roundtrip...; SriXadesBesSignerTests(factura) |
| 37 XAdES NC | mismo U(notaCredito); R.Credit_note_signing_reception_and_history_preserve_integrity |
| 38 Sin migraciones | diff sin Migrations/modelo; CI EF migrations/model check |
| 39 Sin FiscalTrust config | Program/DI y diff; CI arranque API/Containers sin esa seccion |
| 40 API inicia sin PKI | P (WebApplicationFactory real); CI Containers/recovery |
| 41 Historia legible | R.Production_sign_reception...; A.Legacy_known_reception_can_be_queried_but_definitive_rejection_stays_terminal; R.Credit_note_signing_reception_and_history_preserve_integrity |
| 42 Sin bloqueo indirecto | U y R anteriores; busqueda X509Chain/PKI en codigo/DI; revision independiente |
| 43 Debian PartialChain+firma+mock | Dockerfile.fiscal-signing-tests ejecuta U.PartialChain_allows_actual_signature_and_mock_SOAP_transport_without_network con --network none |

Debian prueba criptografia nativa, diagnostico y cliente SOAP con HttpMessageHandler
simulado: no DB ni red. La durabilidad/transacciones se prueban por separado en CI
Ubuntu con PostgreSQL efimero. No se afirma un flujo PostgreSQL dentro de Debian.
La imagen de pruebas nunca es imagen de release. Se conservan todos los checks
obligatorios de backend/frontend, Governance, EF y Containers/recovery.

## Gate Humano Pre-Merge

**VALIDACION HUMANA PRE-MERGE REQUERIDA**. No merge por este implementador.
Tras CI exacta y revision independiente sin BLOCKER/MAJOR, Fernando acepta con
`VALIDADO OK` el cambio de politica funcional en un entorno exclusivamente sintetico:

1. Abrir readiness: cadena local incompleta se muestra como advertencia, sin falso bloqueo si el resto esta correcto.
2. Confirmar que el permiso de intentar emision no se presenta como autorizacion SRI.
3. Aceptar el comportamiento demostrado de rechazo local sin efectos/marker y rechazo/ambiguedad SRI sin duplicacion.

No se pide repetir suites tecnicas, crear multiples ventas ni usar Produccion.
El coordinador conserva la evidencia de CI/revision y las decisiones pendientes.

## Futuro Runbook Humano R4: Primera Factura

Esta seccion es preparacion, NO autorizacion de ejecucion. Solo un humano, con
aprobacion separada del propietario, operara infraestructura/datos/certificados reales.
No aportar secretos, conexiones, certificados o copias reales al sandbox del agente.

1. Verificar merge real, SHA main, CI, aceptacion humana y release aprobada; identificar imagen por digest, no solo tag.
2. Inventariar version/configuracion actuales y ventana de mantenimiento. Respaldar PostgreSQL y keyring compatible juntos, con permisos y custodia humanos.
3. Verificar recuperabilidad/restauracion del backup con el procedimiento humano aprobado. Guardar digest anterior, configuracion y plan de retorno. No borrar claves antiguas.
4. Verificar compatibilidad con empresas existentes, configuracion Produccion y autorizacion operativa. Revisar pendientes/recepciones ambiguas antes del cambio; no reenviarlas.
5. Desplegar exclusivamente bajo autorizacion R4 separada. RC6 no necesita migraciones nuevas, autoridades, manifiestos ni renovaciones de listas.
6. Verificar salud HTTP/backend, contexto de la empresa y disponibilidad del keyring/certificado existente sin exponer PFX/password.
7. Entrar a HF One y consultar readiness. PartialChain debe ser solo advertencia. Confirmar aptitud real, identidad/configuracion y permisos/delegacion deseados.
8. Consultar el secuencial vigente inmediatamente antes de emitir. 437 fue una expectativa historica, no un numero reservado ni autorizado; no forzarlo ni usar ventas de prueba en Produccion para diagnosticar cadena.
9. Confirmar producto, valores e identificacion comercial fuera de artefactos de desarrollo. Emitir una sola vez, unicamente con autorizacion expresa del propietario.
10. Consultar hasta conocer recepcion/autorizacion. Conservar XML exacto, respuestas, intentos y autorizacion/RIDE cuando exista. Un rechazo no autoriza otra factura automatica.
11. Ante rechazo o estado ambiguo, detener nuevas operaciones afectadas y conservar evidencia; consultar por la clave original, no refirmar/renumerar/reenviar ciegamente.

Rollback humano: detener admisiones nuevas segun el runbook aprobado, preservar
evidencia externa y pendientes, volver al digest/configuracion compatibles si es
seguro. No restaurar una DB antigua sobre documentos enviados al SRI sin reconciliar
primero esas emisiones: podria perder evidencia y reutilizar secuenciales.
Restaurar PostgreSQL/keyring solo por el plan humano probado, con backup del estado
actual, reconciliacion y autorizacion expresa. No hay rollback destructivo autonomo.

## Riesgos Residuales y No Objetivos

- No se acredita confianza fiscal/estado de revocacion local; el SRI puede rechazar certificado, firma o documento.
- Identidad NOT_CONFIRMED sigue sin demostrarse; no significa MATCH.
- La recuperacion transaccional de notas de credito conserva un riesgo previo conocido; esta rectificacion no reescribe su concurrencia.
- La plataforma sigue en .NET 8; su evolucion/soporte es trabajo separado, sin upgrade en RC6.
- No incluye CA CRUD, OCSP, monitor global, nuevas reglas IVA/roles, nuevos productos, backup automatico, RC7 ni redisenos UI.
- No se ha usado ni se autoriza aqui entorno/dato/certificado/SRI real.
