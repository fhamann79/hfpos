# DEV-531 / issue #153 - Nucleo fiscal multirregimen

Base de implementacion: `8afe5714f07563861d7516b7da9985610ee3cc7a`.
Riesgo R3. Desarrollo y pruebas sinteticas solamente. **NO MERGE / NO DEPLOY / NO R4.**

## Contrato fiscal y limites

- Cada Company declara un perfil estructurado: General, RIMPE Emprendedor o RIMPE Negocio Popular; persona natural/juridica; contabilidad; calificacion especial y designacion de agente independientes. NP requiere persona natural; sociedades requieren contabilidad. No se infiere regimen ni elegibilidad a partir de ventas.
- Un usuario con `FISCAL_SETTINGS_WRITE` confirma hechos, vigencia, motivo y referencia documental. La confirmacion es una declaracion del contribuyente, no una consulta/verificacion del SRI. RIMPE exige declaracion expresa de elegibilidad y ausencia de actividades excluyentes. La realidad del RUC/actividad debe ser confirmada por el contribuyente y su asesor.
- Nuevas versiones append-only, revision esperada, autor del contexto autenticado, fecha Ecuador e intervalo [desde, hasta). No se aceptan cambios retroactivos. Una declaracion no utilizada puede ser sustituida por otra version del mismo dia sin borrar historia; despues de usarla, un cambio en esa fecha se bloquea. Una version sucesora limita la vigencia de la anterior sin editarla.
- La clasificacion tributaria del producto tambien es una declaracion versionada. Cambiar nombre, identificadores o categoria IVA invalida la confirmacion anterior, incluso si luego se vuelve al valor anterior. Crear/importar productos NO crea una clasificacion verificada.
- Un unico resolvedor determina tarifa efectiva, categoria y codigos SRI. Enum de catalogo, porcentaje y codigo SRI son conceptos distintos. El POS implementa operaciones locales inmediatas; importaciones, servicios periodicos/diferidos, anticipos y hechos generadores especiales no estan cubiertos.

| Caso implementado | Condiciones | Resultado |
| --- | --- | --- |
| Operacion ordinaria | General o Emprendedor, clasificacion compatible y confirmada | IVA 15%, impuesto 2, porcentaje 4 |
| Operacion comprendida NP | Perfil natural NP vigente, elegibilidad declarada, actividad cubierta confirmada | IVA 0%, impuesto 2, porcentaje 0; conserva IVA nominal del catalogo |
| Alimento agricola natural | LRTI 55.1; bien en estado natural; sin mezcla/preparacion/transformacion incompatible; procesos compatibles declarados | IVA 0%, porcentaje 0 |
| Material de construccion listado | Transferencia local; coincidencia exacta de producto/caracteristicas declarada; codigo F de lista cerrada oficial | IVA 5%, porcentaje 5; codigo F en auxiliar XML |
| No objeto, exento, otros ceros | Sin caso operacional completo implementado | BLOQUEO explicito; nunca se convierten en 0% o 15% por defecto |
| Actividad excluida RIMPE / indeterminada | Perfil incompatible o hechos insuficientes | BLOQUEO; no cambiar automaticamente regimen ni gravar solo esa linea |

No se afirma cobertura integral de LRTI 54/55/56, SOFA, turismo, retenciones, ICE, exportaciones u otros beneficios. La designacion de agente es una condicion del emisor; este PR no implementa comprobantes ni calculos de retencion. Un NP no se convierte en Emprendedor/General por superar un contador interno.

La regla general del 15% se versiona en codigo con referencia a la continuidad normativa vigente; no es una garantia sobre futuros cambios legales. El 5% no se aplica a toda ferreteria ni a contratos de construccion. Las opciones de procesamiento de alimentos son un subconjunto cerrado; no autorizan mezclas o transformaciones por texto libre.

## Flujo y contratos

- GET/POST `/api/FiscalSettings/profiles`: historial / nueva declaracion, permisos fiscales existentes.
- GET/POST `/api/FiscalSettings/profiles/products/{id}`: clasificacion de producto dentro de Company, permisos fiscales existentes. GET devuelve revision del producto aun sin clasificacion. POST exige tanto revision esperada de clasificacion como de producto; un formulario anterior a cambios del catalogo no puede confirmar evidencia vieja sobre una identidad nueva.
- El PUT fiscal anterior permite conservar sus campos legados, pero rechaza cambios de regimen/contabilidad/especial con `FISCAL_PROFILE_VERSION_REQUIRED`. No es una via alternativa de activacion.
- POST `/api/Sales/preview`, permiso `POS_SALES_CREATE`: calculo sin reservas, numeracion, caja ni stock. Responde huella, fecha/perfil, totales y lineas efectivas; no expone costos. El carrito muestra tarifa, descuento distribuido, base y total efectivos del backend; sin preview vigente muestra pendiente, nunca el IVA nominal como resultado fiscal.
- Checkout recalcula con el mismo motor bajo lock Company y compara `fiscalFingerprint` antes de bloquear caja/numerar/mover stock. Cambios materiales exigen una nueva revision del operador. No se confia en totales recibidos ni en una huella como credencial. Precios solicitados, descuentos y redondeo monetario existentes se conservan; los residuos se acotan a bases/descuentos no negativos.
- Un unico instante fiscal se reutiliza para perfil, numero, snapshot, clave y XML. Cambio de dia/perfil invalida un preview anterior. El preview no es una reserva ni una garantia de stock.
- Se preserva el hash comercial v1 de idempotencia. Con contexto/actor validos, un resultado ya confirmado se recupera antes de exigir perfil, preview, producto activo o caja abierta actuales. La UI conserva UUID y payload ante resultado ambiguo; no reintenta otro cobro silenciosamente.

## Historicidad, NC y worker

