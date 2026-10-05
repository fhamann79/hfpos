# BE-FE-529: critical operations reliability

Issue #131; roadmap #127 secciones 7 y 14.
Base `35692de5fcb1dad7ee5e9b9c6ec66fa44df48772`. R3.
**VALIDACION HUMANA PRE-MERGE REQUERIDA**. Review, CI exacto HEAD y aceptacion pendientes.
No merge por el implementer. No datos reales, DB compartida, secretos, certificados de firma ni SRI.

## Contratos e invariantes

- Receipt CREATE, entry/exit/adjust manuales y cash-in/out manuales requieren RequestId UUID no vacio.
  Nuevo intento = nueva clave; retry = misma clave y payload/contexto original.
  La misma clave por Company y familia con payload/actor/establecimiento/punto diferentes devuelve
  409 REQUEST_CONFLICT sin entregar el DTO original. Otra Company tiene namespace independiente.
- Fingerprint SHA256 versionado: texto opcional trim/blank-null; cantidades/costos de compra e
  inventario a 4 decimales; caja a 2. Decimales equivalentes tienen representacion canonica.
  Receipt mantiene orden original y lineas duplicadas porque afectan procedencia de costo.
  Fecha ausente es marcador estable, no el reloj del retry. Adjustment incluye todos los Expected*.
- Company FOR UPDATE primero, contexto/sesion/rol actuales, replay antes de validaciones dinamicas,
  productos ordenados y stock, efectos y metadata en la misma transaccion. Sin HTTP bajo locks.
  Controllers nuevos delegan la idempotencia de compras a PurchaseReceiptService.
  Cancel/cost journal 506, Sales/POS 527 y negocios Transfer/Settlement no se reescriben.
- Replay devuelve IDs y snapshots persistidos originales; compra cancelada sigue cancelada.
  Caja devuelve estado/totales autorizados actuales o cierre persistido, no acumuladores inventados
  del momento del primer POST. CashMovement.RequestId identifica el efecto recuperado.
- Apertura agrega RequestId OPCIONAL para mantener callers antiguos y constraint de una caja ACTIVE.
  La UI nueva siempre lo usa: open committed + reply lost + later close deja GET current = null,
  que NO prueba ausencia de commit; otro POST sin identidad duplicaria la intencion historica.
  La clave recupera la sesion original incluso cerrada, aunque exista una apertura posterior.
  Cierre NO agrega clave/esquema: target ID inmutable + Open->Closed ya protege at-most-once;
  recuperacion GET por ID muestra los valores registrados, sin afirmar que otro conteo coincidia.
- Migracion unica 20261005003734_AddCriticalOperationRequests: metadata nullable de legado en
  PurchaseReceipts, InventoryMovements, CashMovements y CashSessions; hash64, checks paired-null,
  UUID no vacio, punto original positivo donde faltaba y unique filtrado (CompanyId, RequestId).
  Solo movimientos manuales SourceType 1/2/3 llevan claves; efectos internos permanecen NULL.
  Sin backfill, DML historico, costo actual inventado, eliminacion de indices previos ni cambios fiscales.
- UI persiste una intencion inmutable por familia/actor en sessionStorage antes del POST.
  Doble click no envia dos solicitudes. Mientras incierta no cambia UUID/payload ni resetea el formulario;
  navegar/restaurar conserva el intento. Cambiar contexto/actor bloquea el retry hasta volver al original.
  HTTP desconocido, 5xx, 408, conflict de identidad o 2xx incompleto NO liberan la intencion.
  Rechazo definitivo inicial permite corregir; rechazo de auth tras incertidumbre NO borra el intento.
  No limpiar storage de una operacion incierta para crear una nueva.
- Receipt GET A/B no retargetea cancelacion: target independiente, contexto y generaciones.
  Kardex lista/detalle latest-wins; close/destroy invalidan next/error/loading viejos.
  Caja congela targets de movimiento/cierre y GET current tiene generaciones.
  Settlement congela fecha/metodo/payload; Transfer conserva su RequestId y reglas existentes.
  No auditoria global de UI ni nuevas policies.

