# DEV-531: servicios sin inventario

Issue #160, relacionado con #133. Riesgo R3. PENDIENTE / NO MERGE.

## Contrato y decisiones

- `Product.TracksInventory` es persistente; creacion/legacy default `true`.
  Update omitido conserva el valor. No se deriva de IVA ni de la clase fiscal.
- Crear con inventario, confirmar clasificacion fiscal vigente como servicio y
  editar para desactivar inventario. Se exige `ExpectedClassificationRevision`.
  La transicion true -> false requiere saldo cero en **cada** establecimiento
  (no una suma). El guard exclusivo de Company serializa operaciones en vuelo.
- Cambiar solo tracking no incrementa `FiscalRevision`. Editar identidad/IVA de
  un servicio sigue invalidando su clasificacion; checkout queda cerrado hasta
  reclasificar. No se permite confirmar Goods mientras tracking sea false.
- Preview incluye snapshot y revision/clase en su fingerprint v2. El hash
  comercial y replay de checkout confirmado no cambian.
- SaleItem y CreditNoteItem conservan `TracksInventorySnapshot`, legacy `true`.
  Void/NC usan la linea historica, no el producto actual. Restauraciones fisicas
  validan fuentes persistidas, contexto tenant y snapshot; no hay bypass cliente.
- Entradas, ajustes, apertura/importacion, compras y transferencias rechazan
  nontracking. Cancelar una compra fisica valida su recepcion persistida.
  Saldos historicos restaurados siguen visibles en inventario/dashboard;
  habilitar tracking otra vez permite su gestion normal. Kardex no se filtra por
  el modo actual.
- Una NC mixta devuelve solo lineas fisicas; replay exige el conjunto fisico
  completo. Una NC solo de servicios responde `CREDIT_NOTE_NO_PHYSICAL_ITEMS`
  sin movimientos ni auditoria de devolucion ficticia. Su efecto financiero
  permanece independiente. XML, IVA, caja y reglas SRI no se modifican.

## Despliegue sobre RC4 (solo runbook humano, no ejecutado)

1. Obtener autorizacion R4 separada, respaldo verificable y ventana sin escritores.
   Verificar artefactos backend/frontend y CI del mismo HEAD aprobado.
2. Detener todas las instancias/escritores RC4. Inspeccionar el SQL aprobado de
   `20261021235813_AddNonInventoryServices`: tres columnas bool NOT NULL default
   true, sin backfill por productos, IDs, SKU ni clasificaciones.
3. Operador autorizado aplica la migracion con el procedimiento habitual y
   publica backend/frontend juntos. No ejecutar binarios RC4 en paralelo.
4. Verificar schema/health y que el catalogo existente conserva tracking true.
   Activar servicios solo mediante el flujo explicito de clasificacion + edicion.

**No volver a binarios RC4 despues de activar/vender servicios.** Ignorarian los
snapshots y podrian crear inventario ficticio. Ante incidentes, detener escritores
y corregir hacia adelante. Down toma ACCESS EXCLUSIVE sobre Products, SaleItems,
CreditNoteItems antes de comprobar datos y rechaza cualquier false. No borrar
columnas, reclasificar ni reescribir historicos para eludir esta barrera.
Un restore completo de respaldo requiere autorizacion humana y reconciliacion
de todas las operaciones posteriores; no es rollback automatico del hotfix.

## Cobertura tecnica

`NonInventoryServicesTests` cubre PostgreSQL: servicio cero, mercaderia cero,
mixta, snapshots void/NC, replay/conjunto incompleto, clasificacion y edicion,
tenant, admisiones de stock, compra/transferencia, import preview/confirm,
carrera Company SHARE/EXCLUSIVE con dos conexiones y `pg_blocking_pids`,
insert/reload false, defaults legacy y Down bloqueado. Frontend cubre mapeo,
agregado/cantidades, respuesta de refresh ausente y etiquetas UI. Resultados
reales y CI completo se registran en el PR; no requieren repetir toda la matriz
como pruebas visuales.

## VALIDACION HUMANA PRE-MERGE REQUERIDA

Solo entorno nuevo disposable, datos sinteticos y transporte SRI fake; sin
certificados reales, servicios SRI reales, DB persistente ni produccion.

1. Crear un producto sintetico con inventario. Confirmar Service en Fiscal y
   desmarcar Controla inventario en Editar. Refrescar y verificar persistencia.
2. Con stock cero: POS muestra Sin inventario y permite agregar cantidades;
   Goods muestra Sin stock y no permite agregar. Vender servicio y verificar
   total/IVA/caja, sin fila de stock ni movimiento nuevo para el servicio.
3. Anular esa venta: efecto de caja esperado y ningun movimiento/stock fisico.
   Como contraste, verificar rechazo de mercaderia con stock cero; una mixta
   representativa es opcional si aporta valor a la aceptacion.

El coordinador y revisor independiente justifican la suficiencia proporcional
de este smoke frente a la evidencia tecnica; no se exige repetir manualmente
NC, tenant, todas las admisiones ni todas las permutaciones de concurrencia.

Aceptacion de Fernando: **pendiente**. Review independiente y CI exacto: registrar
en el PR; este documento no los presume. Sin merge ni deploy por el implementador.
