/* Participant activity runtime. Classic defer script: no DOM, room state or automatic writes.
 * create({ fetch, onSnapshot, onIdentity?, onState?, timers?, now?, random?, supportsStreaming?, timings? })
 * returns start({participant}), stop(), setVisible(bool), snapshot(). onSnapshot(value, context) must
 * await all acceptance-critical work; false/rejection/timeout leaves the checkpoint replayable. Its
 * context.signal aborts with the request, and context.isCurrent() fences late UI work. Checkpoint is
 * an opaque delivery cursor, never a read acknowledgement or a topic-directory cursor.
 * stop/hidden retain only the same participant's checkpoint; participant/authority changes clear it.
 * Server /wait holds idle requests ~15s. Watchdog/poll limits are 45s; SSE rotates after 60s through a
 * fresh actor-stamped snapshot. A poll/live state means degraded but functioning polling, not SSE.
 */
(() => {
  'use strict';
  const scope = typeof window !== 'undefined' ? window : globalThis;
  const MAX_JSON = 512 * 1024, MAX_FRAME = 512 * 1024, MAX_CURSOR = 4096;
  const defaults = { connectMs: 10000, watchdogMs: 45000, pollTimeoutMs: 45000, callbackMs: 15000,
    reprobeMs: 60000, streamLifetimeMs: 60000, retryMinMs: 1000, retryMaxMs: 30000, idlePollMs: 250 };
  const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
  const string = (value, max = 4096) => typeof value === 'string' && value.length <= max;
  const cursor = value => string(value, MAX_CURSOR) && value.length > 0 && !/[\u0000-\u0020]/.test(value);
  const nullableString = value => value == null || string(value);
  const integer = value => Number.isSafeInteger(value) && value >= 0;
  function validate(value) {
    if (!object(value) || !string(value.participantRef) || !value.participantRef || !cursor(value.checkpoint) || !Array.isArray(value.events) || value.events.length > 25
      || !Array.isArray(value.topics) || value.topics.length > 100 || typeof value.hasMore !== 'boolean'
      || typeof value.resetRequired !== 'boolean' || !(value.nextCursor == null || cursor(value.nextCursor))
      || (value.hasMore && value.nextCursor !== value.checkpoint)
      || (!value.hasMore && value.nextCursor != null)
      || !(value.nextTopicCursor == null || cursor(value.nextTopicCursor))) throw new Error('invalid-snapshot');
    for (const name of ['topicsTruncated', 'topicsHasMore', 'topicsIncomplete'])
      if (value[name] !== undefined && typeof value[name] !== 'boolean') throw new Error('invalid-snapshot');
    for (const event of value.events) {
      if (!object(event) || !string(event.sequence, 80) || !event.sequence || !string(event.kind, 128) || !event.kind
        || !string(event.topicKey, 1024) || !string(event.tangentKey, 1024) || !nullableString(event.actorParticipantId)
        || !nullableString(event.targetParticipantId) || !(event.postSequence == null || integer(event.postSequence))
        || !string(event.occurredAt, 80)) throw new Error('invalid-event');
    }
    const seen = new Set();
    for (const topic of value.topics) {
      if (!object(topic) || !string(topic.topicKey, 1024) || !topic.topicKey || seen.has(topic.topicKey)
        || !string(topic.tangentKey, 1024) || !integer(topic.unreadCount) || !integer(topic.directReplies)
        || !integer(topic.lastSequence) || !integer(topic.readSequence) || typeof topic.unreadCountCapped !== 'boolean'
        || !nullableString(topic.lastPostAt)) throw new Error('invalid-topic');
      seen.add(topic.topicKey);
    }
    return value;
  }
  function create(options = {}) {
    const fetcher = options.fetch || scope.fetch.bind(scope);
    if (typeof options.onSnapshot !== 'function') throw new TypeError('onSnapshot is required.');
    const timers = options.timers || scope;
    const setTimer = timers.setTimeout.bind(timers), clearTimer = timers.clearTimeout.bind(timers);
    const now = options.now || Date.now, random = options.random || Math.random;
    const timing = { ...defaults, ...options.timings };
    for (const [name, value] of Object.entries(timing))
      if (!(name in defaults) || !Number.isFinite(value) || value <= 0 || value > 300000) throw new TypeError('Invalid timing: ' + name);
    if (timing.retryMaxMs < timing.retryMinMs) throw new TypeError('Invalid retry bounds.');
    const encoder = new TextEncoder();
    let participant = null, checkpoint = null, generation = 0, desired = false, visible = true, running = null;
    let cursorRecovered = false;
    let state = { mode: 'stopped', status: 'stopped', participant, generation, checkpoint };
    const current = run => running === run && run.generation === generation && !run.controller.signal.aborted;
    const ignore = callback => { try { Promise.resolve(callback()).catch(() => {}); } catch (_) { /* UI callbacks do not own transport cleanup. */ } };
    function emit(run, next) {
      if (run && !current(run)) return;
      state = { ...next, participant, generation, checkpoint };
      if (options.onState) ignore(() => options.onState({ ...state }));
    }
    function halt(status, reason) {
      const old = running; running = null; generation++;
      if (old) old.controller.abort();
      emit(null, { mode: 'stopped', status, ...(reason ? { reason } : {}) });
    }
    function identity(run, detail) {
      if (!current(run)) return;
      desired = false; checkpoint = null;
      halt('identity', detail.reason || 'identity_changed');
      const stoppedGeneration = generation;
      // onState may synchronously start a replacement. Do not send an old identity notification into it.
      if (generation === stoppedGeneration && !running && options.onIdentity) ignore(() => options.onIdentity(detail));
    }
    function guarded(promise, signal, timeoutMs, reason = 'timeout') {
      return new Promise((resolve, reject) => {
        let timer, settled = false;
        const finish = (callback, value) => { if (settled) return; settled = true; clearTimer(timer); signal.removeEventListener('abort', abort); callback(value); };
        const abort = () => finish(reject, new Error('aborted'));
        if (signal.aborted) { Promise.resolve(promise).catch(() => {}); return abort(); }
        signal.addEventListener('abort', abort, { once: true });
        if (timeoutMs) timer = setTimer(() => finish(reject, new Error(reason)), timeoutMs);
        Promise.resolve(promise).then(value => finish(resolve, value), error => finish(reject, error));
      });
    }
    const sleep = (run, ms) => guarded(new Promise(resolve => {
      const id = setTimer(() => { run.controller.signal.removeEventListener('abort', abort); resolve(); }, ms);
      const abort = () => { clearTimer(id); resolve(); }; run.controller.signal.addEventListener('abort', abort, { once: true });
    }), run.controller.signal);
    function disposeReader(reader) {
      if (!reader) return;
      try {
        Promise.resolve(reader.cancel()).catch(() => {}).finally(() => { try { reader.releaseLock(); } catch (_) {} });
      } catch (_) { try { reader.releaseLock(); } catch (_) {} }
    }
    function disposeBody(response) {
      try { if (response?.body && !response.body.locked) Promise.resolve(response.body.cancel()).catch(() => {}); } catch (_) {}
    }
    async function accept(run, req, value) {
      if (!current(run)) throw new Error('aborted');
      if (object(value) && string(value.participantRef) && value.participantRef && value.participantRef !== run.participant) {
        identity(run, { reason: 'participant-mismatch', participantRef: value.participantRef }); throw new Error('aborted');
      }
      validate(value); const acceptedCheckpoint = value.checkpoint;
      if (value.hasMore && acceptedCheckpoint === checkpoint) throw new Error('non-progressing-cursor');
      let deliveryValid = true;
      // Consumers await their invalidation work. Request cancellation/timeout also cancels that work.
      const context = { participant: run.participant, generation: run.generation, signal: req.controller.signal,
        isCurrent: () => deliveryValid && current(run) && !req.controller.signal.aborted };
      try {
        const accepted = await guarded(Promise.resolve().then(() => {
          if (!context.isCurrent()) throw new Error('aborted');
          return options.onSnapshot(value, context);
        }), req.controller.signal, timing.callbackMs, 'snapshot-callback-timeout');
        if (accepted === false) throw new Error('snapshot-rejected');
        if (!context.isCurrent() || req.controller.signal.aborted) throw new Error('aborted');
        checkpoint = acceptedCheckpoint; run.failures = 0;
      } catch (error) { deliveryValid = false; throw error; }
    }
    async function json(run, req, response) {
      const length = response.headers?.get('content-length');
      if (length && (!/^\d+$/.test(length) || Number(length) > MAX_JSON)) throw new Error('json-too-large');
      let text = '';
      if (typeof response.body?.getReader === 'function') {
        req.reader = response.body.getReader(); const decoder = new TextDecoder('utf-8', { fatal: true }); let bytes = 0;
        while (current(run)) {
          const packet = await guarded(req.reader.read(), req.controller.signal);
          if (packet.done) { text += decoder.decode(); break; }
          bytes += packet.value.byteLength;
          if (bytes > MAX_JSON) throw new Error('json-too-large');
          text += decoder.decode(packet.value, { stream: true });
        }
      } else {
        // Older fetch implementations buffer text internally. Limit headers first, then bytes before JSON parsing.
        text = await guarded(response.text(), req.controller.signal);
        if (text.length > MAX_JSON || encoder.encode(text).byteLength > MAX_JSON) throw new Error('json-too-large');
      }
      if (!current(run)) throw new Error('aborted');
      const value = JSON.parse(text); await accept(run, req, value); return value;
    }
    async function stream(run, req, response, touch) {
      if (typeof response.body?.getReader !== 'function') { run.streamEnabled = false; throw new Error('stream-unavailable'); }
      if (!/^text\/event-stream(?:\s*;|$)/i.test(response.headers?.get('content-type') || '')) throw new Error('invalid-stream-type');
      req.reader = response.body.getReader(); const decoder = new TextDecoder('utf-8', { fatal: true });
      let line = '', frameBytes = 0, event = 'message', data = [], skipLF = false;
      async function completeLine() {
        if (!line) {
          const name = event, payload = data.join('\n'); event = 'message'; data = []; frameBytes = 0;
          if (name === 'identity_changed') {
            let detail = {}; try { const parsed = JSON.parse(payload); if (object(parsed)) detail = parsed; } catch (_) {}
            identity(run, { ...detail, reason: 'identity_changed' }); return;
          }
          if ((name === 'activity' || name === 'reset') && payload) {
            await accept(run, req, JSON.parse(payload)); emit(run, { mode: 'sse', status: 'live' });
          } else if (name === 'activity' || name === 'reset') throw new Error('empty-activity-frame');
          else if (name === 'profile' && payload && options.onEvent) {
            const value = JSON.parse(payload);
            if (!object(value)) throw new Error('invalid-profile-frame');
            ignore(() => options.onEvent(name, value));
          }
          return;
        }
        if (!line.startsWith(':')) {
          const colon = line.indexOf(':'), field = colon < 0 ? line : line.slice(0, colon);
          let value = colon < 0 ? '' : line.slice(colon + 1); if (value.startsWith(' ')) value = value.slice(1);
          if (field === 'event') event = value;
          else if (field === 'data') data.push(value);
          // id/retry are deliberately ignored: only an accepted payload can advance the opaque checkpoint.
        }
        line = '';
      }
      while (current(run)) {
        const packet = await guarded(req.reader.read(), req.controller.signal);
        if (packet.done) throw new Error('stream-ended');
        if (packet.value.byteLength > MAX_FRAME * 2) throw new Error('stream-chunk-too-large');
        if (packet.value.byteLength) touch();
        const chunk = decoder.decode(packet.value, { stream: true }); let from = 0;
        for (let index = 0; index < chunk.length; index++) {
          const char = chunk[index];
          if (skipLF) { skipLF = false; if (char === '\n') { from = index + 1; continue; } }
          if (char !== '\n' && char !== '\r') continue;
          const part = chunk.slice(from, index); line += part; frameBytes += encoder.encode(part).byteLength + 1;
          if (frameBytes > MAX_FRAME) throw new Error('frame-too-large');
          await completeLine();
          if (!current(run)) return;
          skipLF = char === '\r'; from = index + 1;
        }
        const part = chunk.slice(from); line += part; frameBytes += encoder.encode(part).byteLength;
        if (frameBytes > MAX_FRAME) throw new Error('frame-too-large');
      }
    }
    async function request(run, mode, deadline) {
      if (!current(run)) throw new Error('aborted');
      if (run.request) throw new Error('duplicate-activity-request');
      const req = { controller: new AbortController(), reader: null, response: null, timeoutReason: null, timer: null };
      run.request = req;
      const abort = () => req.controller.abort(); run.controller.signal.addEventListener('abort', abort, { once: true });
      const arm = (ms, reason) => { clearTimer(req.timer); req.timer = setTimer(() => {
        req.timeoutReason = reason; req.controller.abort();
      }, ms); };
      const suffix = mode === 'sse' ? '/events' : mode === 'poll' ? '/wait' : '';
      let path = '/api/activity' + suffix + (checkpoint ? '?cursor=' + encodeURIComponent(checkpoint) : '');
      if (mode === 'sse' && options.streamQuery) {
        const extra = String(options.streamQuery() || '');
        if (extra.length > 16384 || /[\u0000-\u001f#]/.test(extra)) throw new Error('invalid-stream-query');
        if (extra) path += (path.includes('?') ? '&' : '?') + extra;
      }
      const requestUntil = now() + timing.pollTimeoutMs;
      try {
        // /wait intentionally holds its response headers for up to 15 seconds on the server.
        const headerBudget = mode === 'poll' ? timing.pollTimeoutMs : timing.connectMs;
        arm(Math.min(headerBudget, deadline ? Math.max(1, deadline - now()) : Infinity), deadline && deadline - now() <= headerBudget ? 'reprobe' : 'connect-timeout');
        const response = await guarded(Promise.resolve().then(() => {
          if (req.controller.signal.aborted) throw new Error('aborted');
          return fetcher(path, { method: 'GET', credentials: 'same-origin', cache: 'no-store', signal: req.controller.signal,
            headers: { Accept: mode === 'sse' ? 'text/event-stream' : 'application/json' } });
        }).then(value => { if (req.controller.signal.aborted) { disposeBody(value); throw new Error('aborted'); } return value; }), req.controller.signal);
        req.response = response;
        if (!response.ok) { const error = new Error('http-' + response.status); error.status = response.status; throw error; }
        if (!current(run)) throw new Error('aborted');
        if (mode === 'sse') {
          const rotateAt = now() + timing.streamLifetimeMs;
          const touch = () => { const remaining = Math.max(1, rotateAt - now());
            arm(Math.min(timing.watchdogMs, remaining), remaining <= timing.watchdogMs ? 'stream-rotation' : 'stream-stalled'); }; touch();
          emit(run, { mode, status: 'connecting' }); await stream(run, req, response, touch);
        } else {
          const budget = deadline ? Math.max(1, deadline - now()) : Infinity;
          const remaining = mode === 'poll' ? Math.max(1, requestUntil - now()) : timing.pollTimeoutMs;
          arm(Math.min(remaining, budget), budget <= remaining ? 'reprobe' : 'body-timeout');
          const value = await json(run, req, response); emit(run, { mode, status: 'live' }); return value;
        }
      } catch (error) {
        if (req.timeoutReason) throw new Error(req.timeoutReason);
        throw error;
      } finally {
        clearTimer(req.timer); req.controller.abort(); run.controller.signal.removeEventListener('abort', abort);
        disposeReader(req.reader); if (!req.reader) disposeBody(req.response);
        if (current(run) && run.request === req) run.request = null;
      }
    }
    async function loop(run) {
      let mode = 'snapshot', reprobeAt = Infinity;
      while (current(run)) {
        if (mode === 'poll' && run.streamEnabled && now() >= reprobeAt) mode = 'sse';
        emit(run, { mode, status: mode === 'poll' ? state.mode === 'poll' && state.status === 'live' ? 'live' : 'reconnecting' : 'connecting' });
        try {
          const value = await request(run, mode, mode === 'poll' && run.streamEnabled ? reprobeAt : null);
          if (!current(run)) return;
          if (mode === 'snapshot') { mode = run.streamEnabled ? 'sse' : 'poll'; continue; }
          if (mode === 'poll') { await sleep(run, Math.min(value.hasMore ? 1 : timing.idlePollMs, Math.max(1, reprobeAt - now()))); continue; }
        } catch (error) {
          if (!current(run)) return;
          if ([401, 403].includes(error.status)) { identity(run, { reason: 'authorization', status: error.status }); return; }
          if (error.status === 400) {
            if (checkpoint && !cursorRecovered) { cursorRecovered = true; checkpoint = null; mode = 'snapshot'; continue; }
            desired = false; halt('error', 'invalid-cursor'); return;
          }
          if (error.message === 'reprobe' && run.streamEnabled) { mode = 'sse'; continue; }
          if (error.message === 'stream-rotation') { mode = 'snapshot'; continue; }
          if (mode !== 'poll') reprobeAt = run.streamEnabled ? now() + timing.reprobeMs : Infinity;
          mode = 'poll'; run.failures = Math.min(run.failures + 1, 16);
          const jitter = 0.75 + Math.max(0, Math.min(1, Number(random()) || 0)) * 0.5;
          const retryInMs = Math.min(timing.retryMaxMs, Math.max(1, timing.retryMinMs * 2 ** (run.failures - 1) * jitter));
          emit(run, { mode, status: 'reconnecting', reason: error.message, retryInMs });
          try { await sleep(run, Math.min(retryInMs, Math.max(1, reprobeAt - now()))); } catch (_) { return; }
        }
      }
    }
    function activate() {
      if (!desired || !visible || !participant || running) return;
      const run = { generation: ++generation, participant, controller: new AbortController(), request: null, failures: 0,
        streamEnabled: options.supportsStreaming ?? typeof ReadableStream !== 'undefined' };
      running = run;
      loop(run).catch(error => { if (current(run)) { desired = false; halt('error', error.message); } });
    }
    return Object.freeze({
      start({ participant: next } = {}) {
        if (!string(next, 4096) || !next) throw new TypeError('A participant reference is required.');
        if (next !== participant) { checkpoint = null; cursorRecovered = false; participant = next; halt('stopped'); }
        desired = true; activate();
      },
      stop() { desired = false; halt('stopped'); },
      reconnect() { if (!desired || !participant) return; halt('stopped'); desired = true; activate(); },
      setVisible(value) { const next = Boolean(value); if (next === visible) return; visible = next;
        if (!visible) halt('paused', 'hidden'); else activate(); },
      snapshot() { return { ...state, participant, generation, checkpoint, visible, desired, requestActive: Boolean(running?.request) }; }
    });
  }
  scope.TangentActivity = Object.freeze({ create });
})();
