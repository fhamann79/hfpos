# Issue 156: lote de precios comerciales

Refs #156. Base `e9a13d07ec139db62f56f2027da95c70f0af2797`. R3, NO MERGE.
Entrega acotada de precios; no declara terminado el issue ni el maestro comercial.

## Alcance e invariantes

- Cinco filtros AND compartidos con el selector fiscal: texto, categoria, activo,
  estado de declaracion e IVA NOMINAL del catalogo. No resuelve tasas efectivas.
- Conteo y filas en un snapshot RepeatableRead. Hasta 250 devuelve TODOS los
  resultados; mas de 250 devuelve conteo exacto y cero filas, nunca una pagina parcial.
- Precio base sin IVA: aumentar/disminuir porcentaje o USD, fijar precio y margen
  bruto objetivo `precio = costo / (1 - margen/100)`, `0 <= margen < 100`.
  No ofrece markup ni llama utilidad neta al margen bruto. El margen realizado
  se calcula sobre el precio final redondeado y puede diferir del objetivo.
- USD: precio final a dos decimales, `MidpointRounding.AwayFromZero`.
  Limite TECNICO del batch: precio actual, costo, precio propuesto y valor de regla
  entre cero y 1.000.000.000; regla con maximo cuatro decimales. No cambia los
  limites de DB ni editores existentes. Filas legadas fuera del limite rechazan
  el lote; importes actuales inseguros se omiten (`null`) con error explicito.
  Margenes se transportan y muestran como strings decimales, no `number`.
- Costo cero es un valor conocido. Para margen objetivo rechaza la fila;
  tambien rechaza precio derivado que redondea a cero. Fijar cero explicitamente
  sigue permitido con advertencias. Bajo costo, cero y cambio extremo son visibles.
- `CATALOG_PRODUCTS_READ` + `CATALOG_PRODUCT_PRICES_BATCH` server-side. No exige
  permisos fiscales ni `CATALOG_PRODUCTS_WRITE`; el permiso nuevo se incorpora
  al catalogo/seed existente y se asigna por defecto solo a admin.
- La lectura de costo sigue la politica actual del catalogo. Los editores legacy
  conservan `CATALOG_PRODUCTS_WRITE` no segregado: este PR NO pretende resolver
  esa limitacion ni cambia incidentalmente su autorizacion. No concede costo batch.
- Costos masivos EXCLUIDOS y pendientes de decision del propietario: Product.Cost
  revaloriza inventario actual (InventoryService/Dashboard), y ApplyManualCost
  altera la procedencia usada al cancelar recepciones. No se implementa una
  referencia comercial ficticiamente independiente. Precio/no-op NO crean eventos
  de costo ni modifican costo, revision o evento vigente.
- Tenant y contexto operativo autoritativos; comprobantes limitados a empresa,
  usuario, establecimiento y punto de emision. Escritura exclusiva por empresa,
  luego locks de productos ascendentes; transaccion atomica precio + auditoria.
- Proof Data Protection de 15 minutos ligado a seleccion, regla, motivo, actor y
  contexto. Fingerprint incluye ProductEditorService.Token (precio/costo, revision
  y evento de costo, clasificacion y campos comerciales) y filas revisadas.
- `InitialDataBatches.Kind = product-price-batch`; historial de importaciones
  conserva su allowlist de kinds. Recientes: ultimos 20 del actor/contexto.
- Confirmacion reintentada con MISMO OperationId/proof devuelve el comprobante,
  incluso tras expirar el preview. No aplica por segunda vez un incremento.
  La UI persiste intencion ANTES del HTTP en sessionStorage, ligada a empresa y
  usuario/contexto, siguiendo el patron checkout. Cierre/reapertura/recarga conserva
  el pendiente e impide otra previsualizacion hasta consulta/reintento del mismo
  lote. Un 404 de consulta NO prueba que no hubo commit. Solo rechazos explicitos
  seguros de preview invalid/changed/expired liberan el pendiente; conflicto de
  operacion, red, 5xx y otros errores NO. Storage ilegible/no disponible falla cerrado.
- Sin cambios en IVA, clasificaciones, SRI, documentos/XML historicos, stock,
  valoracion, movimientos, recepciones, migraciones ni snapshot EF.

## Evidencia automatizada local

Solo worktree `.worktrees/issue-156`; un escritor. Datos sinteticos, sin UserSecrets.
SDK completo usado: `%TEMP%/hfpos-155-local/dotnet/dotnet.exe` (.NET 10).
El SDK del PATH es 9; el antiguo SDK dev532 esta incompleto. Ninguno fue reparado.

PostgreSQL 18 existente del laboratorio: loopback `127.0.0.1:55475`, rol sintetico
`hfpos_test`, trust local. Se verifico que `hfpos_test_156` no existia antes de
crearla con la fixture. SOLO esta DB propia se recrea/reset; NO se abre ni modifica
`hfpos_test_528_smoke`, DB humana/persistente o produccion. Docker no fue reparado.

```powershell
$env:HF_POS_TEST_CONNECTION_STRING='Host=127.0.0.1;Port=55475;Database=hfpos_test_156;Username=hfpos_test'
$env:ASPNETCORE_ENVIRONMENT='Testing'
$env:SeedDemoData='false'
$sdk="$env:TEMP/hfpos-155-local/dotnet/dotnet.exe"
& $sdk test backend/Pos.Backend.Api/Pos.Backend.Api.Tests/Pos.Backend.Api.Tests.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~ProductPriceBatchTests|FullyQualifiedName~ProductPriceCalculationTests' --logger 'console;verbosity=minimal' -clp:ErrorsOnly
```

