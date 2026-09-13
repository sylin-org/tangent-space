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
  assert.match(html, /class="role-appearance-layout"/);
  assert.match(html, /id="role-preview"[^>]+aria-label="Role preview"/);
  assert.match(html, /id="role-member-resolve"[^>]*>Find participant</);
  assert.match(html, /id="role-member-resolved"/);
  assert.match(html, /id="role-member-protected"/);
  assert.match(html, /server owner/);
  assert.doesNotMatch(html, /canonical owner/i);
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
  assert.match(html, /id=\"role-member-lookup-status\"/);
  assert.match(html, /id=\"role-member-identifier\"[^>]*aria-describedby=\"role-member-lookup-status\"/);
  assert.match(script, /showOverview/);
  assert.match(script, /showEditor/);
  assert.match(script, /bindingTotal > bindings\.length \? '≥'/);
  assert.match(script, /protectedOwnerRole/);
  assert.match(script, /canManageMembers/);
  assert.match(script, /document\.querySelectorAll\('\[data-role-editor-tab\]'\)\.forEach/);
  assert.match(script, /roleEditorTab\(target\.dataset\.roleEditorTab\)/);
  assert.match(script, /event\.key === 'Home'/);
  assert.match(script, /event\.key === 'End'/);
  assert.match(script, /target\.focus\(\)/);
  assert.doesNotMatch(script, /canonical owner/i);
  assert.match(script, /async function resolveMember/);
  assert.match(script, /Confirm the assignment/);
  assert.match(script, /Remove role/);
  assert.match(script, /normalizeLookupIdentifier/);
  assert.match(script, /memberLabelCache/);
  assert.match(script, /role-member-lookup-status/);
});

test('role editor mobile layout and copy match compactness and member-row constraints', () => {
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-editor-heading \{[\s\S]*?padding:12px 16px;/s);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-editor-tabs \{[\s\S]*?padding:0 16px 0;/s);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-editor-actions \{[\s\S]*?padding:12px 0 16px;/s);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-member \{[\s\S]*?grid-template-columns:36px minmax\(0,1fr\)/s);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-member-remove \{[\s\S]*?grid-column:1 \/ -1/);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-member-remove \{[\s\S]*?min-height:44px/);
});

test('role member lookup path includes local label duplicate short-circuit and inline lookup status', () => {
  assert.match(script, /const duplicateLabel = \[\.\.\.memberLabelCache\.values\(\)\]\.find/);
  assert.match(script, /role-member-lookup-status/);
  assert.match(script, /already has this role/);
});
