# Publicación de landing HF One — FE-175

## Estado real y fuente

La landing comercial está **implementada e integrada** desde PR #174 en `/conoce-hf-one/`. 
El cambio FE-175 prepara la publicación en la URL canónica **https://hfone.app/**.
**Código preparado/mergeado no implica despliegue productivo:** RC8 sigue operativo hasta una R4 humana separada.

## Funcionamiento previsto

- `https://hfone.app/` sirve el HTML estático comercial desde `/conoce-hf-one/index.html`, sin ejecutar la SPA.
- `https://hfone.app/login` y `/platform/login`: Angular y los guards existentes, sin cambios.
- `/api/*` y `/health/*`: proxy existente a backend, sin cambios.
- Assets de la landing permanecen en `/conoce-hf-one/`; la URL histórica puede verse, pero declara canonical de la raíz.
- SPA `index.html` declara `X-Robots-Tag: noindex, nofollow` por Nginx; el sitio comercial no hereda ese encabezado.
- `robots.txt` y `sitemap.xml` enumeran solo la URL comercial canónica, nunca login/tenant/API.

## www

`www.hfone.app` **no se habilita automáticamente**. Antes de añadir redirección HTTP 308 hacia `https://hfone.app/`, comprobar resolución DNS y que el certificado TLS cubre también `www.hfone.app`.
No crear host `www` que produzca alerta de certificado. Usar `hfone.app` como dominio canónico.

## Puerta R4 de publicación web-only

1. CI/revisión independiente del PR y aceptación humana pertinente; el merge no es permiso de despliegue.
2. Construir/publicar por workflow autorizado una **imagen web inmutable** a partir del commit aprobado; backend y migrator permanecen fijados al RC8 actual. No reutilizar la imagen web RC8 antigua ni desplegar `main` directamente.
3. Respaldar `pilot.env` y conservar digest web RC8. Actualizar **solo** `HFPOS_WEB_IMAGE`; no migraciones EF, stop de PostgreSQL, reconfiguración fiscal ni cambio de `HFPOS_RELEASE_VERSION` del backend.
4. Una sola comprobación funcional de `/` (HTML comercial, canonical, imágenes y contactos), `/login` (SPA), `/platform/login`, `/api`/health y HTTPS. Si falla, volver **solo imagen web** al digest RC8 conocido, sin DB Down.
5. Tras la activación verificar 200, HTTPS, `robots.txt`/`sitemap.xml`, canonical/OG y ausencia de `X-Robots-Tag: noindex` en landing. Solo entonces tramitar Search Console si es oportuno.

No usar recursos/certificados/secrets/SRI reales en agentes ni ejecutar operaciones R4 dentro de un PR. No afirmar indexación inmediata en buscadores ni resultados SEO garantizados.
