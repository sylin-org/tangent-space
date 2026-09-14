import test from 'node:test';
import assert from 'node:assert/strict';
import { createWorkspaceRouter, parseWorkspaceRoute, workspaceDestination } from '../src/server/web/wwwroot/spa-router.js';

test('core workspace routes have one stable client route contract', () => {
  assert.deepEqual(parseWorkspaceRoute('/'), { kind: 'home' });
  assert.deepEqual(parseWorkspaceRoute('/tangents/'), { kind: 'tangents' });
  assert.deepEqual(parseWorkspaceRoute('/t/home/topics'), { kind: 'topics', tangent: 'home' });
  assert.deepEqual(parseWorkspaceRoute('/t/home/topics/long%20talk'), { kind: 'topic', tangent: 'home', topic: 'long talk' });
  assert.deepEqual(parseWorkspaceRoute('/t/home/post-7'), { kind: 'post', tangent: 'home', post: 'post-7' });
  assert.deepEqual(parseWorkspaceRoute('/u/did%3Aplc%3Ax'), { kind: 'participant', identifier: 'did:plc:x' });
  assert.deepEqual(parseWorkspaceRoute('/settings'), { kind: 'settings' });
  assert.deepEqual(parseWorkspaceRoute('/t/home/settings'), { kind: 'tangent-settings', tangent: 'home' });
  assert.deepEqual(parseWorkspaceRoute('/t/home/topics/lounge/settings'), { kind: 'topic-settings', tangent: 'home', topic: 'lounge' });
});

test('router refuses auth, administration, malformed and cross-origin destinations', () => {
  const origin = 'https://tangent.example';
  for (const href of ['/sign-in/', '/onboarding/', '/api/site', '/t/home/topics/extra/path/nope', '/t/home/topics/a%2Fb', '/u/a%5Cb', '/u/%00x', 'https://other.example/t/home/topics']) {
    assert.equal(workspaceDestination(href, origin), null, href);
  }
});

test('query and fragment state ride only recognized same-origin routes', () => {
  const destination = workspaceDestination('/t/home/topics/lounge?from=post-1#reply', 'https://tangent.example');
  assert.equal(destination.href, 'https://tangent.example/t/home/topics/lounge?from=post-1#reply');
});

test('navigation mutates the shared route, preserves history scroll and restores it only when ready', () => {
  class Bus {
    constructor() { this.listeners = new Map(); }
    addEventListener(name, listener) { this.listeners.set(name, [...(this.listeners.get(name) || []), listener]); }
    dispatchEvent(event) { for (const listener of this.listeners.get(event.type) || []) listener(event); }
  }
  class CustomEvent { constructor(type, init) { this.type = type; this.detail = init?.detail; } }
  const window = new Bus(), document = new Bus(), route = { kind: 'home' };
  let url = new URL('https://tangent.example/'), restored;
  Object.defineProperty(window, 'location', { value: {
    get href() { return url.href; }, get origin() { return url.origin; }, get pathname() { return url.pathname; },
    get search() { return url.search; }, get hash() { return url.hash; }
  } });
  Object.assign(window, {
    TangentPages: { route }, CustomEvent, scrollX: 4, scrollY: 90,
    requestAnimationFrame: callback => callback(), scrollTo: (x, y) => { restored = [x, y]; }
  });
  window.history = {
    state: null,
    replaceState(state, _title, href) { this.state = state; if (href) url = new URL(href, url); },
    pushState(state, _title, href) { this.state = state; url = new URL(href, url); }
  };
  document.body = { dataset: {} };
  const router = createWorkspaceRouter({ window, document });
  assert.equal(router.canonicalize('/u/someone'), false, 'cannot canonicalize into a different route kind');
  assert.equal(router.navigate('/t/home/topics/lounge'), true);
  assert.deepEqual(route, { kind: 'topic', tangent: 'home', topic: 'lounge' });
  assert.deepEqual(window.history.state.tangentScroll, { position: [0, 0] });
  assert.equal(restored, undefined, 'async route work owns readiness');
  window.dispatchEvent(new CustomEvent('tangent:route-ready', { detail: { href: '/t/home/topics/lounge' } }));
  assert.deepEqual(restored, [0, 0]);

  assert.equal(router.navigate('/u/did%3Aplc%3Ax'), true);
  assert.equal(router.canonicalize('/u/alice.example'), true);
  assert.equal(url.pathname, '/u/alice.example');
  assert.deepEqual(route, { kind: 'participant', identifier: 'alice.example' });

  window.scrollX = 0; window.scrollY = 640;
  window.history.replaceState({ tangentScroll: { position: [4, 90] } }, '', '/');
  window.dispatchEvent({ type: 'popstate', state: { tangentScroll: { position: [4, 90] } } });
  window.dispatchEvent(new CustomEvent('tangent:route-ready', { detail: { href: '/' } }));
  assert.deepEqual(route, { kind: 'home' });
  assert.deepEqual(restored, [4, 90]);
});