## Evidencia automatizada

Ver PR/comentarios para SHA y resultados exactos. Pruebas escritas no equivalen a PASS:
CriticalOperationReliabilityTests requiere PostgreSQL 16 real sin skips; incluye replay/conflict,
rollback de segunda linea/journal/stock, orden duplicado/cancel/inactive, snapshot stale original
vs nueva clave, tenant/actor/punto/establecimiento, versiones revocadas, apertura cerrada recuperada,
cash movement/close en ambos ordenes y ausencia de stock/salidas distintas sin piso negativo.
Las carreras usan SqlCommandGateInterceptor existente y observador independiente pg_blocking_pids.
Unit tests sin PG comprueban rechazos de contrato y atributos de policy (NO sustituyen auth HTTP).
Frontend comprueba storage/snapshot, network ambiguity, actor/contexto, respuesta incompleta,
generaciones/cierre/destroy y contratos existentes Transfer/Settlement.
Docker local OFF: **PG LOCAL NO**, CI real obligatorio. Ninguna DB local fue migrada.
--verify-configuration usa CreateBuilder/HostingStartup y options reales sin Build/Run/listener/DB;
no WebApplicationFactory/content-root que oculte configuracion incompleta.

## UN smoke humano sintetico pre-merge

Solo Fernando ejecuta estos comandos despues de review y CI exacto HEAD.
Registrar HEAD/PR, RequestIds e IDs originales y resultado de todo el recorrido.
Requiere Docker encendido POR EL HUMANO, .NET 8 y Node; terminal en este worktree.
No tocar `hfpos-528-smoke-pg`, su DB/keyring ni servicios existentes. Si un puerto/nombre esta
ocupado, detener este smoke y resolver disponibilidad humana, no reemplazar recursos ajenos.

### Preparacion descartable

```powershell
git rev-parse HEAD
Get-NetTCPConnection -State Listen -LocalPort 54929,7096,4200 -ErrorAction SilentlyContinue
docker ps -a --filter name=hfpos-be-fe-529-tests
# Continuar solo si esos puertos/nombre estan libres.
docker run --name hfpos-be-fe-529-tests -e POSTGRES_USER=hfpos_test -e POSTGRES_PASSWORD=hfpos_test_only_529 -e POSTGRES_DB=hfpos_test_529 -p 127.0.0.1:54929:5432 -d postgres:16-alpine
docker exec hfpos-be-fe-529-tests pg_isready -U hfpos_test -d hfpos_test_529
$env:HF_POS_TEST_CONNECTION_STRING='Host=localhost;Port=54929;Database=hfpos_test_529;Username=hfpos_test;Password=hfpos_test_only_529'
$env:HF_POS_SMOKE_WORKDIR=Join-Path $env:TEMP 'hfpos-529-synthetic-smoke'
dotnet build backend/Pos.Backend.Api/Pos.Backend.Api.sln --configuration Release
dotnet run --project backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost --configuration Release --no-build --no-launch-profile -- --critical-operations --verify-configuration
dotnet run --project backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost --configuration Release --no-build --no-launch-profile -- --critical-operations --initialize
```

Si pg_isready aun no acepta, esperar y repetir ese comando antes de inicializar.
--initialize BORRA y recrea SOLO hfpos_test_529. NO usarlo al reiniciar el host.
El host separado no se referencia ni publica desde la API/container productivos.
Su startup descarta appsettings/overrides inseguros, fuerza Testing, SeedDemoData=false,
SRI synthetic-sri.invalid con transportes interceptados y produccion bloqueada.
No configura certificados SRI/SMTP ni ejecuta fiscalidad. TLS es efimero sintetico localhost.

