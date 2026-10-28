import test from 'node:test';
import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';

// The app never blocks the page: confirmations stay inline and forms open as in-page panels.
const root = new URL('../src/server/web/wwwroot/', import.meta.url);
const blocking = /\b(?:alert|confirm|prompt)\(|showModal\(|<dialog\b|createElement\(\s*['"]dialog['"]\s*\)/;

test('the browser app opens no dialog', async () => {
  const files = (await readdir(root, { recursive: true })).filter(name => /\.(?:js|mjs|html)$/.test(name));
  assert.ok(files.length > 0);
  for (const name of files) assert.doesNotMatch(await readFile(new URL(name.replaceAll('\\', '/'), root), 'utf8'), blocking, name);
});