- Cada nueva venta congela el perfil/identidad del emisor y la decision fiscal por linea. La primera factura/XML permanece en la transaccion original de venta; no se agrega una ventana posterior.
- NC conserva importes/tarifas y saldos/prorrateo del original, y congela su propio emisor al crearla. Preparacion diferida/repetida no lee el regimen vivo. Cambios de regimen entre original y NC se bloquean mientras no exista una regla normativa implementada para sus condiciones de emision. Original sin contexto fiscal verificable tambien se bloquea para nuevas NC; no se inventa historia.
- RIDE siempre lee el XML autorizado, incluyendo leyenda PR #152, especial y agente. No consulta Customer/Company vivos para reemplazar datos fiscales historicos. No se regeneran XML firmados/autorizados.
- Documentos previos sin snapshot quedan legados, sin backfill. Se conservan lectura y consulta de autorizacion con evidencia existente; no pueden iniciar una nueva firma/recepcion sin revision fiscal verificable. El worker registra atencion manual, no borra jobs/documentos ni los reclasifica. Delegacion, revocacion, tenant activo, lease/fence, certificado, Environment y kill-switches siguen obligatorios.

## Migracion y cambio de version operativo

`AddTenantFiscalProfiles` es aditiva: tablas nuevas, referencias/snapshots nullable, revision fiscal de producto inicial 0, indices/FK compuestas por tenant y restricciones no destructivas. No siembra perfiles, no convierte textos libres ni clasifica catalogos.

**Requiere migracion en una futura ventana humana R4 autorizada; no se ejecuto contra una DB persistente.** Antes de esa ventana: backup/rollback aprobado, detener nuevos writers/worker antiguos de forma humana controlada y revisar borradores pendientes. Compatibilidad SQL no autoriza convivir con un binario anterior que emita sin los nuevos gates. No cambiar numeros existentes ni usar secuenciales reales en pruebas.

Rollback conserva esquema, snapshots y auditoria; no usar `Down` para borrar historia. Un rollback de binario debe mantener bloqueada la emision afectada si no puede garantizar las mismas validaciones. No se incluye aqui ningun comando de operacion real ni autorizacion para ejecutarlo.

## Evidencia normativa

- [LRTI consolidada SRI](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar/12c884d5-c8d9-4171-becc-1513b990b87a/LEY_DE_REGIMEN_TRIBUTARIO_INTERNO_LRTI.pdf), ultima reforma 1-abr-2026: arts. 54/55/56/65 y 97.3/97.4/97.9. SHA256 del documento contrastado: `4219808f64053197ad33867688a9f00a43becefb65d89d763b7741083f2efca5`.
- [Circular 25-06](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar?id=236482f4-6125-42fd-b073-62c99d08233d&nombre=NAC-DGECCGC25-00000006.pdf): continuidad del 15% hasta nuevo decreto.
- [Resolucion 24-13](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar?id=6b8588f2-a4bf-44bb-ac40-085391ba2aed&nombre=NAC-DGERCGC24-00000013.pdf): transferencias locales de materiales expresamente listados, no toda una actividad comercial.
- [Resolucion 24-27](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar?id=7c3fb8dc-31a6-4fe3-a6c0-abe1def2c78a&nombre=NAC-DGERCGC24-00000027.pdf): declaraciones/condiciones RIMPE y leyendas.
- [Circular 24-02, II.5](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar?id=0620a32d-24e2-44bc-9070-3a664278ceae&nombre=NAC-DGECCGC24-00000002.pdf): NC utiliza la tarifa del documento modificado; no resuelve por si sola toda transicion de regimen/leyenda.
- Ficha tecnica 2.34, anexo 21 (agente), 22 (leyendas), 23 tabla 31 (codigos F); referencias y discrepancia de XSD antiguos en [evidencia PR #152](DEV-531-rimpe-xml-ride.md). Los validadores locales no equivalen a certificacion tributaria ni validacion integral contra un XSD oficial actualizado; no se altera un XSD oficial para fabricar un PASS.

## Validacion humana PRE-MERGE

Solo entorno local/efimero con datos sinteticos y transporte SRI falso; NO Produccion ni endpoints SRI reales. No repetir las pruebas humanas RC3 ajenas a este cambio.

1. Dos empresas: confirmar perfiles distintos, revisar naturaleza/condiciones/historial y comprobar que el otro tenant no ve ni cambia sus declaraciones.
2. NP + articulo nominal 15% confirmado: preview/checkout 0%; General/Emprendedor: 15%. Confirmar que catalogo y documentos anteriores no cambian.
3. Material local del listado con codigo F y datos confirmados: 5%; material sin evidencia/codigo correcto: bloqueo. Verificar el auxiliar XML.
4. Probar configuracion incompleta, persona juridica sin contabilidad, RIMPE incompatible/actividad excluida, producto sin confirmar y categorias 6/7: error accionable, sin numero/stock/caja afectados.
5. Carrito de tres lineas de 0.04, solo primera al 15%, descuento global 0.01 y orden inverso: total backend 0.12; el mismo total en preview, venta y XML. Cambiar perfil/producto/dia entre preview y confirmar: nueva confirmacion, no cobro silencioso.
6. Simular respuesta de venta perdida y recuperar el mismo UUID/payload; comprobar un solo efecto. Cambio de contexto debe bloquear recuperacion cruzada.
7. NC parcial de factura sintetica autorizada por transporte falso: IVA original, snapshot propio y preparacion repetible; transicion de regimen no soportada se bloquea antes de numerar. RIDE visual/PDF conservan leyendas, condiciones, codigos largos y wrap.

CI/review/contadores finales se registraran en el PR sobre su HEAD exacto. Esta guia no declara su ejecucion humana ni autoriza merge/despliegue.
