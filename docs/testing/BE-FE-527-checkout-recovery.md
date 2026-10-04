# BE-FE-527: checkout recuperable y ticket interno

## Alcance y contrato

Base de implementacion: `52b39a1a281b258357a86d802315c7a10cac142e`.
Riesgo R3. Issue #129, roadmap #127. Solo datos sinteticos, sin SRI real,
certificados, secretos ni bases persistentes/compartidas.

- `POST /api/Sales` requiere `requestId` UUID no vacio, creado por el cliente.
  No se genera una clave en el servidor. La clave es unica por CompanyId.
- El hash v1 incluye actor/contexto por comparacion persistida y material de la
  intencion: cliente, Cash/Ticket como defaults efectivos, descuentos cero,
  notas trim, recibido y lineas en su orden original, sin agrupar duplicados.
  Los decimales usan representacion invariante normalizada.
- Misma intencion devuelve el DTO de la misma venta persistida; el estado
  comercial/fiscal puede haber avanzado. Otro material, actor o contexto produce
  `409 REQUEST_CONFLICT`, sin publicar datos de la venta ajena.
- Company FOR UPDATE desde el inicio serializa las ventas del tenant y evita
  upgrades SHARE->UPDATE. Tradeoff deliberado: menor paralelismo entre cajas del
  mismo tenant. Orden: Company -> CashSession -> DocumentSequence -> stock.
  Se revalidan versiones de sesion/rol y contexto al adquirir el lock, despues
  de caja y antes del commit. Administracion/lifecycle comparte ese guard.
- El replay se resuelve antes de exigir caja abierta, cliente/productos activos,
  stock o configuracion fiscal vigente. No recrea draft, numero ni movimientos.
- Efectivo requiere `cashReceived` no negativo y dentro de numeric(18,2).
  Se normaliza a dos decimales con AwayFromZero, como el dinero existente, y debe
  cubrir el total autoritativo. `cashChange = cashReceived - total` se persiste.
  Caja sigue sumando SOLO Sale.Total. Otros metodos rechazan cashReceived y
  conservan sus totales. Total cero permite recibido/vuelto cero; sigue siendo
  una venta con numero y movimiento de stock si corresponde.
- Cantidad admite cuatro decimales; precio dos. Hay limites de precision,
  cantidad de lineas (500), y rechazo de overflow antes de persistir efectos.
- Migracion aditiva `20261004084857_AddRecoverableSaleCheckout`: RequestId/hash,
  CashReceived/CashChange y ProductNameSnapshot/ProductSkuSnapshot nullable;
  indice unico filtrado y checks. Sin backfill inventado. MinimumStock de 526
  mantiene su sentinel vigente en modelo/snapshots.
- GET/POST/replay exponen los campos nuevos. El ticket usa solo snapshots de
  comprador y producto, cantidades/importes/pago persistidos. Para legacy sin
  snapshots indica historia no disponible; no sustituye nombres desde catalogo
  o cliente actuales, ni fabrica recibido/vuelto cero. No muestra costo/margen.
- El POS conserva en sessionStorage del tab la intencion congelada, separada
  por tenant/punto/actor, antes del POST. Red/5xx/respuesta malformada/conflicto
  retienen esa intencion. Reintento no pasa por gates mutables de caja/stock.
  Cambio de contexto bloquea su reenvio. No hay soporte offline.
- Solo errores de negocio definitivos conocidos liberan la intencion para
  corregir el carrito y emitir una nueva clave. Stock rechazado conserva todas
  las cantidades, precios y descuentos; refresca solo IDs del carrito en lotes
  maximos de 100, incluyendo inactivos. Ausencia en busqueda paginada no borra.
- Resultado POST/replay permite ticket con permiso de crear, sin agregar permiso
  de reportes. Reimpresion usa el detalle existente bajo ReportsSalesRead.
  Impresion crea un documento iframe exclusivo para el article del ticket,
  pagina de 80mm con altura calculada, sin navegacion/dialogos ni estilos RIDE.
  Error/cancelacion de impresion nunca llama POST. Siguiente cliente es explicito.

## Evidencia automatica

`SaleCheckoutRecoveryTests` ejecuta PostgreSQL real del servicio efimero CI:
concurrentes con bloqueo demostrado por pg_blocking_pids, Ticket y Invoice con
generacion fiscal puramente local/sintetica, respuesta descartada despues del
commit y replay en otro scope, caja cerrada/catalogo/fiscal config cambiados,
hash/defaults/duplicados, tenant/actor/punto, revocacion de sesion y rol durante
espera, efectivo/otros metodos/cero/precision/overflow, rollback de segundo item
y retry del mismo DbContext, y migracion legacy down/up en DB propia descartable.
No se simula el motor SQL. El descarte de respuesta es una prueba de recuperacion
post-commit a nivel servicio, no una prueba de transporte HTTP ni impresion fisica.

