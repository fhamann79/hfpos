import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';

const browserRoot = new URL('../dist/pos-frontend/browser/', import.meta.url);
const landingRoot = new URL('conoce-hf-one/', browserRoot);
const html = await readFile(new URL('index.html', landingRoot), 'utf8');
const { document } = new JSDOM(html).window;
const demoMessage =
  'Hola, deseo conocer HF One para mi negocio y coordinar una demostración por videollamada. ¿Podemos conversar?';
const whatsapp = `https://wa.me/593983842862?text=${encodeURIComponent(demoMessage)}`;
const mailto = 'mailto:fhamann79@hotmail.com';
const normalizedText = (element) => element?.textContent.replace(/\s+/g, ' ').trim();

test('the built landing preserves preview metadata and navigable sections', () => {
  assert.equal(document.documentElement.lang, 'es-EC');
  assert.match(document.title, /HF One/);
  assert.ok(document.querySelector('meta[name="description"]')?.content);
  assert.equal(document.querySelector('meta[name="robots"]')?.content, 'noindex');
  assert.equal(document.querySelectorAll('main h1').length, 1);
  const headline = document.querySelector('main h1');
  assert.equal(normalizedText(headline.firstElementChild), 'HF One');
  assert.equal(normalizedText(headline), 'HF One Tu negocio, bajo control. Tus ventas, más ágiles.');
  assert.equal(normalizedText(document.querySelector('.hero-signature')), 'Tu negocio, todo en uno.');
  for (const id of ['contenido', 'beneficios', 'funcionalidades', 'como-funciona', 'sectores', 'preguntas', 'demostracion']) {
    assert.ok(document.querySelector(`a[href="#${id}"]`), id);
    assert.ok(document.getElementById(id), id);
  }
  for (const anchor of document.querySelectorAll('a[href^="#"]')) {
    assert.ok(document.getElementById(anchor.hash.slice(1)), anchor.hash);
  }
  assert.equal(document.querySelectorAll('#preguntas details > summary').length, 6);
  assert.equal(document.querySelectorAll('.capabilities li').length, 4);
  assert.equal(document.querySelectorAll('.features dt').length, 7);
  assert.equal(document.querySelectorAll('.steps li').length, 3);
  assert.equal(document.querySelectorAll('.sectors li').length, 5);
  for (const grid of document.querySelectorAll('.features, .sectors')) {
    assert.equal([...grid.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim()), false);
  }
  const navLinks = (selector) => [...document.querySelectorAll(selector)].map((link) => link.hash);
  assert.ok(document.querySelector('.compact-nav > summary'));
  assert.deepEqual(navLinks('.compact-nav nav a'), navLinks('.primary-nav a'));
  for (const region of ['header', 'footer']) {
    assert.ok(document.querySelector(`${region} a[href="/login"]`), region);
  }
});

test('SEO metadata and structured data stay valid without activating indexation', async () => {
  const meta = (attr, key) => document.querySelector(`meta[${attr}="${key}"]`)?.content;
  assert.match(document.title, /Punto de venta y facturación electrónica para Ecuador/);
  assert.equal(meta('property', 'og:locale'), 'es_EC');
  assert.equal(meta('name', 'twitter:card'), 'summary_large_image');
  for (const [attr, prefix] of [['property', 'og'], ['name', 'twitter']]) {
    assert.ok(meta(attr, `${prefix}:title`));
    assert.ok(meta(attr, `${prefix}:description`));
    assert.ok(meta(attr, `${prefix}:image:alt`));
    assert.equal(meta(attr, `${prefix}:image`), '/conoce-hf-one/social-hf-one.jpg');
  }
  assert.equal(meta('property', 'og:image:width'), '1200');
  assert.equal(meta('property', 'og:image:height'), '630');
  assert.equal(meta('property', 'og:image:type'), 'image/jpeg');
  assert.equal(document.querySelector('link[rel="canonical"], meta[property="og:url"], meta[name="twitter:url"]'), null);
  const scripts = document.querySelectorAll('script');
  assert.equal(scripts.length, 1);
  assert.equal(scripts[0].type, 'application/ld+json');
  assert.equal(scripts[0].hasAttribute('src'), false);
  const { description, ...application } = JSON.parse(scripts[0].textContent);
  assert.ok(description.includes('Ecuador'));
  assert.deepEqual(application, {
    '@context': 'https://schema.org',
    '@type': 'SoftwareApplication',
    name: 'HF One',
    applicationCategory: 'BusinessApplication',
    operatingSystem: 'Web',
    publisher: { '@type': 'Organization', name: 'Comercial HFSISTEMAS' },
  });
  for (const filename of ['robots.txt', 'sitemap.xml']) {
    await assert.rejects(readFile(new URL(filename, browserRoot)), { code: 'ENOENT' });
  }
});

