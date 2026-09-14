import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const html = await readFile(new URL('../src/server/web/wwwroot/index.html', import.meta.url), 'utf8');
const css = await readFile(new URL('../src/server/web/wwwroot/landing.css', import.meta.url), 'utf8');
const script = await readFile(new URL('../src/server/web/wwwroot/roles.js', import.meta.url), 'utf8');
const controller = await readFile(new URL('../src/server/web/Authorization/RoleUiController.cs', import.meta.url), 'utf8');

test('role management is a first-class settings tab with adaptive list and editor', () => {
  assert.match(html, /id="settings-roles-tab"[^>]+role="tab"/);
  assert.match(html, /id="role-directory-list"[^>]+role="listbox"/);
  assert.match(html, /id="role-create"[^>]*>Create role</);
  assert.match(html, /data-role-editor-tab="appearance"/);
  assert.match(html, /data-role-editor-tab="permissions"/);
  assert.match(html, /data-role-editor-tab="members"/);
  assert.match(html, /id="role-member-open"[^>]*>Add member</);
  assert.match(html, /id="role-retire"[^>]*>Delete role</);
  assert.match(css, /\.settings-shell\s*\{[^}]*grid-template-columns:210px minmax\(0,1fr\)/s);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.settings-tabs\s*\{[^}]*display:grid/s);
});

test('role UI uses the singular Koan Role collection without versions or scoped policy APIs', () => {
  assert.match(script, /\/api\/identity\/roles/);
  assert.match(script, /\/api\/roles\/ui\/descriptor/);
  assert.match(script, /permissions: roleGrants\(\)/);
  assert.match(script, /metadata: \{ purpose:/);
  assert.match(script, /method: selected\._new \? 'PUT' : 'PATCH'/);
  assert.match(script, /method: 'DELETE'/);
  assert.match(script, /\/members/);
  assert.doesNotMatch(script, /scoped-roles|If-Match|selected\.version|binding|tombstone/i);
});

test('Appearance and Members preserve the delightful interaction requirements', () => {
  assert.match(script, /updateRolePreview/);
  assert.match(script, /metadata\.purpose/);
  assert.match(script, /metadata\.color/);
  assert.match(script, /showAvatar/);
  assert.match(script, /memberByline/);
  assert.match(script, /async function resolveMember/);
  assert.match(script, /Confirm the assignment/);
  assert.match(script, /'role-member-remove', '×'/);
  assert.match(script, /remove\.setAttribute\('aria-label', removeLabel\)/);
  assert.match(script, /already has this role/);
  assert.match(script, /const roleMemberPageSize = 50/);
  assert.match(controller, /ParticipantWindow = 50/);
  assert.match(controller, /profile\.DisplayName \?\? profile\.Handle \?\? "Participant"/);
});

test('role editor keyboard navigation and responsive member rows remain accessible', () => {
  assert.match(script, /event\.key === 'Home'/);
  assert.match(script, /event\.key === 'End'/);
  assert.match(script, /target\.focus\(\)/);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-member \{[\s\S]*?grid-template-columns:36px minmax\(0,1fr\) 44px/s);
  assert.match(css, /@media\(max-width:760px\)[\s\S]*?\.role-member-remove \{[\s\S]*?width:44px; height:44px/);
});

test('switching between Server settings tabs does not discard a dirty role draft', () => {
  assert.match(script, /name === 'roles' && !\(dirty && selected\)/);
});

test('an optional permission descriptor failure cannot erase a successful role list', () => {
  assert.match(script, /Promise\.allSettled/);
  assert.match(script, /rolePageResult\.status === 'rejected'/);
  assert.match(script, /Roles loaded\. Permission details are temporarily unavailable/);
  assert.match(script, /if \(descriptorError\) return \[\.\.\.new Set/);
});
