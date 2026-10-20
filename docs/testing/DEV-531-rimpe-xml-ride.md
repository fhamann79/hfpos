# DEV-531: XML RIMPE y RIDE historico

Alcance R3: leyendas de factura/NC, precedencia del XML autorizado en RIDE y
legibilidad/paginacion PDF. Sin cambios de frontend, motor IVA, transacciones,
DB, migraciones, numeracion, configuracion, firma, certificados o despliegue.

## Fuentes primarias y discrepancia

Referencias y contraste confirmados por el coordinador:

- [Portal SRI](https://www.sri.gob.ec/facturacion-electronica).
- [Ficha offline 2.34](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar/f8d9bb36-5632-4f96-b463-b9265b55338c/FICHA%20TE%CC%81CNICA%20COMPROBANTES%20ELECTRO%CC%81NICOS%20ESQUEMA%20OFFLINE%20Versio%CC%81n%202.34.pdf),
  anexo 22, pp. 129-130: `infoTributaria.contribuyenteRimpe`, despues del opcional
  `agenteRetencion`. Leyenda generica/emprendedor: `CONTRIBUYENTE RÉGIMEN RIMPE`
  (27 caracteres); negocio popular: `CONTRIBUYENTE NEGOCIO POPULAR - RÉGIMEN RIMPE`
  (45 caracteres).
- Los ZIP oficiales publicados en el portal, etiquetados febrero 2022, tienen
  un patron antiguo que solo admite la leyenda de 27 caracteres. El contraste
  independiente con lxml XMLSchema del **tipo aislado**, sin alterar el patron,
  acepta 27 y rechaza 45 en factura 1.0/1.1/2.0/2.1 y NC 1.0/1.1. **No es
  validacion del XML completo ni se afirma conformidad NP con esos XSD 2022**.
  Los bundles oficiales permanecen externos, sin modificaciones.
- Resolucion NAC-DGERCGC24-00000027, art. 6 p. 5: etiquetas explicitas
  `Contribuyente RIMPE Negocio Popular` y `Contribuyente RIMPE - Emprendedor`.
  Se reconocen etiquetas completas y variantes conservadoras, no coincidencias
  de subcadenas, negaciones o mezclas de regimenes.

SHA256 de las fuentes externas:

| Fuente | SHA256 |
| --- | --- |
| Ficha 2.34 PDF | `7333aebfbdf2cb3ba83f9fc67a7a7f0346ca59506480a260cc42f96dbdfc13c9` |
| ZIP factura | `ba1ff0c4e329fe759c3f88dc75f2975780b315b6eb3d0069071b77c1f26fec03` |
| ZIP NC | `9b7e9c1a240ae39a858aa8fbaf91c41c761417e7bd763ccb20ffc0b81ad1bd43` |

## IVA: analisis y limites

Ficha 2.34, p. 30 tabla 17: IVA 0% codigo `0`, 15% `4`, 5% `5`, no objeto `6`,
exento `7`. [LRTI oficial, reforma 1 de abril de 2026](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar/12c884d5-c8d9-4171-becc-1513b990b87a/LEY_DE_REGIMEN_TRIBUTARIO_INTERNO_LRTI.pdf),
art. 97.9 p. 123: transferencias/servicios cubiertos de negocios populares tienen
IVA 0%; emprendedores aplican tarifa legal. Considerar exclusiones del art. 97.4.

Esto **no certifica el IVA NP ni habilita produccion NP con productos al 15%**.
No se infiere aplicabilidad tributaria desde texto libre `Company.TaxpayerRegime`;
no se introduce override ni recalculo. No se afirma que HFSISTEMAS sea NP ni se
accedio a su configuracion, facturas, datos o entornos reales.

## Evidencia tecnica reproducible

`SriRimpeRideTests` cubre generadores factura/NC, validadores locales sin cambios,
ubicacion/longitudes/variantes/negativos, precedencia canonica y fallbacks historicos,
microempresas de factura y ausencia de reinterpretacion desde Company actual.
Pasar un validador local **no certifica cumplimiento normativo ni XSD oficial**.

Los cuatro PDF sinteticos usan IVA 0%, EAN13, leyenda NP, multilinea, tokens largos
y continuacion entre paginas. Se exportan definiendo `HFPOS_RIMPE_PDF_OUTPUT` al
ejecutar el filtro `FullyQualifiedName~SriRimpeRideTests` del proyecto backend.
El test separado de preservacion usa deliberadamente un snapshot NP/15% inconsistente
para demostrar que esta correccion no altera impuestos; no lo normaliza ni certifica.
Los XML autorizados historicos siguen siendo la fuente del RIDE.
