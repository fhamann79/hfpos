# BE-FE-535 - Alta y edicion unificada de producto/servicio

Refs #137 y DEV-532 #134. Adelanto acotado aprobado, no activacion del maestro completo.
Base reconciliada: `ea633ce19b1f19f5e769c817b2e08f6404ad7230`.
Riesgo: **R3**. Estado: **PENDIENTE**, sin merge ni despliegue.

## Contrato e invariantes

- `GET /api/Products/editor/{id}` entrega producto, revisiones, clasificacion visible segun permiso y huella de estado coherentes bajo el lock de empresa existente.
- `POST /api/Products/editor` y `PUT /api/Products/editor/{id}` componen producto, evento de costo y declaracion fiscal opcional en una sola transaccion. Los endpoints anteriores conservan su contrato.
- `CatalogProductsWrite` es obligatorio; una declaracion adicional exige `FiscalSettingsWrite`. La evidencia fiscal de respuesta exige `FiscalSettingsRead`. El contexto/sesion y los permisos se revalidan bajo lock, incluso tras una espera.
- El PUT exige la huella completa anterior (incluye identidad, precio, costo/revision/evento, categoria, IVA, inventario, activo y revision de clasificacion). La declaracion comprueba revisiones PREVIAS y se construye con identidad/IVA/revision FINALES.
- Se reutilizan `FiscalTaxResolver`, `ProductInventoryPolicy`, el control de saldo distinto de cero en todos los establecimientos, los cost events y el historial de declaraciones. No se anidan transacciones del servicio de declaracion ni se llama una cadena HTTP para guardar.
- Guardar solo fiscal/otros datos o un no-op no crea un cost event manual ni cambia la procedencia del costo de compra. Un valor de costo distinto o `setManualCost=true` expresa intervencion manual; la UI envia la marca solo cuando se edito el control de costo, incluso si se reafirma el mismo valor. Reconciliar conserva esa intencion solo al seleccionar el costo del borrador.
- Sin migraciones/model changes ni normalizacion ficticia de datos ausentes: categoria/nombre/precio/costo/minimo e IVA siguen el contrato actual; identificadores opcionales siguen siendo null. La naturaleza elegida es un borrador hasta declarar y confirmar hechos/evidencia.
- Guardar basico no habilita automaticamente venta ni factura. Los pendientes de catalogo usan el resolver fiscal existente, tambien para ticket. Caja, cantidad real, comprador y requisitos adicionales de emision se revalidan en el flujo de venta; esta lectura no promete elegibilidad total.

## UI y recuperacion

- Tres pestanas en un contexto; Servicio propone sin inventario, con excepcion explicita para inventario fisico. Guardado basico mantiene el ID y el borrador fiscal para completar mediante PUT.
- Edicion carga autoridad actual, no datos de tabla combinados con clasificacion reciente. Diferencias con la tabla requieren revision explicita.
- Generation + company/establishment/emission point/token protegen cargas, errores y guardados, incluida reapertura del mismo ID. Se bloquea doble clic y cierre durante guardado.
- Un 409 conserva valores y permite comparar estado actual. Cada campo permite elegir borrador/actual; cambios concurrentes ganan por defecto. Reconciliar no escribe: actualiza la base optimista tras seleccion humana y exige nueva confirmacion fiscal. Otro cambio posterior vuelve a producir conflicto.
- Un error de red/5xx es resultado incierto: no reintento automatico. Para alta, busqueda acotada de coincidencias por codigo/nombre y seleccion humana de un registro permiten comparar y continuar mediante PUT, sin repetir POST.
- Limite deliberado sin esquema nuevo: POST no ofrece exactly-once durable para solicitudes sin identificadores. Codigos opcionales presentes usan unicidad tenant existente y conflictos, no se convierten en claves de idempotencia ficticias. Ausencia de coincidencias no autoriza automaticamente otra alta.

## Evidencia local ejecutada

- `dotnet build backend/Pos.Backend.Api/Pos.Backend.Api.sln --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false -v quiet -clp:ErrorsOnly`: correcto, sin errores; avisos preexistentes fuera del alcance.
- `dotnet test backend/Pos.Backend.Api/Pos.Backend.Api.Tests/Pos.Backend.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ProductFiscalDeclarationTests|FullyQualifiedName~FiscalTaxResolverTests|FullyQualifiedName~CriticalOperationContractTests" --disable-build-servers -p:UseSharedCompilation=false -v quiet -clp:ErrorsOnly`: 28/28. Compila API y todos los tests, incluidas regresiones M3, sin conectarse a DB.
- `npm test -- --watch=false --include='src/app/features/catalog/components/products/*.spec.ts' --include='src/app/features/fiscal-settings/**/*.spec.ts'`: 42/42, mas guardas de environments.
- `npm run build`: correcto, incluidas tres comprobaciones de environments/bundle.
- `npm audit --omit=dev --audit-level=high`: cero vulnerabilidades runtime. npm ci aviso de dependencias de desarrollo/deprecacion existentes; no se actualizan paquetes.
- `git diff --check`: correcto.
- Al congelar `7c62c3f5843603b53a6fbfb58ec15219b183f979`, Docker engine local no estaba disponible; aquella evidencia no incluia ejecucion PostgreSQL local. Ver correccion y ejecucion posterior abajo.

## Correccion del fallo CI en PR167

