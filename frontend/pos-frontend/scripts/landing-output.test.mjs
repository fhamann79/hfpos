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
  for (const region of ['header', 'footer']) {
    assert.ok(document.querySelector(`${region} a[href="/login"]`), region);
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
  assert.equal(document.querySelector('script, form, input, iframe, object, embed, base, [style], [ping]'), null);
  assert.equal(document.querySelector('meta[http-equiv]'), null);
  const resources = document.querySelectorAll('[src], [srcset], link[href]');
  const allowed = new Set([
    '/hf-one-logo.svg',
    '/hf-one-mark.svg',
    '/conoce-hf-one/landing.css',
    '/conoce-hf-one/pos-laboratorio.webp',
  ]);
  const used = new Set();
  for (const resource of resources) {
    assert.equal(resource.hasAttribute('srcset'), false);
    const path = resource.getAttribute('src') ?? resource.getAttribute('href');
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
      assert.doesNotMatch(data.toString('utf8'), /@import|url\s*\(/i);
    }
  }
  assert.match(normalizedText(document.querySelector('.hero figcaption')), /datos ficticios/);
});