Frontend focused cubre intencion/doble submit/retry/destruccion/contexto, errores
ambiguos y definitivos, carrito recuperable, recibido, siguiente cliente y DOM de
ticket/impresion/cleanup. Los jobs requeridos CI ejecutan las suites completas y
verificacion EF en HEAD publicado. Docker local sin engine: PG local NO ejecutado;
PG CI es obligatorio, nunca se declara skip exitoso. No se levanta API/demo local.

Primera ejecucion CI sobre `8d6557d667737829e70dc8e141cf387e71cf0cf0`:
[run 37202891859](https://github.com/fhamann79/hfpos/actions/runs/37202891859),
backend 356 passed / 2 failed / 358 total. Ambas variantes Ticket/Invoice fallaron
en la barrera del test: exigia bloqueo directo por el propietario, ignorando el
waiter anterior en la cola de locks. Correccion acotada al test: consulta recursiva
de pg_blocking_pids desde observer separado, prueba de cadena hasta el propietario
y liberacion del propietario en finally antes de drenar scopes. Sin ampliar
timeouts, skips ni cambios de negocio. EF del job CI no se ejecuto por ese fallo;
la evidencia local de modelo no sustituye la validacion CI final.

## Un smoke humano integrado pre-merge

**VALIDACION HUMANA PRE-MERGE REQUERIDA.** Aceptacion de Fernando pendiente.
Ejecutar sobre el HEAD congelado del PR en entorno de prueba descartable con
catalogo/tenant/usuarios sinteticos. No utilizar la DB Development persistente.
No enviar/fimar documentos SRI, usar certificados ni infraestructura real.

1. Abrir caja con 5.00; escanear/buscar dos productos sinteticos, ajustar cantidades
   y descuento. Cobrar Ticket con total 10.00 y recibido 20.00. Probar recibido
   insuficiente antes de confirmar. Pulsar doble click/Enter/F12: una venta, un
   secuencial, un efecto de inventario y caja esperado 15.00 (no 25.00).
2. Ver numero/estado/pago y vuelto 10.00 del resultado. Abrir vista previa de
   impresion: solo ticket interno, ancho 80mm, sin nav/dialogos/costos. Cancelar,
   reimprimir y verificar que no aparece otra venta. Desde ventas recientes,
   editar catalogo/cliente sinteticos y reimprimir: conserva snapshots vendidos.
   Verificar tambien una fila legacy sin snapshots: no inventa nombres historicos.
3. Siguiente cliente: carrito/cliente/descuento/notas/tipo/pago/recibido/busqueda
   limpios, foco al escaner. Realizar otra venta con tarjeta o transferencia y
   comprobar que no pide recibido ni altera el efectivo de caja.
4. En el entorno de prueba, descartar UNA respuesta de POST /api/Sales SOLO despues
   de observar su 201/commit en un proxy/harness de fallos de prueba. El POS debe
   mostrar cobro pendiente y bloquear otra intencion. Cerrar caja y cambiar stock
   o desactivar producto sintetico; recuperar desde el mismo usuario/punto devuelve
   la venta original. Refrescar/navegar y volver mantiene la recuperacion pendiente.
   Confirmar una venta/un stock/un numero/un draft si era Invoice. No reenviar a SRI.
5. Forzar stock insuficiente desde otra sesion sintetica antes del commit. Comprobar
   rechazo sin efectos parciales; el carrito completo conserva lineas/importes,
   muestra disponibilidad actual y permite corregir la linea afectada. La siguiente
   intencion usa otra clave; los productos ajenos a la correccion no desaparecen.
6. Cerrar caja y conciliar Total por metodo. Confirmar aislamiento con otro tenant
   y que un usuario sin permiso crear no cobra, sin permiso reportes no reimprime
   desde detalle. Usuario solo POS create puede imprimir su respuesta inmediata.

Registrar HEAD probado y resultado. Solo Fernando declara `VALIDADO OK`.
Impresion fisica/ergonomia y perdida HTTP real siguen pendientes de este smoke;
los tests DOM/servicio no los sustituyen. Review independiente pendiente.
El implementer no hace merge. No iniciar 528+ ni limpiar este worktree.

## Rollback

Preferir revert de codigo compatible o correccion hacia delante antes de piloto.
Down de esta migracion elimina metadatos de recovery/recibo; no ejecutarlo sobre
ventas reales. Operaciones reales/deployment/migracion son R4 exclusivamente humanas.
