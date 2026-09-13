import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const html = await readFile(new URL('../src/server/web/wwwroot/index.html', import.meta.url), 'utf8');
const css = await readFile(new URL('../src/server/web/wwwroot/landing.css', import.meta.url), 'utf8');
const script = await readFile(new URL('../src/server/web/wwwroot/roles.js', import.meta.url), 'utf8');

test('role management is a first-class settings tab with an adaptive rail', () => {
  assert.match(html, /id="settings-roles-tab"[^>]+role="tab"/);
  assert.match(html, /id="settings-roles-panel"[^>]+role="tabpanel"/);
  assert.match(html, /id="role-overview"/);
  assert.match(html, /id="role-directory-list"[^>]+role="listbox"/);
  assert.match(html, /id="role-create"[^>]*>Create role</);
  assert.match(html, /data-role-editor-tab="appearance"/);
  assert.match(html, /data-role-editor-tab="permissions"/);
  assert.match(html, /data-role-editor-tab="members"/);
  assert.match(html, /id="role-back"[^>]*>← All roles</);
  assert.match(css, /\.settings-shell\s*\{[^}]*grid-template-columns:210px minmax\(0,1fr\)/s);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.settings-tabs\s*\{[^}]*display:flex/s);
  assert.match(css, /@media\(max-width:1000px\)[^{]*\{[^}]*\.role-editor-workspace\s*\{[^}]*grid-template-columns:1fr/s);
});

test('role UI uses Koan scoped roles with mutation safety and bounded identity lookup', () => {
  assert.match(script, /\/api\/identity\/scoped-roles\/descriptor/);
  assert.match(script, /\/api\/roles\/ui\/session/);
  assert.match(script, /'If-Match'/);
  assert.match(script, /listing\.rooms \|\| listing\.channels \|\| \[\]/);
  assert.match(script, /slice\(0, 50\)/);
  assert.match(script, /showOverview/);
  assert.match(script, /showEditor/);
  assert.match(script, /bindingTotal > bindings\.length \? '≥'/);
});
