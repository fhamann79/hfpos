import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { test } from 'node:test';

const read = path => readFileSync(resolve(path), 'utf8');
const config = JSON.parse(read('angular.json')).projects['pos-frontend'].architect;

test('Production environment is same-origin and contains no localhost', () => {
  const source = read('src/environments/environment.ts');
  assert.match(source, /apiUrl:\s*''/);
  assert.doesNotMatch(source, /localhost/);
  assert.equal(config.build.defaultConfiguration, 'production');
  assert.equal(config.build.configurations.production.fileReplacements, undefined);
});

test('Development and serve select the local API environment', () => {
  assert.match(read('src/environments/environment.development.ts'), /https:\/\/localhost:7096/);
  assert.deepEqual(config.build.configurations.development.fileReplacements, [{
    replace: 'src/environments/environment.ts', with: 'src/environments/environment.development.ts',
  }]);
  assert.equal(config.serve.defaultConfiguration, 'development');
  assert.equal(config.serve.configurations.development.buildTarget, 'pos-frontend:build:development');
});

if (process.argv.includes('--bundle')) {
  test('Built Production JavaScript never includes the Development API endpoint', () => {
    const files = readdirSync('dist/pos-frontend/browser').filter(file => file.endsWith('.js'));
    assert.ok(files.length > 0);
    for (const file of files) assert.doesNotMatch(read(`dist/pos-frontend/browser/${file}`), /localhost:7096/);
  });
}
