/* One participant activity connection per browser origin. Tabs remain responsible for
 * rendering and explicitly acknowledge each delivery before the durable cursor advances. */
'use strict';
importScripts('/activity-transport.js?v=20260912-shared2');

const MAX_PORTS = 64, MAX_PROFILES = 64, PORT_TTL = 45000, HEARTBEAT = 10000;
const ports = new Map();
let transport, participant = '', lastSnapshot, lastState, deliverySequence = 0, activationSequence = 0;
let profileSelection = '', profileReconnect;

const validString = (value, max = 4096) => typeof value === 'string' && value.length > 0 && value.length <= max;
const post = (client, value) => { try { client.port.postMessage(value); } catch (_) { remove(client.id); } };
const broadcast = value => { for (const client of ports.values()) if (client.desired) post(client, value); };
const currentClients = () => [...ports.values()].filter(client => client.desired && client.visible && client.participant === participant);

function remove(id) {
  const client = ports.get(id); if (!client) return;
  ports.delete(id);
  for (const delivery of client.deliveries.values()) delivery(false);
  maintain();
}
function profileQuery() {
  const dids = [...new Set(currentClients().flatMap(client => client.profiles))].sort().slice(0, MAX_PROFILES);
  return dids.map(did => 'profileDid=' + encodeURIComponent(did)).join('&');
}
function refreshProfiles() {
  const next = profileQuery();
  if (next === profileSelection) return;
  profileSelection = next;
  clearTimeout(profileReconnect);
  profileReconnect = setTimeout(() => transport?.reconnect(), 100);
}
function deliver(client, snapshot, forcedReset = false) {
  if (!client.desired || !client.visible || client.participant !== participant) return { id: 0, promise: Promise.resolve(true) };
  const id = ++deliverySequence;
  const value = forcedReset && !snapshot.resetRequired ? { ...snapshot, resetRequired: true, events: [] } : snapshot;
  const promise = new Promise(resolve => {
    const finish = accepted => { if (!client.deliveries.delete(id)) return; resolve(accepted); };
    client.deliveries.set(id, finish);
    post(client, { type: 'snapshot', id, participant, snapshot: value });
  });
  return { id, promise };
}
async function accept(snapshot, context) {
  const clients = currentClients();
  if (!clients.length) return false;
  const deliveries = clients.map(client => ({ client, ...deliver(client, snapshot, client.needsReset) }));
  const abort = () => {
    for (const item of deliveries) {
      if (!item.id) continue;
      post(item.client, { type: 'cancel', id: item.id });
      item.client.deliveries.get(item.id)?.(false);
    }
  };
  context.signal.addEventListener('abort', abort, { once: true });
  try {
    const results = await Promise.all(deliveries.map(item => item.promise));
    if (!context.isCurrent() || results.some(value => value !== true)) return false;
    for (const client of clients) client.needsReset = false;
    lastSnapshot = snapshot;
    return true;
  } finally { context.signal.removeEventListener('abort', abort); }
}
function createTransport(nextParticipant) {
  transport?.stop(); participant = nextParticipant; lastSnapshot = undefined; profileSelection = profileQuery();
  transport = self.TangentActivity.create({
    fetch: (path, options) => fetch(path, options),
    streamQuery: () => profileSelection,
    onSnapshot: accept,
    onEvent(name, value) { if (name === 'profile') broadcast({ type: 'profile', participant, profile: value }); },
    onIdentity(detail) { broadcast({ type: 'identity', participant, detail }); },
    onState(state) { lastState = state; broadcast({ type: 'state', participant, state }); }
  });
  transport.start({ participant });
}
function maintain() {
  const clients = [...ports.values()].filter(client => client.desired && client.visible);
  if (!clients.length) { transport?.setVisible(false); refreshProfiles(); return; }
  const nextParticipant = clients.sort((a, b) => b.touched - a.touched)[0].participant;
  if (!transport || participant !== nextParticipant) createTransport(nextParticipant);
  transport.setVisible(true);
  for (const client of clients) {
    if (client.participant !== participant) post(client, { type: 'identity', participant: client.participant,
      detail: { reason: 'participant-generation-changed' } });
    else if (client.needsReset && lastSnapshot && client.deliveries.size === 0)
      deliver(client, lastSnapshot, true).promise.then(accepted => { if (accepted) client.needsReset = false; });
  }
  refreshProfiles();
}
function receive(client, message) {
  if (!message || typeof message !== 'object') return;
  client.lastSeen = Date.now();
  if (message.type === 'start' && validString(message.participant)) {
    client.touched = ++activationSequence;
    client.desired = true; client.visible = message.visible === true; client.participant = message.participant;
    client.needsReset = true; maintain();
    if (lastState && participant === client.participant) post(client, { type: 'state', participant, state: lastState });
  } else if (message.type === 'visibility') {
    if (message.visible === true) client.touched = ++activationSequence;
    client.visible = message.visible === true; if (client.visible) client.needsReset = true;
    if (!client.visible) for (const finish of client.deliveries.values()) finish(true);
    maintain();
  } else if (message.type === 'profiles' && Array.isArray(message.dids)) {
    client.profiles = [...new Set(message.dids.filter(did => validString(did, 256)))].slice(0, MAX_PROFILES);
    refreshProfiles();
  } else if (message.type === 'ack' && Number.isSafeInteger(message.id)) {
    client.deliveries.get(message.id)?.(message.accepted === true);
  } else if (message.type === 'stop') {
    client.desired = false; client.visible = false; client.profiles = [];
    for (const finish of client.deliveries.values()) finish(true); maintain();
  } else if (message.type === 'heartbeat') maintain();
}

self.onconnect = event => {
  const port = event.ports[0];
  if (ports.size >= MAX_PORTS) { port.postMessage({ type: 'error', reason: 'too-many-tabs' }); port.close(); return; }
  const id = crypto.randomUUID();
  const client = { id, port, participant: '', desired: false, visible: false, profiles: [], needsReset: true,
    touched: ++activationSequence, lastSeen: Date.now(), deliveries: new Map() };
  ports.set(id, client); port.onmessage = message => receive(client, message.data); port.start();
  port.postMessage({ type: 'ready', id });
};
setInterval(() => {
  const expired = Date.now() - PORT_TTL;
  for (const client of ports.values()) if (client.lastSeen < expired) remove(client.id);
}, HEARTBEAT);