En segunda terminal del MISMO worktree:
```powershell
Set-Location frontend/pos-frontend
npm ci
npm start -- --host localhost --port 4200
```
Abrir primero https://localhost:7096/swagger y aceptar SOLO su TLS localhost sintetico;
luego http://localhost:4200. Usuario `cashier-critical-529`, password `synthetic-only-529-password`.
Tenant/punto/IDs se imprimen al inicializar; productos inician con stock 10, costo proveniente 2,
un proveedor y destino 002 sinteticos. No usar usuarios existentes.

### Control puntual de transporte (DevTools de UI)

Pegar una vez en Console, solo con el host sintetico anterior. Funciona con fetch y XHR.
Armar UN request antes de la accion de UI; no afecta OPTIONS ni otras rutas.
GET demora la respuesta ya calculada; POST drop aborta solo DESPUES de finalizar un 2xx.
Un rechazo 4xx no se pierde. Recargar pagina elimina estos hooks; no elimina sessionStorage.

```javascript
(() => {
  const fetch0 = window.fetch, open0 = XMLHttpRequest.prototype.open;
  const send0 = XMLHttpRequest.prototype.send, meta = new WeakMap();
  let armed = null;
  window.arm529 = (method, path, mode, ms = 5000) => {
    armed = { method: method.toUpperCase(), path, mode, ms };
  };
  const take = (method, url) => {
    const target = new URL(url, location.href);
    if (!armed || target.hostname !== 'localhost' || target.port !== '7096'
      || method.toUpperCase() !== armed.method || target.pathname !== armed.path) return null;
    const value = armed; armed = null;
    return value.mode === 'drop' ? ['X-Hfpos-Smoke-Drop', 'after-commit']
      : ['X-Hfpos-Smoke-Delay', String(value.ms)];
  };
  XMLHttpRequest.prototype.open = function(method, url, ...args) {
    meta.set(this, { method, url: String(url) });
    return Reflect.apply(open0, this, [method, url, ...args]);
  };
  XMLHttpRequest.prototype.send = function(body) {
    const value = meta.get(this), header = value && take(value.method, value.url);
    if (header) this.setRequestHeader(...header);
    return Reflect.apply(send0, this, [body]);
  };
  window.fetch = function(input, init) {
    const request = new Request(input, init), header = take(request.method, request.url);
    if (!header) return Reflect.apply(fetch0, this, [request]);
    const headers = new Headers(request.headers); headers.set(...header);
    return Reflect.apply(fetch0, this, [new Request(request, { headers })]);
  };
})();
arm529('POST', '/api/PurchaseReceipts', 'drop');
```

Usar la ruta exacta del request Network (case-sensitive en el hook):
`/api/inventory/entry`, `/api/inventory/exit`, `/api/inventory/adjust`,
`/api/CashSessions/open`, `/api/CashSessions/<ID>/movements`,
`/api/CashSessions/<ID>/close`, `/api/inventory/transfers`, `/api/PaymentSettlements`.
Para GET: `arm529('GET', '/api/PurchaseReceipts/<A>', 'delay')` o
`/api/inventory/movements` (query ignorado), `/api/inventory/movements/<ID>`,
`/api/CashSessions/current`, `/api/PaymentSettlements/reconciliation`.
Confirmar header/drop/delay en Network. Si no se aplico, NO declarar ese paso probado.

### Recorrido unico y aceptacion

1. Crear recepcion A con drop after-commit, doble click y cierre mientras pendiente.
   Ver error incierto/formulario retenido; registrar UUID/body. Recuperar y comprobar misma A,
   stock/costo/lineas una vez. Repetir mismo body/UUID por Network sin hook: mismo ID.
   Cambiar cantidad con misma UUID: 409 REQUEST_CONFLICT, ningun efecto nuevo.
   Crear B normal. Demorar GET A; abrir B/abrir cancelar B; dejar llegar A.
   Confirmar cancela SOLO B; A sigue Posted. Replay CREATE B devuelve B Canceled, no repone stock/costo.
