import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';

const browserRoot = new URL('../dist/pos-frontend/browser/', import.meta.url);
const html = await readFile(new URL('conoce-hf-one/index.html', browserRoot), 'utf8');
const { document } = new JSDOM(html).window;

test('the built landing exposes metadata and navigable anchors without taking over login', () => {
  assert.equal(document.documentElement.lang, 'es-EC');
  assert.match(document.title, /HF One/);
  assert.ok(document.querySelector('meta[name="description"]')?.content);
  assert.equal(document.querySelector('meta[name="robots"]')?.content, 'noindex');
  assert.equal(document.querySelectorAll('main h1').length, 1);
  const headline = document.querySelector('main h1');
  assert.equal(headline.firstElementChild?.textContent, 'HF One');
  assert.equal(headline.textContent.replace(/\s+/g, ' ').trim(), 'HF One Tu negocio, todo en uno.');
  assert.ok(document.querySelector('a[href="/login"]'));
  for (const id of ['contenido', 'funcionalidades', 'como-funciona', 'sectores', 'preguntas', 'demostracion']) {
    assert.ok(document.querySelector(`a[href="#${id}"]`), id);
    assert.ok(document.getElementById(id), id);
  }
  for (const anchor of document.querySelectorAll('a[href^="#"]')) {
    assert.ok(document.getElementById(anchor.hash.slice(1)), anchor.hash);
  }
  assert.ok(document.querySelector('#preguntas details > summary'));
});

test('the pending demo does not collect data, and every public asset is local and built', async () => {
  assert.equal(document.querySelector('script, form, input, iframe, [onclick]'), null);
  const demo = document.querySelector('#demostracion button');
  assert.ok(demo?.disabled);
  assert.ok(document.getElementById(demo.getAttribute('aria-describedby'))?.textContent.trim());
  const resources = document.querySelectorAll('img[src], source[srcset], link[href]');
  for (const resource of resources) {
    const path = resource.getAttribute('src') ?? resource.getAttribute('srcset') ?? resource.getAttribute('href');
    assert.match(path, /^\/(?!\/)/);
    const data = await readFile(new URL(path.slice(1), browserRoot));
    assert.ok(data.length > 0, path);
    if (path.endsWith('.webp')) {
      assert.equal(data.toString('ascii', 8, 12), 'WEBP', path);
      assert.ok(data.length < 150_000, path);
    }
  }
  for (const anchor of document.querySelectorAll('a[href]')) {
    assert.match(anchor.getAttribute('href'), /^(?:#|\/login$)/);
  }
});
