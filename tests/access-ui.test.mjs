import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const html = await readFile(new URL('../src/server/web/wwwroot/index.html', import.meta.url), 'utf8');
const script = await readFile(new URL('../src/server/web/wwwroot/access.js', import.meta.url), 'utf8');
const css = await readFile(new URL('../src/server/web/wwwroot/access.css', import.meta.url), 'utf8');

test('one Access panel serves Server, Tangent, and Topic maps', () => {
  assert.match(html, /id="settings-access-tab"[^>]+data-settings-tab="access"/);
  assert.match(html, /id="settings-access-panel"/);
  assert.match(script, /kind: 'server'[\s\S]+accessPath: '\/api\/server\/access'/);
  assert.match(script, /kind: 'tangent'[\s\S]+\/api\/v1\/tangents\//);
  assert.match(script, /kind: 'topic'[\s\S]+\/topics\/[\s\S]+\/access/);
  assert.match(script, /\['see', 'Who can see'/);
  assert.match(script, /\['post', 'Who can post'/);
  assert.match(script, /\['manage', 'Who can manage'/);
  assert.match(script, /\['createTopics', 'Who can create Topics'/);
});

test('Access is a compact role-token editor, not a policy-mode form', () => {
  assert.match(script, /\/api\/identity\/roles\?page=1&pageSize=100/);
  assert.match(script, /'@everyone'/);
  assert.match(script, /'@authenticated'/);
  assert.match(script, /Type @role to add/);
  assert.match(script, /className = 'access-chip'/);
  assert.match(script, /Remove ' \+ text\.textContent/);
  assert.match(script, /Use Tangent default/);
  assert.match(script, /Object\.fromEntries\(context\.decisions/);
  assert.match(script, /method: 'PUT'/);
  assert.doesNotMatch(script, /scoped-roles|Selected roles|Signed-in participants|access-choice/);
  assert.match(css, /\.access-map-card/);
  assert.match(css, /\.access-combobox/);
  assert.match(css, /\.access-suggestions/);
  assert.match(css, /\.access-chip-row\.is-inherited/);
});

test('Details fields are explicit visible controls and topic creation is only Access', () => {
  assert.match(script, /'Tangent name', 'name'/);
  assert.match(script, /'Topic title', 'title'/);
  assert.match(script, /settings-control/);
  assert.match(script, /\/topics\/.*\/settings/);
  assert.match(script, /method: 'PATCH'/);
  assert.doesNotMatch(script, /allowMemberTopics/);
  assert.match(css, /\.settings-context-fields \.settings-control[\s\S]+display: block/);
});

test('token combobox supports keyboard selection and bounded suggestions', () => {
  assert.match(script, /setAttribute\('role', 'combobox'\)/);
  assert.match(script, /setAttribute\('role', 'listbox'\)/);
  assert.match(script, /event\.key === 'ArrowDown'/);
  assert.match(script, /event\.key === 'ArrowUp'/);
  assert.match(script, /event\.key === 'Enter'/);
  assert.match(script, /event\.key === 'Escape'/);
  assert.match(script, /setAttribute\('aria-labelledby', title\.id\)/);
  assert.match(script, /setAttribute\('aria-activedescendant'/);
  assert.match(script, /button\.id = list\.id \+ '-' \+ index/);
  assert.match(script, /\.slice\(0, 12\)/);
});

test('inherited Topic audiences stay visible and become a preserved local override when extended', () => {
  assert.match(script, /view\.parent\?\.\[key\] \|\| view\.effective\?\.\[key\]/);
  assert.match(script, /if \(draft\[key\] == null\) draft\[key\] = \[\.\.\.chosen\(\)\]/);
  assert.match(script, /const signature = contextSignature\(context\)/);
  assert.match(script, /signature !== contextSignature\(contextFor\(route\(\)\)\)/);
});
