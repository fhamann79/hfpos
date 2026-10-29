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
- Docker engine local no disponible. No se inicio infraestructura ni se ejecuto PostgreSQL local. La nueva suite `ProductEditorTests` compila y queda **pendiente de ejecucion en CI PostgreSQL**, no se presenta como pasada localmente.

## Cobertura PostgreSQL para CI

Crear servicio y vender ticket/factura sin segunda edicion ni efecto fisico; guardado basico con nulls/pendientes y bloqueo de ticket; completar bien en el mismo registro; rollback de alta/edicion tras flush incluyendo costo/clasificacion; declaracion con revisiones previas e identidad/IVA finales; revisiones obsoletas; clasificacion nueva para desactivar inventario; saldo positivo/negativo; tenant/categoria ajenos; permisos separados y revocados durante espera; sesion revocada durante espera; dos guardados de la misma huella; altas concurrentes con identificador duplicado; no-op sin cambiar procedencia y secuencia compra 2->4, guardado fiscal/no-op, cancelacion (retorna 2), frente a intervencion manual explicita (conserva 4 o costo nuevo). Las carreras verifican bloqueo real en PostgreSQL.

## Validacion humana pre-merge

**VALIDACION HUMANA PRE-MERGE REQUERIDA. Resultado PENDIENTE.** Usar solo entorno de validacion autorizado y datos sinteticos, nunca produccion/SRI/certificados reales.

1. Crear servicio de copias: completar basicos, comprobar propuesta sin inventario, aportar declaracion/evidencia sintetica explicita, guardar completo una vez; vender ticket y preparar factura de prueba por flujo vigente. Verificar ausencia de stock ficticio y segunda edicion.
2. Crear bien fisico; comprobar pendiente de existencias y comportamiento habitual tras stock de prueba autorizado.
3. Guardar basico con codigos vacios, cambiar pestanas, completar Fiscal en el mismo ID. Verificar que Servicio con excepcion fisica de alta basica permite desmarcar inventario al completar sin otra operacion.
4. Con dos sesiones sinteticas editar nombre/precio/costo/IVA. Forzar conflicto; comparar sin perder borrador, preservar cambio remoto por defecto, seleccionar explicitamente diferencias, reconfirmar fiscal y guardar. No debe haber sobreescritura silenciosa.
5. Probar error de validacion, respuesta tardia, cierre/reapertura del mismo ID, cambio de contexto/token, doble clic y respuesta incierta. Reconciliar alta incierta mediante busqueda/seleccion sin POST repetido.
6. Con catalog-write sin fiscal-write guardar basico y comprobar rechazo de declaracion via API; comprobar bloqueo al desactivar inventario con stock distinto de cero. Verificar desktop/mobile y legibilidad del comparador.
7. Costo inicial 2, compra sintetica a 4, guardar solo Fiscal y cancelar compra: restaura 2. Repetir reafirmando/editando costo manualmente: conserva el costo manual explicitado.

## Revision y entrega

Los tres MAJOR comunicados por Linnaeus (recuperacion del conflicto, resincronizacion tras basico y procedencia del costo al guardar fiscal) fueron corregidos en lote con regresiones. Esto **no** equivale a revision aprobada ni a BLOCKER/MAJOR=0. Delta review del HEAD publicado, CI obligatorio exacto y aceptacion humana siguen pendientes y a cargo del coordinador. No cerrar issues ni hacer merge. Rollback de codigo: revertir el PR; no deshacer datos/historial generado mediante scripts ni asumir autorizacion de despliegue.
