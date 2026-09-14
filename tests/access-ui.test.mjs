import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const html = await readFile(new URL('../src/server/web/wwwroot/index.html', import.meta.url), 'utf8');
const script = await readFile(new URL('../src/server/web/wwwroot/access.js', import.meta.url), 'utf8');
const css = await readFile(new URL('../src/server/web/wwwroot/access.css', import.meta.url), 'utf8');

test('one Access panel serves server, Tangent, and Topic settings routes', () => {
  assert.match(html, /id="settings-access-tab"[^>]+data-settings-tab="access"/);
  assert.match(html, /id="settings-access-panel"/);
  assert.match(script, /current\.kind === 'settings'/);
  assert.match(script, /current\.kind === 'tangent-settings'/);
  assert.match(script, /current\.kind === 'topic-settings'/);
  assert.match(script, /kind: 'server', scope: \{ type: 'host', id: 'site' \}/);
  assert.match(script, /kind: 'tangent', tangent: current\.tangent, scope: \{ type: 'tangent', id: current\.tangent \}/);
  assert.match(script, /kind: 'topic', tangent: current\.tangent, topic: current\.topic, scope: \{ type: 'topic', id: current\.topic \}/);
});

test('Access translates friendly audiences into bounded Koan role policies', () => {
  assert.match(script, /page=1&pageSize=100/);
  assert.match(script, /'Everyone'/);
  assert.match(script, /'Signed-in participants'/);
  assert.match(script, /'Selected roles'/);
  assert.match(script, /\{ kind: 0 \}, \{ kind: 1 \}/);
  assert.match(script, /\{ kind: 3, value \}/);
  assert.match(script, /'If-Match'/);
  assert.match(script, /unshownSelected/);
  assert.doesNotMatch(script, /await load\(true\)/);
  assert.equal((script.match(/^\s+configureShell\(context, resource\);/gm) || []).length, 1);
  assert.match(script, /state\.textContent = mode === 'inherit'/);
  assert.match(script, /status\.textContent = label \+ ' saved\.'/);
  assert.match(script, /contextSignature\(context\) !== contextSignature\(contextFor\(route\(\)\)\)/);
  assert.match(css, /\.access-role-picker/);
});

test('Tangent and Topic Details are editable in the shared settings shell', () => {
  assert.match(html, /id="settings-context-editor"/);
  assert.match(script, /'Tangent name', 'name'/);
  assert.match(script, /'Topic title', 'title'/);
  assert.match(script, /allowMemberTopics/);
  assert.match(script, /allowPostEditing/);
  assert.match(script, /\/topics\/.*\/settings/);
  assert.match(script, /method: 'PATCH'/);
});
