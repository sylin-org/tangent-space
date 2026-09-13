import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../src/server/web/wwwroot/profile.js', import.meta.url), 'utf8');

test('identity quarantine removes every participant value and decoration key', () => {
  const nodes = new Map();
  const node = id => {
    if (!nodes.has(id)) nodes.set(id, {
      id, dataset: {}, children: [], textContent: '', hidden: false,
      replaceChildren(...children) { this.children = children; this.textContent = ''; }
    });
    return nodes.get(id);
  };
  const window = { TangentPages: { route: { kind: 'home' } }, addEventListener() {} };
  const context = { window, document: { getElementById: node }, location: { pathname: '/', search: '', hash: '' }, CustomEvent: class {} };
  vm.runInNewContext(source, context);

  node('participant-profile').dataset.profileDid = 'did:plc:old';
  for (const id of ['profile-name', 'profile-avatar']) Object.assign(node(id), {
    textContent: 'Old identity', children: [{ tag: 'img' }],
    dataset: { profileDid: 'did:plc:old', profilePart: id, profileFallback: 'Old', profileVersion: '1' }
  });
  for (const id of ['profile-handle', 'profile-did', 'profile-description', 'profile-classification', 'profile-joined', 'profile-roles']) node(id).textContent = 'private';
  node('profile-posts').children = [{ text: 'private post' }];
  node('profile-actions').children = [{ text: 'private action' }];

  window.TangentProfile.clear();

  assert.deepEqual(node('participant-profile').dataset, {});
  for (const id of ['profile-name', 'profile-avatar']) {
    assert.equal(node(id).textContent, '');
    assert.deepEqual(node(id).children, []);
    assert.deepEqual(node(id).dataset, {});
  }
  for (const id of ['profile-handle', 'profile-did', 'profile-description', 'profile-classification', 'profile-joined', 'profile-roles']) assert.equal(node(id).textContent, '');
  assert.deepEqual(node('profile-posts').children, []);
  assert.deepEqual(node('profile-actions').children, []);
});
