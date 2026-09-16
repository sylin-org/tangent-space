/* Main-thread adapter for the origin-wide activity SharedWorker. Falls back to the
 * single-tab transport where SharedWorker is unavailable or cannot start. */
'use strict';
(() => {
  const scope = window;
  let current, pendingProfiles = [];
  function create(options = {}) {
    let participant = '', desired = false, visible = !document.hidden, generation = 0, delivery, direct;
    let worker, port, ready = false, profiles = [], profileKey = '', heartbeat, startup, sharedState = {};
    const emitProfile = profile => scope.dispatchEvent(new CustomEvent('tangent:profile-stream', { detail: profile }));
    const context = signal => { const own = generation; return { participant, generation: own, signal,
      isCurrent: () => desired && visible && own === generation && !signal.aborted }; };
    function startDirect() {
      if (direct || !scope.TangentActivity) return;
      ready = false; sharedState = {};
      direct = scope.TangentActivity.create({ ...options, onEvent(name, value) { if (name === 'profile') emitProfile(value); options.onEvent?.(name, value); } });
      if (desired && participant) { direct.setVisible(visible); direct.start({ participant }); }
      scope.dispatchEvent(new CustomEvent('tangent:live-ready'));
    }
    function send(value) { if (ready && port) port.postMessage(value); }
    function abortDelivery() { delivery?.controller.abort(); delivery = undefined; }
    async function receive(message) {
      if (!message || typeof message !== 'object') return;
      if (message.type === 'ready') {
        ready = true; clearTimeout(startup);
        if (desired && participant) send({ type: 'start', participant, visible });
        if (profiles.length) send({ type: 'profiles', dids: profiles });
      } else if (message.participant && message.participant !== participant) return;
      else if (message.type === 'snapshot' && desired && visible) {
        abortDelivery(); const controller = new AbortController(); delivery = { id: message.id, controller };
        let accepted = false;
        try { accepted = await options.onSnapshot?.(message.snapshot, context(controller.signal)) !== false; }
        catch (_) { accepted = false; }
        if (delivery?.id === message.id) { delivery = undefined; send({ type: 'ack', id: message.id, accepted }); }
      } else if (message.type === 'profile') emitProfile(message.profile);
      else if (message.type === 'cancel' && delivery?.id === message.id) { delivery.controller.abort(); delivery = undefined; }
      else if (message.type === 'identity') options.onIdentity?.(message.detail || { reason: 'identity_changed' });
      else if (message.type === 'state') { sharedState = { ...message.state, shared: true }; options.onState?.(sharedState); }
      else if (message.type === 'error') { ready = false; try { port?.close(); } catch (_) {} startDirect(); }
    }
    if (typeof SharedWorker === 'function') {
      try {
        worker = new SharedWorker('/activity-shared-worker.js?v=20260912-shared2', 'tangent-activity-shared2');
        port = worker.port; port.onmessage = event => receive(event.data); port.start();
        heartbeat = setInterval(() => send({ type: 'heartbeat' }), 10000);
        startup = setTimeout(() => { if (!ready) { try { port.close(); } catch (_) {} startDirect(); } }, 2000);
      } catch (_) { startDirect(); }
    } else startDirect();
    return Object.freeze({
      start({ participant: next } = {}) {
        if (typeof next !== 'string' || !next || next.length > 4096) throw new TypeError('A participant reference is required.');
        if (next !== participant) { participant = next; generation++; abortDelivery(); }
        desired = true;
        if (direct) { direct.setVisible(visible); direct.start({ participant }); }
        else send({ type: 'start', participant, visible });
      },
      stop() { desired = false; generation++; abortDelivery(); if (direct) direct.stop(); else send({ type: 'stop' }); },
      setVisible(value) {
        const next = Boolean(value); if (next === visible) return;
        visible = next; generation++; if (!visible) abortDelivery();
        if (direct) direct.setVisible(visible); else send({ type: 'visibility', visible });
      },
      setProfiles(dids) {
        const next = Array.isArray(dids) ? [...new Set(dids.filter(value => typeof value === 'string' && value.length <= 256))].slice(0, 64) : [];
        if (direct) return false;
        const nextKey = next.join('|');
        if (nextKey !== profileKey) { profiles = next; profileKey = nextKey; send({ type: 'profiles', dids: profiles }); }
        return true;
      },
      snapshot() { return direct?.snapshot() || { participant, generation, visible, desired, shared: true, ready, ...sharedState }; }
    });
  }
  scope.TangentActivityCoordinator = Object.freeze({
    create(options) {
      current = create(options);
      if (pendingProfiles.length) current.setProfiles(pendingProfiles);
      scope.dispatchEvent(new CustomEvent('tangent:live-ready'));
      return current;
    },
    setProfiles(dids) {
      pendingProfiles = Array.isArray(dids) ? dids : [];
      if (current) return current.setProfiles(pendingProfiles) === true;
      // Keep the selection for the worker, but do not claim delivery the coordinator
      // cannot make: with no session there is nothing to multiplex onto. Onboarding
      // never creates one, because the welcome that would is not dispatched on that
      // route, so claiming here left first sign-in waiting on an announcement that
      // could not arrive. The caller opens its own stream and hands over on
      // tangent:live-ready, which costs one transient SSE and always delivers.
      return false;
    },
    snapshot() { return current?.snapshot(); }
  });
})();
