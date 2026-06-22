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
- El backend convierte la fecha de negocio a un rango UTC semiabierto usando la zona horaria de la compania:
  - inicio inclusivo: `>= from`
  - fin exclusivo: `< to`

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

## SRI y documentos fiscales

- No cambiar fechas fiscales, XML, firma, autorizacion ni RIDE por esta politica salvo que el flujo lo requiera explicitamente.
- Las conversiones operativas no deben tocar certificados, adjuntos ni secretos.
