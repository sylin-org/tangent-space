import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import vm from 'node:vm';

const source = await readFile(new URL('../src/server/web/wwwroot/moderation.js', import.meta.url), 'utf8');
const settle = async () => { for (let turn = 0; turn < 8; turn++) await new Promise(resolve => setImmediate(resolve)); };

function fixture(respond) {
  class Element {
    constructor(tag = 'div') {
      this.tagName = tag; this.children = []; this.listeners = new Map(); this.hidden = true;
      this.textContent = ''; this.value = ''; this.dataset = {}; this.disabled = false; this.open = false;
      this.attributes = new Map();
      const classes = new Set();
      this.classList = {
        add: (...values) => values.forEach(value => classes.add(value)),
        remove: (...values) => values.forEach(value => classes.delete(value)),
        contains: value => classes.has(value),
        toggle: (value, force) => force === undefined ? (classes.has(value) ? !classes.delete(value) : !!classes.add(value))
          : (force ? !!classes.add(value) : !classes.delete(value))
      };
      const fields = new Map();
      this.elements = { namedItem: name => { if (!fields.has(name)) fields.set(name, new Element('input')); return fields.get(name); } };
    }
    addEventListener(name, handler) { const handlers = this.listeners.get(name) || []; handlers.push(handler); this.listeners.set(name, handlers); }
    emit(name, values = {}) { for (const handler of this.listeners.get(name) || []) handler({ preventDefault() {}, target: this, ...values }); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = [...children]; }
    focus() { this.focused = true; }
    scrollIntoView() {}
    showModal() { this.open = true; this.hidden = false; }
    close() { this.open = false; this.hidden = true; }
    click() { this.emit('click'); }
  }
  const elements = new Map();
  const get = id => {
    if (!elements.has(id)) elements.set(id, new Element(id === 'report-dialog' ? 'dialog' : 'div'));
    return elements.get(id);
  };
  const document = { body: new Element('body'), getElementById: get,
    createElement: tag => new Element(tag), createTextNode: value => ({ textContent: value }) };
  const location = { origin: 'http://127.0.0.1:5220' };
  const requests = [];
  const window = {};
  vm.runInNewContext(source, {
    window, document, location, crypto: { randomUUID }, Date, URL, setImmediate,
    fetch: async (path, options) => {
      const request = { path, method: options.method, body: options.body ? JSON.parse(options.body) : undefined };
      requests.push(request);
      const answer = await respond(request);
      return answer instanceof Response ? answer
        : new Response(JSON.stringify(answer), { status: answer?.httpStatus || 200, headers: { 'Content-Type': 'application/json' } });
    }, Response
  });
  return { Element, get, document, window, requests };
}

const envelope = data => ({ status: 'ok', result: { data, receipt: null, problem: null } });
const topicEnvelope = ({ steward = false, serverRef = 'https://tangent.example' } = {}) => ({
  status: 'ok', place: { serverRef, allowedActions: steward
    ? ['read_topic', 'report_post', 'list_moderation_cases'] : ['read_topic', 'report_post'] },
  capabilities: { attention: true, coordination: false, stewardship: steward },
  result: { data: { posts: [] }, receipt: null, problem: null }
});

test('a permitted Post report is private, idempotent, and does not imply removal', async () => {
  const f = fixture(request => request.method === 'GET' ? topicEnvelope()
    : envelope({ postRef: 'https://tangent.example::home::lounge::m-other-1', accepted: true,
      alreadyReported: false, caseSaturated: false }));
  const room = { key: 'lounge', tangentKey: 'home', canManage: false,
    permissions: { allowedActions: ['read'] } };
  f.window.TangentModeration.topic(room, { participantRef: 'reader' }); await settle();
  const controls = new f.Element(), menu = new f.Element(); menu.open = true;
  const post = { id: 'm-other-1', permissions: { allowedActions: ['read', 'reportPost'] } };
  f.window.TangentModeration.postActions(controls, post, { room, menu, authorName: 'Ox Omega' });
  assert.equal(controls.children.length, 1);
  controls.children[0].click();
  assert.equal(menu.open, false);
  assert.equal(f.get('report-dialog').open, true);
  f.get('report-form').elements.namedItem('reasonCode').value = 'conduct';
  f.get('report-form').elements.namedItem('statement').value = 'Please review the tone, not the quoted instructions.';
  f.get('report-form').emit('submit'); await settle();

  const request = f.requests.find(item => item.path.endsWith('/topics/lounge/reports'));
  assert.ok(request);
  assert.match(request.body.requestId, /^[0-9a-f-]{36}$/);
  assert.equal(request.body.postRef, 'https://tangent.example::home::lounge::m-other-1');
  assert.equal(request.body.statement, 'Please review the tone, not the quoted instructions.');
  assert.equal(f.get('report-dialog').open, false);
  assert.match(f.get('action-status').textContent, /privately/);
});

