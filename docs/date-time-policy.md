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
- Las fechas de negocio tipo `ReceiptDate` se muestran como fecha de negocio, no como instante local del navegador.
- El backend convierte la fecha de negocio a un rango UTC semiabierto usando la zona horaria de la compania:
  - inicio inclusivo: `>= from`
  - fin exclusivo: `< to`

## Instantes reales en frontend

- `/api/Auth/me` expone `companyTimeZoneId` para que las pantallas no dependan de la zona horaria del navegador.
- El frontend debe usar `companyTimeZoneId` para mostrar instantes reales como `CreatedAt`, `CanceledAt`, `SentAt` o `GeneratedAt` cuando correspondan al negocio.
- Las exportaciones CSV deben usar fecha y hora de empresa, no UTC crudo.

## Servicios backend

- Usar `IBusinessClockService` para:
  - obtener la fecha de negocio actual de la compania;
  - convertir una fecha de negocio a inicio UTC;
  - construir rangos UTC semiabiertos;
  - agrupar instantes UTC por fecha operativa.
- No duplicar conversiones manuales en controllers o servicios.

## Compras e inventario

- La fecha de recepcion de compra es una fecha de negocio.
- Para guardar una recepcion, el backend toma el `yyyy-MM-dd` recibido y lo convierte al inicio UTC de ese dia en la zona horaria de la compania.
- Para mostrar esa fecha en Angular, usar el pipe `date` con zona `UTC` cuando el valor representa el inicio UTC de una fecha de negocio. Asi se evita mostrar el dia anterior.

## Datos historicos

- Datos historicos afectados por bugs de zona horaria deben corregirse con scripts controlados por caso, no automaticamente en migraciones generales.

## SRI y documentos fiscales

- No cambiar fechas fiscales, XML, firma, autorizacion ni RIDE por esta politica salvo que el flujo lo requiera explicitamente.
- Las conversiones operativas no deben tocar certificados, adjuntos ni secretos.
