import test from 'node:test';
import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

// The browser never opens a dialog: a destructive action confirms inline and a form opens as a panel in the document.
const base = fileURLToPath(new URL('../../', import.meta.url));
const blocking = [
  [/\b(?:alert|confirm|prompt)\(/, 'a blocking call'],
  [/<dialog\b/, 'a dialog element'],
  [/showModal/, 'showModal'],
];
const binary = /\.(?:png|jpe?g|gif|webp|ico|woff2?|ttf|otf|zip|gz|pdf|wasm|mp[34]|webm)$/i;

async function nodes(directory) {
  try {
    return await readdir(directory, { recursive: true, withFileTypes: true });
  } catch (failure) {
    if (failure.code === 'ENOENT') return [];
    throw failure;
  }
}

// src/client (source) and every wwwroot under src (built bundles and statics).
async function scannedFiles() {
  const sources = [];
  for (const node of await nodes(join(base, 'src'))) {
    if (!node.isFile() || binary.test(node.name)) continue;
    const path = join(node.parentPath, node.name);
    const inSrc = relative(join(base, 'src'), path).replaceAll(sep, '/');
    if (/(^|\/)(node_modules|bin|obj)\//.test(inSrc)) continue;
    if (inSrc.startsWith('client/') || /(^|\/)wwwroot\//.test(inSrc)) sources.push(path);
  }
  return sources;
}

test('no browser file opens a dialog', async () => {
  const violations = [];
  for (const path of await scannedFiles()) {
    const lines = Array.from((await readFile(path, 'utf8')).matchAll(/[^\r\n]*(?:\r\n|\n|$)/g), found => found[0]).filter((line, index, all) => line !== '' || index < all.length - 1);
    lines.forEach((line, index) => {
      for (const [pattern, name] of blocking) {
        if (pattern.test(line)) violations.push(`${relative(base, path).replaceAll(sep, '/')}:${index + 1}: no-dialogs: ${name}`);
      }
    });
  }
  for (const violation of violations) console.log(violation);
  assert.deepEqual(violations, []);
});