test('the only external links are the approved commercial contacts', () => {
  for (const region of ['header', '.hero', '#demostracion', 'footer']) {
    assert.ok(document.querySelector(`${region} a[href="${whatsapp}"]`), region);
  }
  for (const region of ['#demostracion', 'footer']) {
    assert.ok(document.querySelector(`${region} a[href="${mailto}"]`), region);
  }
  for (const anchor of document.querySelectorAll('a[href]')) {
    const href = anchor.getAttribute('href');
    if (href.startsWith('#') || href === '/login' || href === mailto) continue;
    assert.equal(href, whatsapp);
    assert.equal(new URL(href).searchParams.get('text'), demoMessage);
    assert.equal(anchor.target, '_blank');
    assert.ok(anchor.relList.contains('noopener'));
    assert.ok(anchor.relList.contains('noreferrer'));
    assert.equal(anchor.hasAttribute('ping'), false);
  }
  assert.match(normalizedText(document.querySelector('footer .privacy')), /sujeta a sus condiciones y políticas/);
});

test('the landing loads only local build assets and does not collect data', async () => {
  assert.equal(document.querySelector('form, input, textarea, iframe, object, embed, base, [style], [ping]'), null);
  assert.equal(document.querySelector('meta[http-equiv]'), null);
  for (const element of document.querySelectorAll('*')) {
    assert.equal(element.getAttributeNames().some((name) => name.startsWith('on')), false);
  }
  const resources = document.querySelectorAll('[src], [srcset], link[href], use[href]');
  const allowed = new Set([
    '/hf-one-logo.svg',
    '/hf-one-mark.svg',
    '/conoce-hf-one/landing.css',
    '/conoce-hf-one/pos-laboratorio.webp',
    '/conoce-hf-one/equipo.jpg',
    '/conoce-hf-one/icons.svg',
  ]);
  const used = new Set();
  const icons = new Set();
  for (const resource of resources) {
    assert.equal(resource.hasAttribute('srcset'), false);
    const reference = resource.getAttribute('src') ?? resource.getAttribute('href');
    const [path, fragment] = reference.split('#');
    if (resource.localName === 'use') {
      assert.equal(path, '/conoce-hf-one/icons.svg');
      assert.match(fragment, /^[a-z]+$/);
      icons.add(fragment);
    } else {
      assert.equal(fragment, undefined);
    }
    assert.ok(allowed.has(path), path);
    used.add(path);
  }
  assert.deepEqual(used, allowed);
  for (const path of used) {
    const data = await readFile(new URL(path.slice(1), browserRoot));
    assert.ok(data.length > 0, path);
    if (path.endsWith('.webp')) {
      assert.equal(data.toString('ascii', 8, 12), 'WEBP', path);
      assert.ok(data.length < 150_000, path);
    }
    if (path.endsWith('.css')) {
      const css = data.toString('utf8');
      assert.doesNotMatch(css, /@import/i);
      const urls = new Set([...css.matchAll(/url\(\s*["']?([^"'\s)]+)["']?\s*\)/gi)].map((match) => match[1]));
      assert.deepEqual(urls, new Set(['/conoce-hf-one/hero-ambiente.jpg', '/conoce-hf-one/sectores.jpg']));
    }
    if (path.endsWith('/icons.svg')) {
      const sprite = new JSDOM(data.toString('utf8'), { contentType: 'image/svg+xml' }).window.document;
      assert.equal(sprite.querySelector('script, foreignObject, [href]'), null);
      for (const icon of icons) {
        assert.equal(sprite.getElementById(icon)?.localName, 'symbol', icon);
      }
    }
  }
  for (const filename of ['hero-ambiente.jpg', 'sectores.jpg', 'equipo.jpg', 'social-hf-one.jpg']) {
    const data = await readFile(new URL(filename, landingRoot));
    assert.equal(data.readUInt16BE(0), 0xffd8, filename);
    assert.ok(data.length < 200_000, filename);
  }
  assert.match(normalizedText(document.querySelector('.hero figcaption')), /datos ficticios/);
});
