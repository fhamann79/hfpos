# SEO de la landing de HF One

## Estado de la vista previa

`/conoce-hf-one/` es HTML estático. Mantiene `noindex` hasta la autorización expresa de publicación e indexación. No es un control de acceso: no debe contener material privado. No se publican un canonical, `og:url`, robots.txt ni sitemap.xml, porque el host y la ruta pública finales no se han confirmado. Las rutas de las imágenes sociales son locales a la vista previa.

El contenido responde a la intención comercial de encontrar un punto de venta para Ecuador, gestionar inventario y caja, o emitir y consultar facturas electrónicas. No se declaran volúmenes de búsqueda ni se añaden listas de palabras clave. Los datos estructurados describen únicamente HF One y su desarrollador; no añaden ofertas, reseñas ni valoraciones ni prometen resultados enriquecidos.

## Activación durante el lanzamiento autorizado

1. Confirmar con Fernando el host HTTPS y la ruta pública definitiva. No inferirlos del piloto. Comprobar que la URL final devuelve 200, que carga sin depender de JavaScript y que su HTML, imágenes y enlaces responden.
2. Añadir un `rel="canonical"` y un `og:url` absolutos con esa misma URL. Convertir `og:image` y `twitter:image` a la URL HTTPS absoluta del JPEG 1200 × 630 ya publicado y comprobar que devuelve 200 con su tipo correcto. Revisar la vista previa social.
3. Solo tras aprobar el lanzamiento, retirar el `noindex` de la landing, comprobar que no exista un `X-Robots-Tag` contradictorio y permitir el rastreo de la URL y sus recursos. No sobrescribir reglas del host sin revisar su alcance ni usar robots.txt como protección de autenticación.
4. Publicar el sitemap con la misma URL canónica y referenciarlo desde robots.txt. Mantener las rutas de aplicación y login fuera de este sitemap. Validar respuestas HTTPS, canonical y rastreabilidad antes de verificar la propiedad pública en Search Console, inspeccionar la URL y enviar el sitemap.

Actualizar las pruebas que hoy preservan el contrato de vista previa en ese mismo cambio autorizado. No activar DNS, Search Console ni la indexación con este PR.

### Plantillas inertes

Sustituir todos los marcadores por URLs aprobadas. Estos bloques son documentación, no archivos de `public/`.

```html
<link rel="canonical" href="URL_PUBLICA_HTTPS_CONFIRMADA">
<meta property="og:url" content="URL_PUBLICA_HTTPS_CONFIRMADA">
<meta property="og:image" content="URL_HTTPS_CONFIRMADA_IMAGEN_SOCIAL">
<meta name="twitter:image" content="URL_HTTPS_CONFIRMADA_IMAGEN_SOCIAL">
```

Fragmento para integrar al robots.txt del host, sin descartar sus reglas vigentes:

```text
Sitemap: URL_HTTPS_CONFIRMADA_SITEMAP
```

```xml
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>URL_PUBLICA_HTTPS_CONFIRMADA</loc>
  </url>
</urlset>
```

Referencias oficiales: [SEO Starter Guide](https://developers.google.com/search/docs/fundamentals/seo-starter-guide), [URLs canónicas](https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls) y [sitemaps](https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap).
