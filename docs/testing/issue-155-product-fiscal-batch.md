# #155: clasificacion fiscal por lote (R3)

- Base: `fa83db3ff294e74ee3ea7731b7a2a6d6a86ef410`.
- Catalogo > Productos > Clasificacion fiscal. El flujo exige lectura y escritura de productos y configuracion fiscal; los mismos permisos se vuelven a validar en el servidor. La empresa y el actor se derivan de la sesion.
- Se seleccionan IDs explicitamente en paginas de 30, con busqueda por nombre, codigo interno o barras, categoria, estado comercial y estado de la ultima declaracion. El limite de 250 cubre el universo previsto de aproximadamente 228, sin seleccion global ni lecturas masivas. Agrupar solo productos para los que se pueda confirmar el mismo fundamento y evidencia.
- La vigencia de una declaracion en la consulta no equivale a elegibilidad fiscal. La previsualizacion vincula perfil y fecha fiscal de Ecuador, regla vigente, seleccion y estados revisados. Un producto sin declaracion no se presume mal clasificado por tener IVA nominal 0%. El IVA nominal propuesto deriva del fundamento y la tarifa efectiva del perfil vigente, usando el resolver existente.
- Rechazo de una fila impide confirmar todo el lote. Inactividad, categoria inactiva y falta de existencias son advertencias operativas, no cambios incluidos ni una garantia de emision. Se puede excluir y generar otra previsualizacion.
- Confirmacion explicita dentro de 15 minutos: primero lock exclusivo de empresa, luego productos por ID en orden ascendente (los mismos locks que ventas y compras). Se vuelve a resolver y comparar el estado bajo lock; una divergencia pide una nueva previsualizacion.
- Se actualizan solo IVA nominal y revision fiscal cuando corresponde, y se agrega una declaracion por cambio efectivo. No se escribe actividad comercial, stock, modo de inventario, costo, precio, ventas ni documentos. El checkout invalida su propia previsualizacion si las decisiones cambian; XML e impuestos anteriores conservan sus snapshots.
- Sin migracion: `InitialDataBatches` aporta clave unica por empresa/solicitud y persistencia atomica de actor, motivo, antes/despues e informe. El tipo reservado `product-fiscal-batch` no aparece en el historial de carga inicial. Un lote sin cambios no agrega declaraciones; un reintento autentico recupera el mismo comprobante incluso si vencio la previsualizacion, sin agregar otro registro.

## Contratos

Se agregan `GET /api/products/fiscal-batches/candidates`, `POST .../preview`, `POST .../confirm` y `GET .../{operationId}`. El comprobante se recupera desde el mismo usuario y contexto operativo, con los permisos vigentes.

Compatibilidad del PUT legado `/api/products/{id}`: si cambia el IVA nominal, debe enviar `expectedProductFiscalRevision` y `expectedClassificationRevision` vigentes (obtenibles de `/api/products/editor/{id}`); de otro modo devuelve `409 PRODUCT_FISCAL_REVISION_CONFLICT`. Si omite el IVA o envia el mismo valor, la nueva precondicion no aplica. La UI normal usa el editor unificado.

## Validacion del lab sintetico

1. Preparar dos empresas, perfil vigente, usuario con permisos de catalogo y fiscal, otro solo comercial, productos 0% nominal sin declarar, 0% natural declarado, producto inactivo, servicio sin inventario y una factura historica con XML de prueba. No usar certificados reales ni llamar al SRI.
2. Buscar y seleccionar explicitamente en dos paginas. Compruebe que los rechazos bloquean todo el lote y que excluirlos exige nueva previsualizacion y atestacion. Compare IVA nominal, efectivo y advertencias. Confirme y recupere el comprobante desde su referencia.
3. Reintentar la misma confirmacion y verificar un unico comprobante. Probar lote sin cambios y una previsualizacion invalidada por una edicion intervenida. Verificar que el usuario comercial no puede realizar la operacion y que no aparecen productos ajenos.
4. Verificar que inactivos siguen inactivos, inventario/precios/costos no cambian, y los impuestos y XML de la factura historica coinciden con el original.
