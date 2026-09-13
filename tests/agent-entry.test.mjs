import test from 'node:test';
import assert from 'node:assert/strict';
import { discoverLocalConnector, qualifyAgentEntry } from '../src/server/web/wwwroot/agent-entry.js';

const body = { product: 'tangent-space-connector', discoveryVersion: 1, operatorOrigin: 'http://127.0.0.1:5219' };
const response = value => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const fallback = () => ({
  href: 'https://github.com/sylin-org/tangent-space#bring-an-agent',
  textContent: 'Tangent Space for Agents ↗',
  target: '_blank',
  rel: 'noopener',
  dataset: {}
});

test('a validated connector on browser loopback reveals the local connect action', async () => {
  let call;
  const link = fallback();
  const found = await qualifyAgentEntry(link, { send: async (url, options) => { call = { url, options }; return response(body); } });
  assert.equal(found, true);
  assert.equal(call.url, 'http://127.0.0.1:5219/api/discovery');
  assert.equal(call.options.credentials, 'omit');
  assert.equal(call.options.redirect, 'error');
  assert.equal(call.options.mode, 'cors');
  assert.equal(link.href, 'http://127.0.0.1:5219/');
  assert.equal(link.textContent, 'Connect an Agent');
  assert.equal(link.dataset.connector, 'ready');
});

test('unavailable and incompatible listeners preserve the useful project fallback', async () => {
  for (const send of [
    async () => { throw new TypeError('connection refused'); },
    async () => response({ ...body, product: 'unrelated-local-service' }),
    async () => response({ ...body, operatorOrigin: 'http://127.0.0.1:9999' })
  ]) {
    const link = fallback();
    assert.equal(await qualifyAgentEntry(link, { send }), false);
    assert.equal(link.href, 'https://github.com/sylin-org/tangent-space#bring-an-agent');
    assert.equal(link.textContent, 'Tangent Space for Agents ↗');
    assert.deepEqual(link.dataset, {});
  }
});

test('discovery rejects redirected, non-JSON and oversized responses', async () => {
  assert.equal(await discoverLocalConnector({ send: async () => ({ ok: true, redirected: true, headers: new Headers({ 'Content-Type': 'application/json' }) }) }), null);
  assert.equal(await discoverLocalConnector({ send: async () => new Response('hello', { headers: { 'Content-Type': 'text/plain' } }) }), null);
  assert.equal(await discoverLocalConnector({ send: async () => new Response('x', { headers: { 'Content-Type': 'application/json', 'Content-Length': '4097' } }) }), null);
});
