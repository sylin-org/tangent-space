// A framework-independent retained-page model. DOM measurement, transport, drafts and
// authoritative read state belong to their own owners; see SPA_WINDOW_CONTRACT.md.
export const DEFAULT_WINDOW_BUDGETS = Object.freeze({
  maxTopics: 3,
  maxPages: 16,
  maxRecords: 1000,
  maxBytes: 8 * 1024 * 1024,
  maxTotalRecords: 3000,
  maxTotalBytes: 16 * 1024 * 1024,
  maxPageRecords: 200,
  maxPageBytes: 2 * 1024 * 1024,
  maxRecordBytes: 64 * 1024,
  maxCursorBytes: 4096,
});

const encoder = new TextEncoder();
const directions = ['replace', 'before', 'after'];
const emptyRequests = () => ({ replace: null, before: null, after: null });
const ordered = (a, b) => a.sequence - b.sequence;
const result = (accepted, reason) => Object.freeze({ accepted, reason });
const validNumber = value => Number.isSafeInteger(value) && value >= 0;
const validKey = value => typeof value === 'string' && value.length > 0 && value.length <= 256;

function charge(remaining, bytes) {
  remaining.bytes -= bytes;
  if (remaining.bytes < 0) throw new Error('response_budget');
}
function chargeString(value, remaining) {
  // Count JSON UTF-8 bytes before cloning/stringifying. A huge already-decoded
  // string is rejected by length without allocating another huge string/buffer.
  if (value.length + 2 > remaining.bytes) throw new Error('response_budget');
  charge(remaining, 2);
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code === 34 || code === 92 || [8, 9, 10, 12, 13].includes(code)) charge(remaining, 2);
    else if (code < 32) charge(remaining, 6);
    else if (code < 128) charge(remaining, 1);
    else if (code < 2048) charge(remaining, 2);
    else if (code >= 0xd800 && code <= 0xdbff && value.charCodeAt(index + 1) >= 0xdc00 && value.charCodeAt(index + 1) <= 0xdfff) { charge(remaining, 4); index++; }
    else charge(remaining, code >= 0xd800 && code <= 0xdfff ? 6 : 3);
  }
}
function canonicalCopy(value, remaining, depth = 0) {
  if (--remaining.nodes < 0 || depth > 32) throw new Error('invalid_record');
  if (value === null || typeof value === 'boolean') { charge(remaining, value === false ? 5 : 4); return value; }
  if (typeof value === 'string') { chargeString(value, remaining); return value; }
  if (typeof value === 'number' && Number.isFinite(value)) { charge(remaining, JSON.stringify(value).length); return value; }
  if (!value || typeof value !== 'object') throw new Error('invalid_record');
  if (Array.isArray(value)) {
    if (value.length > remaining.nodes) throw new Error('invalid_record');
    charge(remaining, 2 + Math.max(0, value.length - 1));
    return Object.freeze(Array.from({ length: value.length }, (_, index) => {
      const descriptor = Object.getOwnPropertyDescriptor(value, index);
      if (!descriptor || !Object.hasOwn(descriptor, 'value')) throw new Error('invalid_record');
      return canonicalCopy(descriptor.value, remaining, depth + 1);
    }));
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== Object.prototype && prototype !== null) throw new Error('invalid_record');
  const keys = Object.keys(value).sort();
  if (keys.length > remaining.nodes) throw new Error('invalid_record');
  charge(remaining, 2 + Math.max(0, keys.length - 1) + keys.length);
  const entries = keys.map(key => {
    chargeString(key, remaining);
    const descriptor = Object.getOwnPropertyDescriptor(value, key);
    if (!descriptor || !Object.hasOwn(descriptor, 'value')) throw new Error('invalid_record');
    return [key, canonicalCopy(descriptor.value, remaining, depth + 1)];
  });
  return Object.freeze(Object.fromEntries(entries));
}