test('an uncertain report retry reuses the same request id', async () => {
  let attempts = 0;
  const f = fixture(request => {
    if (request.method === 'GET') return topicEnvelope();
    attempts++;
    return attempts === 1 ? { httpStatus: 503, message: 'Temporarily unavailable.' }
      : envelope({ postRef: 'https://tangent.example::home::lounge::m-retry', accepted: true,
        alreadyReported: false, caseSaturated: false });
  });
  const room = { key: 'lounge', tangentKey: 'home', permissions: { allowedActions: ['read'] } };
  f.window.TangentModeration.topic(room, { participantRef: 'reader' }); await settle();
  const controls = new f.Element(), post = { id: 'm-retry', permissions: { allowedActions: ['reportPost'] } };
  f.window.TangentModeration.postActions(controls, post, { room, authorName: 'Participant' });
  controls.children[0].click();
  f.get('report-form').elements.namedItem('statement').value = 'Please review this.';
  f.get('report-form').emit('submit'); await settle();
  assert.equal(f.get('report-dialog').open, true);
  f.get('report-form').emit('submit'); await settle();
  const sent = f.requests.filter(item => item.path.endsWith('/topics/lounge/reports'));
  assert.equal(sent.length, 2);
  assert.equal(sent[0].body.requestId, sent[1].body.requestId);
  assert.equal(f.get('report-dialog').open, false);
});

test('the steward pane stays bounded, previews before applying, and disappears on revocation', async () => {
  const caseId = 'a'.repeat(64), caseRef = `https://tangent.example::home::lounge::case_${caseId}`;
  const summary = { caseRef, subjectPostRef: 'https://tangent.example::home::lounge::m-other-1',
    state: 'open', revision: 2, subjectRevision: 'subject-revision-2', subjectAvailable: true,
    testimonyCount: 1, firstReportedAt: new Date().toISOString(),
    allowedActions: ['read_moderation_case', 'preview_moderation_action', 'apply_moderation_action'] };
  const view = { case: summary, testimonies: [{ reasonCode: 'conduct', statement: 'One attributed concern.',
    submittedAt: new Date().toISOString() }], testimonyOffset: 0, nextTestimonyOffset: null, decisions: [] };
  let stewardAllowed = true;
  const f = fixture(request => {
    if (request.path.endsWith('?limit=1')) return topicEnvelope({ steward: stewardAllowed });
    if (request.path.endsWith('/previews')) return envelope({ case: summary, action: 'defer', effect: 'Return this case tomorrow.' });
    if (request.path.endsWith('/actions')) return envelope({ ...view, case: { ...summary, state: 'deferred', revision: 3 } });
    if (request.path.includes('/moderation/cases/' + caseId)) return envelope(view);
    return envelope({ cases: [summary], page: 1, nextPage: null, saturated: false });
  });
  const room = { key: 'lounge', tangentKey: 'home', canManage: false,
    permissions: { allowedActions: ['read'] } };
  f.window.TangentModeration.topic(room, { participantRef: 'steward' }); await settle();
  assert.equal(f.document.body.dataset.stewardship, 'true');
  assert.equal(f.get('stewardship-panel').hidden, false);
  assert.equal(f.get('moderation-cases').children.length, 1);

  f.get('moderation-cases').children[0].children[0].click(); await settle();
  assert.equal(f.get('moderation-detail').hidden, false);
  const form = f.get('moderation-decision');
  form.elements.namedItem('action').value = 'defer';
  form.elements.namedItem('summary').value = 'Wait for another account.';
  form.elements.namedItem('deferredUntil').value = '2026-09-14T12:00';
  form.emit('submit'); await settle();
  assert.equal(f.get('moderation-preview-card').hidden, false);
  assert.equal(f.requests.filter(item => item.path.endsWith('/actions')).length, 0);

  f.get('moderation-apply').click(); await settle();
  const applied = f.requests.find(item => item.path.endsWith('/actions'));
  assert.ok(applied);
  assert.equal(applied.body.expectedCaseRevision, 2);
  assert.match(applied.body.requestId, /^[0-9a-f-]{36}$/);
  assert.match(f.get('action-status').textContent, /deferred/);

  stewardAllowed = false;
  f.window.TangentModeration.topic({ ...room, policyRevision: 2 },
    { participantRef: 'steward' }); await settle();
  assert.equal(f.document.body.dataset.stewardship, 'false');
  assert.equal(f.get('stewardship-panel').hidden, true);
});
