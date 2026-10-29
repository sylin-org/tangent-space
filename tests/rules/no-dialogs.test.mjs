import test from 'node:test';
import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

// The browser never opens a dialog: a destructive action confirms inline and a form opens as a panel in the page.
const root = fileURLToPath(new URL('../../', import.meta.url));
const blocking = [
  [/\b(?:alert|confirm|prompt)\(/, 'a blocking call'],
  [/<dialog\b/, 'a dialog element'],
  [/showModal/, 'showModal'],
];
const binary = /\.(?:png|jpe?g|gif|webp|ico|woff2?|ttf|otf|zip|gz|pdf|wasm|mp[34]|webm)$/i;

async function entries(directory) {
  try {
    return await readdir(directory, { recursive: true, withFileTypes: true });
  } catch (error) {
    if (error.code === 'ENOENT') return [];
    throw error;
  }
}

// src/client (source) and every wwwroot under src (built bundles and statics).
async function scannedFiles() {
  const sources = [];
  for (const entry of await entries(join(root, 'src'))) {
    if (!entry.isFile() || binary.test(entry.name)) continue;
    const path = join(entry.parentPath, entry.name);
    const parts = relative(join(root, 'src'), path).split(sep);
    if (parts.includes('node_modules') || parts.includes('bin') || parts.includes('obj')) continue;
    if (parts[0] === 'client' || parts.includes('wwwroot')) sources.push(path);
  }
  return sources;
}

test('no browser file opens a dialog', async () => {
  const violations = [];
  for (const path of await scannedFiles()) {
    const lines = (await readFile(path, 'utf8')).split('\n');
    lines.forEach((line, index) => {
      for (const [pattern, name] of blocking) {
        if (pattern.test(line)) violations.push(`${relative(root, path).split(sep).join('/')}:${index + 1}: no-dialogs: ${name}`);
      }
    });
  }
  for (const violation of violations) console.log(violation);
  assert.deepEqual(violations, []);
});