/** Decoded JSON only. Byte accounting is canonical UTF-8 JSON, not JS heap size. */
export function createWindowStore(overrides = {}) {
  for (const key of Object.keys(overrides)) {
    if (!Object.hasOwn(DEFAULT_WINDOW_BUDGETS, key)) throw new TypeError('Unknown budget: ' + key);
  }
  const budgets = Object.freeze({ ...DEFAULT_WINDOW_BUDGETS, ...overrides });
  for (const value of Object.values(budgets)) {
    if (!Number.isSafeInteger(value) || value < 1) throw new RangeError('Budgets must be positive safe integers.');
  }
  const topics = new Map(); // In insertion/LRU order, including only retained windows.
  let session = null, sessionGeneration = 0, viewGeneration = 0, active = null;

  const validCursor = value => typeof value === 'string' && !!value && value.length <= budgets.maxCursorBytes && encoder.encode(value).length <= budgets.maxCursorBytes;
  function current(context) { return !!active && active.context === context && !!session; }
  function counts(scope) {
    return { records: scope.records.size, bytes: [...scope.records.values()].reduce((sum, entry) => sum + entry.bytes, 0) };
  }
  function stats() {
    let records = 0, bytes = 0, pages = 0, pendingRequests = 0;
    for (const scope of topics.values()) {
      const count = counts(scope); records += count.records; bytes += count.bytes;
      pages += scope.pages.length;
      pendingRequests += Object.values(scope.requests).filter(Boolean).length;
    }
    return Object.freeze({ topics: topics.size, records, bytes, pages, pendingRequests });
  }
  function enforceAggregate() {
    for (;;) {
      const count = stats();
      if (count.topics <= budgets.maxTopics && count.records <= budgets.maxTotalRecords && count.bytes <= budgets.maxTotalBytes) return;
      const oldest = [...topics.values()].find(scope => scope !== active);
      if (!oldest) throw new Error('Internal aggregate budget invariant failed.');
      topics.delete(oldest.topic);
    }
  }
  function clearView(scope) {
    if (!scope) return;
    scope.context = null; scope.requests = emptyRequests();
    scope.readCheckpoint = scope.liveCheckpoint = null;
  }
  function beginSession({ origin, participant }) {
    if (typeof origin !== 'string' || origin.length > 2048) throw new TypeError('A valid origin is required.');
    const address = new URL(origin);
    if (!['http:', 'https:'].includes(address.protocol)) throw new TypeError('An HTTP origin is required.');
    if (participant !== null && (typeof participant !== 'string' || !participant || participant.length > 2048)) throw new TypeError('A participant reference or null is required.');
    clearView(active); topics.clear(); active = null;
    sessionGeneration++; viewGeneration++;
    session = Object.freeze({ origin: address.origin, participant });
  }
  function openTopic(topic) {
    if (!session) throw new Error('Begin a session before opening a Topic.');
    if (!validKey(topic)) throw new TypeError('A bounded Topic key is required.');
    clearView(active);
    let scope = topics.get(topic);
    if (!scope) scope = { topic, pages: [], records: new Map(), anchor: null, anchorUnavailable: null, generation: 0 };
    topics.delete(topic); topics.set(topic, scope); active = scope;
    scope.requests = emptyRequests(); scope.readCheckpoint = scope.liveCheckpoint = null;
    scope.context = Object.freeze({ sessionGeneration, viewGeneration: ++viewGeneration, topic });
    enforceAggregate();
    return scope.context;
  }
  function leaveTopic(context) {
    if (!current(context)) return false;
    clearView(active); active = null; viewGeneration++;
    return true;
  }
  function edge(scope, direction) { return direction === 'before' ? scope.pages[0] : scope.pages.at(-1); }
  function beginRequest(context, { direction, anchorId = null }) {
    if (!current(context)) return null;
    if (!directions.includes(direction)) throw new TypeError('Unknown request direction.');
    if (anchorId !== null && (!validKey(anchorId) || direction !== 'replace')) throw new TypeError('A requested anchor belongs to a replacement window.');
    if (direction === 'replace') { active.generation++; active.requests = emptyRequests(); }
    else if (active.requests.replace || !edge(active, direction)?.[direction]) return null;
    const boundary = direction === 'replace' ? null : edge(active, direction);
    const ticket = Object.freeze({ context, direction, generation: active.generation,
      cursor: boundary?.[direction] || null, anchorId });
    // Exactly one outstanding ticket per lane. Boundary identity is private and bounded.
    active.requests[direction] = { ticket, boundary };
    return ticket;
  }
  function normalizePage(page) {
    if (!page || !Array.isArray(page.records)) throw new Error('invalid_page');
    if (page.records.length > budgets.maxPageRecords) throw new Error('response_budget');
    for (const side of ['before', 'after']) {
      if (page[side] !== null && !validCursor(page[side])) throw new Error('invalid_cursor');
    }
    let bytes = 0;
    const records = new Map(), sequences = new Map();
    for (const value of page.records) {
      const limit = Math.min(budgets.maxRecordBytes, budgets.maxPageBytes - bytes), remaining = { nodes: 16384, bytes: limit };
      const record = canonicalCopy(value, remaining);
      if (!record || !validKey(record.id) || !validNumber(record.sequence) || !validNumber(record.revision)) throw new Error('invalid_record');
      const serialized = JSON.stringify(record), size = limit - remaining.bytes;
      bytes += size;
      if (size > budgets.maxRecordBytes || bytes > budgets.maxPageBytes) throw new Error('response_budget');
      const previous = records.get(record.id), owner = sequences.get(record.sequence);
      if (owner && owner !== record.id || previous && previous.record.sequence !== record.sequence) throw new Error('identity_conflict');
      if (previous?.record.revision === record.revision && JSON.stringify(previous.record) !== serialized) throw new Error('revision_conflict');
      if (!previous || previous.record.revision < record.revision) records.set(record.id, { record, bytes: size });
      sequences.set(record.sequence, record.id);
    }
    return { records, before: page.before, after: page.after };
  }
  function materialize(pages, registry) {
    const retained = new Map();
    for (const page of pages) for (const id of page.ids) retained.set(id, registry.get(id));
    return new Map([...retained].sort(([, a], [, b]) => ordered(a.record, b.record)));
  }
  function anchorFor(scope, ticket, records) {
    const requested = ticket.anchorId || scope.anchor?.id;
    const selected = requested && records.get(requested);
    if (selected) return { anchor: Object.freeze({ id: requested, sequence: selected.record.sequence,
      offset: scope.anchor?.id === requested ? scope.anchor.offset : 0 }), unavailable: null };
    const sequence = requested ? scope.records.get(requested)?.record.sequence ?? (scope.anchor?.id === requested ? scope.anchor.sequence : undefined) : undefined;
    const nearest = [...records.values()].sort((a, b) => sequence === undefined ? ordered(a.record, b.record)
      : Math.abs(a.record.sequence - sequence) - Math.abs(b.record.sequence - sequence) || b.record.sequence - a.record.sequence)[0];
    return { anchor: nearest ? Object.freeze({ id: nearest.record.id, sequence: nearest.record.sequence, offset: scope.anchor?.offset || 0 }) : null,
      unavailable: requested || null };
  }
  function trim(pages, registry, anchor) {
    let records = materialize(pages, registry);
    const withinBudget = () => pages.length <= budgets.maxPages && records.size <= Math.min(budgets.maxRecords, budgets.maxTotalRecords)
      && [...records.values()].reduce((sum, entry) => sum + entry.bytes, 0) <= Math.min(budgets.maxBytes, budgets.maxTotalBytes);
    while (!withinBudget()) {
      if (pages.length < 2) throw new Error('window_budget');
      const candidates = [0, pages.length - 1].map(index => {
        const remaining = pages.filter((_, pageIndex) => pageIndex !== index), retained = materialize(remaining, registry);
        const distance = Math.max(0, ...pages[index].ids.map(id => Math.abs(registry.get(id).record.sequence - (anchor?.sequence || 0))));
        return { index, remaining, retained, distance };
      }).filter(candidate => !anchor || candidate.retained.has(anchor.id)).sort((a, b) => b.distance - a.distance || b.index - a.index);
      if (!candidates.length) throw new Error('window_budget');
      pages = candidates[0].remaining; records = candidates[0].retained;
    }
    return { pages, records };
  }
  function applyPage(ticket, page) {
    if (!ticket || !current(ticket.context) || ticket.generation !== active.generation) return result(false, 'stale_context');
    const pending = active.requests[ticket.direction];
    if (pending?.ticket !== ticket) return result(false, 'superseded_request');
    active.requests[ticket.direction] = null;
    if (ticket.direction !== 'replace' && edge(active, ticket.direction) !== pending.boundary) return result(false, 'edge_moved');
    try {
      const incoming = normalizePage(page), registry = new Map(active.records);
      const sequences = new Map([...registry.values()].map(entry => [entry.record.sequence, entry.record.id]));
      const existing = [...active.records.values()];
      const low = existing[0]?.record.sequence, high = existing.at(-1)?.record.sequence;
      let additions = 0;
      for (const [id, entry] of incoming.records) {
        const old = registry.get(id), owner = sequences.get(entry.record.sequence);
        if (owner && owner !== id || old && old.record.sequence !== entry.record.sequence) throw new Error('identity_conflict');
        if (old?.record.revision === entry.record.revision && JSON.stringify(old.record) !== JSON.stringify(entry.record)) throw new Error('revision_conflict');
        if (!old) {
          if (ticket.direction === 'before' && low !== undefined && entry.record.sequence >= low
            || ticket.direction === 'after' && high !== undefined && entry.record.sequence <= high) throw new Error('non_adjacent_page');
          additions++;
        }
        if (!old || old.record.revision < entry.record.revision) registry.set(id, entry);
        sequences.set(entry.record.sequence, id);
      }
      const nextPage = { ids: [...incoming.records.values()].map(entry => entry.record).sort(ordered).map(record => record.id), before: incoming.before, after: incoming.after };
      let pages;
      if (ticket.direction === 'replace') pages = [nextPage];
      else if (!additions) {
        // Empty/overlapping permission-filtered pages can advance an edge without
        // retaining an unbounded chain of empty pages or losing already loaded rows.
        pages = [...active.pages];
        const index = ticket.direction === 'before' ? 0 : pages.length - 1;
        if (incoming[ticket.direction] === ticket.cursor) throw new Error('cursor_stalled');
        pages[index] = { ...pages[index], [ticket.direction]: incoming[ticket.direction] };
      } else pages = ticket.direction === 'before' ? [nextPage, ...active.pages] : [...active.pages, nextPage];
      const selected = anchorFor(active, ticket, materialize(pages, registry));
      const retained = trim(pages, registry, selected.anchor);
      // Reject a fetch whose useful rows are all immediately evicted. Its edge must
      // stay unchanged, so the caller can move the anchor or request a smaller page.
      if (additions && ticket.direction !== 'replace' && ![...incoming.records.keys()].some(id => !active.records.has(id) && retained.records.has(id))) throw new Error('anchor_budget');
      active.pages = retained.pages; active.records = retained.records;
      active.anchor = selected.anchor; active.anchorUnavailable = selected.unavailable;
      enforceAggregate();
      return result(true, 'applied');
    } catch (error) {
      return result(false, ['invalid_page', 'invalid_record', 'invalid_cursor', 'response_budget', 'window_budget', 'anchor_budget', 'identity_conflict', 'revision_conflict', 'non_adjacent_page', 'cursor_stalled'].includes(error.message) ? error.message : 'invalid_page');
    }
  }
  function setAnchor(context, { id, offset = 0 }) {
    if (!current(context) || !active.records.has(id) || !Number.isFinite(offset) || Math.abs(offset) > 1000000) return false;
    active.anchor = Object.freeze({ id, sequence: active.records.get(id).record.sequence, offset });
    active.anchorUnavailable = null;
    return true;
  }
  function checkpoint(context, value, field) {
    if (!current(context)) return false;
    if (!value || !validNumber(value.sequence) || !validCursor(value.cursor)) return false;
    if (active[field] && value.sequence < active[field].sequence) return false;
    active[field] = Object.freeze({ sequence: value.sequence, cursor: value.cursor });
    return true;
  }
  function snapshot(context) {
    if (!current(context)) return null;
    const count = counts(active);
    return Object.freeze({ topic: active.topic, records: Object.freeze([...active.records.values()].map(entry => entry.record)),
      pages: Object.freeze(active.pages.map(page => Object.freeze({ ids: Object.freeze([...page.ids]), before: page.before, after: page.after }))),
      before: active.pages[0]?.before || null, after: active.pages.at(-1)?.after || null,
      anchor: active.anchor, anchorUnavailable: active.anchorUnavailable, readCheckpoint: active.readCheckpoint,
      liveCheckpoint: active.liveCheckpoint, recordCount: count.records, serializedBytes: count.bytes });
  }
  return Object.freeze({ budgets, beginSession, openTopic, leaveTopic, beginRequest, applyPage, setAnchor,
    setLiveCheckpoint: (context, value) => checkpoint(context, value, 'liveCheckpoint'),
    acknowledgeRead: (context, value) => checkpoint(context, value, 'readCheckpoint'), snapshot, stats });
}
