# Politica de fechas operativas

HFPOS separa las fechas de negocio de los instantes tecnicos UTC.

## Zona horaria de compania

- Cada compania define `TimeZoneId`.
- El valor por defecto es `America/Guayaquil`.
- Los valores se guardan como identificadores IANA cuando vienen del frontend.
- El backend acepta y resuelve la zona horaria antes de guardar la compania.

## Fechas de negocio

- Las pantallas operativas deben enviar fechas como `yyyy-MM-dd`.
- El frontend no debe construir fechas de negocio como `yyyy-MM-ddT00:00:00Z`, porque eso representa medianoche UTC y puede caer en el dia anterior para Ecuador.
- Las fechas de negocio se guardan como snapshots `date` dedicados, por ejemplo `BusinessDate`, `ReceiptBusinessDate` o `CanceledBusinessDate`.
- Los reportes, dashboard y kardex filtran por esos snapshots de fecha operativa, no recalculando historicos desde `CreatedAt` con la zona horaria actual.
- Los cambios futuros de `Company.TimeZoneId` afectan registros nuevos, no reinterpretan registros historicos.

## Snapshots de zona horaria

- Cada registro operativo que agrupa por dia debe guardar el identificador usado al crearse en un campo `TimeZoneIdSnapshot`.
- Ventas guardan `Sale.BusinessDate` y `Sale.TimeZoneIdSnapshot`.
- Compras guardan `PurchaseReceipt.ReceiptBusinessDate` y `PurchaseReceipt.ReceiptTimeZoneIdSnapshot`.
- Cancelaciones de compra guardan `PurchaseReceipt.CanceledBusinessDate` y `PurchaseReceipt.CanceledTimeZoneIdSnapshot`.
- Movimientos de inventario guardan `InventoryMovement.BusinessDate` y `InventoryMovement.TimeZoneIdSnapshot`.
- `CreatedAt`, `CanceledAt` y otros instantes reales siguen siendo UTC para auditoria.

## Instantes reales en frontend

- `/api/Auth/me` expone `companyTimeZoneId` para que las pantallas no dependan de la zona horaria del navegador.
- El frontend debe usar `companyTimeZoneId` para mostrar instantes reales como `CreatedAt`, `CanceledAt`, `SentAt` o `GeneratedAt` cuando correspondan al negocio.
- Las exportaciones CSV deben usar fecha y hora de empresa, no UTC crudo.

## Servicios backend

- Usar `IBusinessClockService` para:
  - obtener la fecha de negocio actual de la compania;
  - convertir una fecha de negocio a inicio UTC;
  - construir rangos UTC semiabiertos;
  - calcular el snapshot de fecha operativa al crear un registro nuevo.
- No duplicar conversiones manuales en controllers o servicios.

## Compras e inventario

- La fecha de recepcion de compra es una fecha de negocio.
- Para guardar una recepcion, el backend toma el `yyyy-MM-dd` recibido y lo convierte al inicio UTC de ese dia en la zona horaria de la compania.
- El backend tambien guarda `ReceiptBusinessDate` con el `yyyy-MM-dd` recibido para que reportes y dashboard no dependan de la zona horaria actual.
- Para mostrar esa fecha en Angular, usar `ReceiptBusinessDate` como `DateOnly` y formatear el texto sin construir un objeto `Date`.

## Datos historicos

- Datos historicos afectados por bugs de zona horaria deben corregirse con scripts controlados por caso, no automaticamente en migraciones generales.
- Las migraciones de snapshots pueden hacer backfill conservador para preservar la fecha historica esperada sin recalcularla con la zona horaria actual de la compania.

## SRI y documentos fiscales

- No cambiar fechas fiscales, XML, firma, autorizacion ni RIDE por esta politica salvo que el flujo lo requiera explicitamente.
- Las conversiones operativas no deben tocar certificados, adjuntos ni secretos.