2. Inventario: entry +2, exit -1 y adjust al conteo correcto con snapshot actual, cada uno con drop.
   Recuperar cada UUID/body; una fila por clave, StockBefore/After originales.
   Navegar/recargar tras error: mismo intento. Snapshot stale con NUEVA clave debe rechazarse;
   el mismo ajuste ya confirmado debe recuperarse aun con snapshot viejo.
   Kardex: demorar pagina/detalle A, pedir B, cerrar detalle o navegar antes de recibir A;
   filas/pagina/errores/loading y selected B no cambian por A.
3. Caja: apertura con drop; recuperar UUID original. Manual in/out con drop y targets congelados,
   comprobar una fila por UUID y actor original. Demorar current mientras otro refresh termina:
   no cambia target de dialogo ya abierto. Cierre con drop: recuperar por ID original y ver
   conteo/diferencia guardados, sin nueva clave ni cierre duplicado.
   Caso apertura cerrada: perder respuesta de apertura; anotar su ID via SQL de abajo.
   En Network reenviar un POST close sintetico contra ESE ID (sin hook), y un POST open con NUEVA UUID.
   Recuperar la apertura pendiente en UI: devuelve sesion original Closed y current es la posterior;
   no crea tercera apertura para el intento original.
4. Transfer: mover 1 unidad al destino 002 con drop; recuperar misma UUID y ambas patas una vez.
   Settlement: seleccionar fecha ANTERIOR al dia actual y metodo no efectivo con neto 0 sintetico, demorar overview de otra
   fecha, confirmar/reintentar tras drop. Request conserva fecha/metodo/UUID originales.
   Repetir double click, navegacion y cambio de contexto autorizado solo en usuarios sintéticos:
   retry fuera del contexto original se bloquea, volver y recuperar. No editar intentos en storage.
5. Verificar permiso servidor actual: en este tenant sintetico revocar permiso o cambiar
   SessionVersion/RoleAuthorizationVersion mediante administracion existente; replay capturado
   rechaza 401/403, no devuelve resultado anterior. No aplicar cambios a usuarios/DB reales.

Lectura SQL sintetica para IDs/efectos (terminal humana):
```powershell
@'
SELECT "Id","RequestId","Status","OpeningAmount","CountedCashAmount","OpenedByUserId" FROM "CashSessions" ORDER BY "Id";
SELECT "RequestId",count(*) FROM "CashMovements" WHERE "RequestId" IS NOT NULL GROUP BY "RequestId";
SELECT "Id","RequestId","SourceType","StockBefore","StockAfter","UserId" FROM "InventoryMovements" ORDER BY "Id";
SELECT "Id","RequestId","Status","CreatedByUserId" FROM "PurchaseReceipts" ORDER BY "Id";
'@ | docker exec -i hfpos-be-fe-529-tests psql -U hfpos_test -d hfpos_test_529 -v ON_ERROR_STOP=1
```

No llamar aprobado un paso inconcluso ni HTTP 2xx sin identificar la operacion.
Anotar fallos/dudas, detener gate y enviar evidencia al coordinador.
Fernando confirma **VALIDADO OK** solo para HEAD/PR y recorrido realmente aceptados.
CI verde NO sustituye este gate. Merge PENDIENTE, autor sin autoridad para merge.

### Cierre del entorno humano propio

Detener UI/host con Ctrl+C. Conservar evidencia y recursos hasta aceptacion.
Solo despues, si no queda otro frente dependiente, el humano elimina SU container 529:
`docker rm -f hfpos-be-fe-529-tests`. No volumes externos fueron montados.
Keyring 529 propio queda en HF_POS_SMOKE_WORKDIR; no borrar keyrings existentes/528.
Rollback funcional requiere volver a version compatible y resolver intentos pendientes;
NO aplicar Down ni version vieja sobre datos/intentos reales autonomamente.