- 34 casos nuevos: calculos, bordes/rango/transporte, permiso sin fiscal,
  250 candidatos/247 seleccionados y excepciones, 251 sin truncamiento, aislamiento,
  cambios entre preview-confirm, doble confirmacion, rollback despues de flush,
  expiracion/replay, historial, recepcion/cancelacion y factura/XML historicos.
  Ultima corrida agrupada: 33/34 pasaron; el caso restante tenia una comparacion
  por referencia de arrays de advertencias. Corregida a comparacion estructural,
  su reejecucion focalizada paso 1/1; no cambia el comportamiento aplicativo.
- Regresion de extraccion: ProductFiscalBatchTests mas snapshot historico nuevo,
  12/12 pasaron. La extraccion conserva el contrato fiscal previo.
- Frontend: `npm test -- --watch=false --include='**/product-price-batch-dialog.spec.ts'
  --include='**/product-fiscal-batch-dialog.spec.ts'`: 21/21 pasaron.
- `npm run build`: Angular production + seguridad environments + landing-output.
- `npm audit --omit=dev --audit-level=high`: cero vulnerabilidades.
- Build backend Release con SDK completo: sin errores; warnings preexistentes y
  NU1900 local por acceso al feed de vulnerabilidades. CI final mantiene sus checks.
- `git diff --check`. No se duplican suites full locales; corresponden al CI del
  HEAD publicado. La revision independiente y CI final se registran en el PR.

## Laboratorio final y barrera humana

VALIDACION HUMANA PRE-MERGE REQUERIDA, una sola aceptacion final tras review/CI.
No se ha ejecutado ni aceptado el smoke humano de esta entrega. NO MERGE.

No parar/reconfigurar lab #177 en 4215/7096 mientras el coordinador congela sus
evidencias. No reutilizar su DB, keyring, usuario o sesion. Parametros para preparar
el laboratorio de #156 DESPUES de la coordinacion:

- HEAD: el commit publicado del PR #156, no main/RC9 ni una release nueva.
- DB propia `hfpos_test_156`, exclusivamente despues de finalizar todos los tests.
  La fixture ya aplica el schema en esta DB desechable. No volver a ejecutar tests
  contra ella cuando contenga evidencia pendiente de validacion humana.
- API normal en `Testing`, `SeedDemoData=false`, sin launch profile ni UserSecrets.
  `ConnectionStrings__DefaultConnection` debe ser la misma conexion sintetica anterior.
  `Jwt__Key` solo sintetica (32+ caracteres), issuer/audience `hfpos-156-lab`;
  DataProtection KeysPath propio y ApplicationName `hfpos-156-lab`.
  PlatformBootstrap sintetico puede crear un administrador de plataforma local
  para provisionar por API una empresa NUEVA `Synthetic 156`, nunca un perfil real.
  `Sri__AllowProductionSubmission=false`; TODAS las URLs SRI deben apuntar a
  `https://synthetic-sri.invalid/never-called`. Sin certificados ni envios.
- No ejecutar el modo legacy `Hfpos.FiscalSmokeHost --initialize`: esta fijado a
  `hfpos_test_528_smoke` y lo rechaza para #156. No debilitar ese guard.
- Usuario tenant admin NUEVO `batch-156-admin`, clave sintetica solo de lab;
  tenant provisioning usa TenantDefaults y obtiene el permiso nuevo. Crear ademas
  un rol `batch-156-price` con SOLO CATALOG_PRODUCTS_READ, CATALOG_PRODUCT_PRICES_BATCH
  y OP_STRUCTURE_READ (sin fiscal ni catalog write) y un usuario asociado.
- Seleccionar puertos libres propios (por ejemplo frontend 4216/API 7097) y alinear
  API URL/proxy del lab sin cambiar environments versionados. Alternativamente el
  coordinador puede autorizar reemplazar 4215/7096 una vez cerradas sus evidencias;
  esto no se ha hecho. Arrancar procesos ocultos, no ventanas interactivas.
- Cargar 228 productos SINTETICOS por el importador existente (no nueva importacion
  en este batch); incluir costo cero, precio bajo costo y costo diminuto 0.0001.
  La cuenta/empresa solo sirve para validacion; nunca usar contribuyentes reales.

Pasos del unico smoke humano final:

1. Entrar como usuario batch sin permisos fiscales. Combinar los cinco filtros,
   seleccionar todos los 228 y excluir dos; contador y preview completos (226).
2. Previsualizar incremento/decremento por porcentaje/importe, fijacion y margen.
   Verificar USD/redondeo, formula y margen realizado; costo cero/derivado cero
   rechazados para margen. Excluir las filas rechazadas, no llamarlas desconocidas.
3. Confirmar explicitamente un incremento. Ver comprobante, copiar referencia,
   recuperar por referencia y recientes. Revisar solo los seleccionados cambiados.
4. En entorno sintetico, simular respuesta perdida tras confirmar; cerrar/reabrir
   el dialogo. Debe recuperar/reintentar MISMO UUID/proof, sin habilitar otro preview
   ni duplicar el incremento. Cambiar precio/costo en otra sesion entre preview y
   confirm debe rechazar preview obsoleto sin aplicar parcialmente.
5. Usuario sin permiso batch: boton ausente y API 403. Confirmar costos/procedencia,
   recepciones, stock y documentos anteriores intactos. No ejecutar SRI.

Limitaciones residuales: sessionStorage es por pestana y se pierde si el usuario
lo borra expresamente; la recuperacion server-side por comprobante sigue disponible.
No prometer segregacion de costo en editores legacy ni costos masivos independientes.
Rollback de codigo no revierte precios confirmados: cualquier correccion exige
otro lote explicitamente revisado/autorizado; no modificar auditoria ni DB real.