- CI `40935068268`, job `123663326446`, sobre `7c62c3f`: 847/848 PASS; fallo unico al completar el alta basica por `PRODUCT_EDITOR_STATE_CONFLICT`. Frontend y Containers `40935068155` verdes en ese SHA, no se presentan como CI del siguiente HEAD.
- Causa real, no fixture: la huella usaba JSON de decimales con escala CLR. EF reload puede conservar la representacion anterior de un valor numericamente igual, mientras un contexto nuevo lee la escala PostgreSQL (`numeric(18,2)` / `numeric(18,4)`). POST y GET entregaban valores iguales pero JSON/huellas distintos sin ninguna escritura intermedia.
- Correccion limitada a la huella: representacion decimal `G29` con `InvariantCulture`, sin redondear ni perder precision. Se conservan todos los campos, revisiones, procedencia de costo, comparacion optimista y locks. No se agregan reintentos ni se debilitan asserts/politicas de stock.
- Reproduccion antes del fix: caso original y dos regresiones POST->GET (basico/completo) fallaron; estas ultimas fallaron especificamente al comparar las huellas, despues de comprobar igualdad numerica y diferencia JSON.
- Tras el fix: `dotnet test backend/Pos.Backend.Api/Pos.Backend.Api.Tests/Pos.Backend.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ProductEditorTests" --disable-build-servers -p:UseSharedCompilation=false -v quiet -clp:ErrorsOnly --logger "console;verbosity=normal"`: **31/31 PASS**. Compila API/tests Release. Incluye cinco casos nuevos: escala POST/GET y siguiente PUT para basico/completo, mas rechazo de huella stale por cambios reales de precio (0.01), costo (0.0001) y minimo (0.0001). Tambien pasan stock, rollback, carreras, permisos/sesion y compra/cancelacion existentes.
- Ejecucion aislada: `ASPNETCORE_ENVIRONMENT=Testing`, `SeedDemoData=false`, `HF_POS_TEST_CONNECTION_STRING` apunta exclusivamente a PostgreSQL 16 efimero propio, DB `hfpos_test_pr167_corrector`, puerto loopback dinamico. Sin UserSecrets, DB persistente/shared, datos reales, certificados ni SRI. No se modifican archivos de environment ni recursos del entorno del coordinador.
- `git diff --check`: correcto. Sin cambios frontend, framework, contrato publico, modelo ni migraciones. El CI obligatorio y delta-review deben cubrir el nuevo HEAD publicado; esta evidencia local no los sustituye.

## Cobertura PostgreSQL para CI

Crear servicio y vender ticket/factura sin segunda edicion ni efecto fisico; guardado basico con nulls/pendientes y bloqueo de ticket; completar bien en el mismo registro; rollback de alta/edicion tras flush incluyendo costo/clasificacion; declaracion con revisiones previas e identidad/IVA finales; revisiones obsoletas; clasificacion nueva para desactivar inventario; saldo positivo/negativo; tenant/categoria ajenos; permisos separados y revocados durante espera; sesion revocada durante espera; dos guardados de la misma huella; altas concurrentes con identificador duplicado; no-op sin cambiar procedencia y secuencia compra 2->4, guardado fiscal/no-op, cancelacion (retorna 2), frente a intervencion manual explicita (conserva 4 o costo nuevo). Las carreras verifican bloqueo real en PostgreSQL.

## Validacion humana pre-merge

**VALIDACION HUMANA PRE-MERGE REQUERIDA. Resultado PENDIENTE.** Usar solo entorno de validacion autorizado y datos sinteticos, nunca produccion/SRI/certificados reales.

Una sola sesion de formulario, acotada por Fernando. El coordinador prepara el entorno sintetico y el acceso necesarios antes de la sesion.

1. En Catalogo, abrir el alta e ingresar un servicio de prueba (por ejemplo, COPIA SINTETICA), categoria, precio y costo. Dejar codigo interno y codigo de barras vacios. Pulsar **Guardar basico**.
2. Sin salir del mismo formulario ni crear otro registro, ir a **Fiscal**, elegir **Servicio** y completar/confirmar la declaracion con hechos y evidencia sinteticos proporcionados para la prueba. En **Inventario**, comprobar que queda sin control de inventario.
3. Pulsar **Guardar completo**. Comprobar que se conserva el mismo registro y los datos ingresados, sin duplicados, stock ficticio ni otra pantalla para terminar el servicio; los pendientes deben ser claros y coherentes con lo guardado.

No se pide repetir compras/cancelaciones, bienes/stock, permisos negativos, aislamiento, carreras/comparador, latencias, doble clic ni respuestas inciertas como pruebas humanas. Esos casos corresponden a las pruebas automatizadas PostgreSQL/UI y al CI obligatorio del HEAD final. La unica aceptacion humana pendiente es el recorrido de servicio anterior; no se inventa su resultado.

## Revision y entrega

Linnaeus cerro M1/M2/M3 sobre `7c62c3f5843603b53a6fbfb58ec15219b183f979` con BLOCKER=0 / MAJOR=0; la revision externa del mismo SHA no reporto findings. Esa evidencia se conserva para el alcance no modificado, pero no se afirma que cubra automaticamente esta correccion. Delta-review proporcional del cambio y su impacto, CI obligatorio del nuevo HEAD y aceptacion humana siguen pendientes y a cargo del coordinador. No cerrar issues ni hacer merge. Rollback de codigo: revertir el PR; no deshacer datos/historial generado mediante scripts ni asumir autorizacion de despliegue.
