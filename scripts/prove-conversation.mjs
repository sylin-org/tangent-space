import http from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, spawnSync } from 'node:child_process';
import { createInterface } from 'node:readline';

// This script uses existing disposable-account cookies. It never prints cookies or provider credentials.
// Run: node scripts/prove-conversation.mjs --cookie-directory .local/... --external true
// A coordinated restart can be checked with the same --run plus --verify-restart true.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  if (!process.argv[i]?.startsWith('--') || !process.argv[i + 1]) throw new Error('Use --name value arguments.');
  args.set(process.argv[i].slice(2), process.argv[i + 1]);
}
const origin = args.get('origin') ?? 'http://127.0.0.1:5223';
if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(origin)) throw new Error('The proof is limited to an explicit loopback origin.');
const fixture = JSON.parse(await readFile(resolve(root, args.get('fixtures') ?? '.local/spaces-network/fixtures.json'), 'utf8'));
const accounts = Object.fromEntries(fixture.accounts.map(account => [account.role, account]));
const cookieDirectory = args.get('cookie-directory');
if (!cookieDirectory) throw new Error('Supply --cookie-directory for previously authorized disposable accounts.');
const cookies = {};
for (const role of ['owner', 'manager', 'agent', 'outsider']) {
  const saved = JSON.parse(await readFile(resolve(root, cookieDirectory, `${role}.cookies.json`), 'utf8'));
  if (saved.origin !== origin || saved.did !== accounts[role].did) throw new Error(`Cookie identity/origin mismatch for ${role}.`);
  cookies[role] = saved.cookies.map(([name, value]) => `${name}=${value}`).join('; ');
}
const run = args.get('run') ?? `conversation-${Date.now()}`;
if (!/^[a-z0-9-]{1,44}$/.test(run)) throw new Error('Use a short lowercase run identifier.');
const statePath = resolve(root, args.get('state') ?? `.local/tangent/${run}/state.json`);
if (!statePath.startsWith(resolve(root, '.local') + '\\') && !statePath.startsWith(resolve(root, '.local') + '/'))
  throw new Error('Restart checkpoints must be inside the ignored .local directory.');
const evidencePath = resolve(root, args.get('evidence') ?? 'docs/evidence/conversation.json');
const checks = [];
let state = { run, origin, rooms: { primary: `${run}-workshop`, secondary: `${run}-side` }, sourceRecords: [], cookieDirectory };
if (args.get('continue') === 'true') state = JSON.parse(await readFile(statePath, 'utf8'));
let stage = 'startup';
let completed = false;

async function request(role, method, path, body, options = {}) {
  stage = `${method} ${path.split('?')[0]}`;
  const bytes = body === undefined ? null : Buffer.from(JSON.stringify(body));
  const headers = { Accept: 'application/json', ...(role ? { Cookie: cookies[role] } : {}) };
  if (options.origin !== null) headers.Origin = options.origin ?? origin;
  if (options.authorization) headers.Authorization = options.authorization;
  if (bytes) { headers['Content-Type'] = 'application/json'; headers['Content-Length'] = bytes.length; }
  return new Promise((complete, reject) => {
    const req = http.request(new URL(path, origin), { method, headers }, response => {
      const chunks = [];
      let size = 0;
      response.on('data', chunk => {
        size += chunk.length;
        if (size > 256 * 1024) { req.destroy(new Error('API response exceeded proof bound.')); return; }
        chunks.push(chunk);
      });
      response.on('end', () => {
        let json = null;
        try { json = JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { /* Only status is reported for a non-JSON error. */ }
        complete({ status: response.statusCode, json, bytes: size, noStore: response.headers['cache-control']?.includes('no-store') === true });
      });
      response.on('error', reject);
    });
    req.setTimeout(90000, () => req.destroy(new Error('API request timed out.')));
    req.on('error', error => reject(new Error(`API transport failure: ${error.code ?? error.name}`)));
    req.end(bytes);
  });
}
function check(name, passed, observed = {}) {
  checks.push({ name, passed: Boolean(passed), observed });
  if (!passed) throw new Error(`Check failed: ${name}`);
}
async function saveState() {
  await mkdir(dirname(statePath), { recursive: true });
  await writeFile(statePath, JSON.stringify(state, null, 2) + '\n');
}
function roomPath(room) { return `/api/rooms/${encodeURIComponent(room)}`; }
async function admin(name, path, body, method = 'POST', role = 'owner') {
  stage = name;
  const response = await request(role, method, path, body);
  check(name, response.status === 200 && response.json?.accepted, { status: response.status, audited: Boolean(response.json?.auditId) });
  return response.json;
}
async function member(room, role, membership, actor = 'owner') {
  return admin(`Set ${role} ${membership} in ${room}`, `${roomPath(room)}/members/${encodeURIComponent(accounts[role].did)}`, { role: membership }, 'PUT', actor);
}
async function post(role, room, operationId, text, replyTo) {
  stage = `Source post ${operationId}`;
  const response = await request(role, 'POST', `${roomPath(room)}/messages`, { operationId, text, ...(replyTo ? { replyTo } : {}) });
  check(`Accepted source post ${operationId}`, response.status === 200 && response.json?.state === 'accepted' && response.json?.sourceUri && response.json?.sourceCid,
    { status: response.status, state: response.json?.state ?? null, detail: response.json?.detail ?? null });
  const reference = { uri: response.json.sourceUri, cid: response.json.sourceCid };
  state.sourceRecords.push({ role, room, operationId, reference });
  await saveState();
  return reference;
}
async function history(role, room, cursor) {
  stage = `Read ${role} ${room}`;
  const response = await request(role, 'GET', `${roomPath(room)}/messages${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`);
  check(`Bounded authorized history ${role}`, response.status === 200 && Array.isArray(response.json?.messages)
    && response.json.messages.length <= 20 && response.bytes <= 128 * 1024 && response.noStore,
  { status: response.status, messages: response.json?.messages?.length ?? null, responseBytes: response.bytes, noStore: response.noStore });
  return response.json;
}

async function externalProof() {
  stage = 'Stage independent protocol helper';
  for (const command of [
    ['exec', 'tangent-spaces-network', 'mkdir', '-p', '/atproto/tangent-client'],
    ['cp', resolve(root, 'probes/scripts/direct-spaces.mjs'), 'tangent-spaces-network:/atproto/tangent-client/direct-spaces.mjs'],
  ]) {
    const result = spawnSync('docker', command, { windowsHide: true, stdio: 'ignore', timeout: 15000 });
    if (result.error || result.status !== 0) throw new Error('Independent protocol helper staging failed.');
  }
  // Independent official-client process: credentials and DPoP keys remain in that process until EOF.
  const child = spawn('docker', ['exec', '-i', 'tangent-spaces-network', 'node', '/atproto/tangent-client/direct-spaces.mjs'],
    { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  const lines = createInterface({ input: child.stdout, crlfDelay: Infinity })[Symbol.asyncIterator]();
  child.stderr.resume(); // Never forward child diagnostics which might contain provider metadata.
  let spawnFailed = false;
  child.on('error', () => { spawnFailed = true; });
  async function direct(command) {
    stage = `Independent protocol ${command.command}`;
    if (spawnFailed) throw new Error('Independent protocol helper unavailable.');
    child.stdin.write(JSON.stringify(command) + '\n');
    let timeout;
    try {
      const next = await Promise.race([lines.next(), new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('Independent protocol helper timed out.')), 35000); })]);
      if (next.done || next.value.length > 65536) throw new Error('Independent protocol helper response unavailable.');
      return JSON.parse(next.value);
    } finally { clearTimeout(timeout); }
  }
  async function syncRoom(room) {
    const response = await request('owner', 'POST', `${roomPath(room)}/sync`, {});
    check('Real source reconciliation completed', response.status === 200 && ['checked', 'catching-up'].includes(response.json?.freshness),
      { status: response.status, freshness: response.json?.freshness ?? null });
  }
  async function externalWrite(key, text, createdAt = new Date().toISOString(), replyTo) {
    const result = await direct({ command: 'write', role: 'agent', space: state.external.space, rkey: key,
      record: { $type: 'local.tangent.message', text, createdAt, ...(replyTo ? { replyTo } : {}) } });
    check(`Independent PDS accepts source ${key}`, result.status === 200 && result.body?.uri && result.body?.cid,
      { status: result.status, stage: result.stage ?? null, error: result.body?.error ?? result.error ?? null });
    return { uri: result.body.uri, cid: result.body.cid };
  }
  try {
    const room = state.rooms.secondary;
    state.external = { room, space: `at://${accounts.authority.did}/space/local.tangent.room/${room}` };
    for (const role of ['agent', 'outsider']) {
      const result = await direct({ command: 'login', role });
      check(`Independent disposable ${role} authentication`, result.status === 200 && result.authenticated,
        { status: result.status, authentication: result.authentication ?? null });
    }
    state.external.seed = await post('owner', room, 'external-seed', 'A source for the independent credential test.');
    const seedKey = state.external.seed.uri.split('/').at(-1);
    const issued = await direct({ command: 'issue', role: 'agent', space: state.external.space, name: 'before_removal' });
    check('Admitted member receives a real Space credential', issued.status === 200 && issued.issued,
      { status: issued.status, stage: issued.stage ?? null, error: issued.body?.error ?? issued.error ?? null, lifetimeSeconds: issued.lifetimeSeconds ?? null });
    const crossPds = await direct({ command: 'read', name: 'before_removal', space: state.external.space, repo: 'owner', rkey: seedKey });
    check('Independent credential reads the other PDS source', crossPds.status === 200 && crossPds.body?.cid === state.external.seed.cid,
      { status: crossPds.status, sameCid: crossPds.body?.cid === state.external.seed.cid });
    const outsider = await direct({ command: 'issue', role: 'outsider', space: state.external.space, name: 'outsider' });
    check('Provider denies a new credential to an outsider', outsider.status === 400 && outsider.body?.error === 'UserNotAuthorized', { status: outsider.status, error: outsider.body?.error });

    await member(room, 'agent', 'Reader', 'manager');
    const readerPost = await request('agent', 'POST', `${roomPath(room)}/messages`, { operationId: 'reader-app', text: 'Denied current write policy.' });
    check('Read-only membership denies application posting', readerPost.status === 403, { status: readerPost.status });
    state.external.readOnly = await externalWrite('external-readonly', 'The author PDS can hold a record that this room rejects.');
    await syncRoom(room);
    let page = await history('owner', room);
    check('Current read-only policy rejects an independently written source', page.messages.length === 1 && !page.messages.some(message => message.sourceCid === state.external.readOnly.cid), { visible: page.messages.length });
    await member(room, 'agent', 'Member', 'manager');
    await syncRoom(room);
    page = await history('owner', room);
    check('Restoring membership does not re-admit a previously rejected version', page.messages.length === 1 && !page.messages.some(message => message.sourceCid === state.external.readOnly.cid), { visible: page.messages.length });

    // Write the parent before its child but give the child the lexicographically earlier key.
    state.external.parent = await externalWrite('z-external-parent', 'A parent discovered out of record-key order.');
    state.external.reply = await externalWrite('a-external-reply', 'A valid reply must not be rejected merely because it sorts first.', new Date().toISOString(), state.external.parent);
    await syncRoom(room);
    await syncRoom(room);
    page = await history('owner', room);
    check('Independent parent and reply survive discovery-order differences', page.messages.some(message => message.sourceCid === state.external.parent.cid)
      && page.messages.some(message => message.sourceCid === state.external.reply.cid && message.content.replyTo?.cid === state.external.parent.cid), { visible: page.messages.length });

    await member(room, 'agent', 'Removed', 'manager');
    const cached = await request('agent', 'GET', `${roomPath(room)}/messages`);
    check('Removal immediately denies cached application history', cached.status === 403, { status: cached.status });
    const fresh = await direct({ command: 'issue', role: 'agent', space: state.external.space, name: 'after_removal' });
    check('Removal denies a newly requested upstream credential', fresh.status === 400 && fresh.body?.error === 'UserNotAuthorized', { status: fresh.status, error: fresh.body?.error });
    const retained = await direct({ command: 'read', name: 'before_removal', space: state.external.space, repo: 'owner', rkey: seedKey });
    check('An already issued upstream credential retains its documented validity window', retained.status === 200 && retained.body?.cid === state.external.seed.cid && retained.expiresInSeconds > 0,
      { status: retained.status, expiresInSeconds: retained.expiresInSeconds ?? null, lifetimeSeconds: retained.lifetimeSeconds ?? null,
        observed: 'Application history and new credential issuance are denied; the existing credential still reads immediately after removal. Its declared expiry was recorded, not waited through.' });
    state.external.late = await externalWrite('external-delayed', 'An old createdAt cannot bypass current removal.', '2000-01-01T00:00:00.000Z');
    await syncRoom(room);
    page = await history('owner', room);
    check('A newly observed backdated record is rejected after removal', !page.messages.some(message => message.sourceCid === state.external.late.cid), { visible: page.messages.length, backdated: true });
    check('Previously accepted author messages remain after removal', page.messages.some(message => message.sourceCid === state.external.parent.cid)
      && page.messages.some(message => message.sourceCid === state.external.reply.cid), { retained: true });
    const rebuild = await request('owner', 'POST', `${roomPath(room)}/rebuild`, {});
    check('Owner rebuild reconstructs the accepted projection', rebuild.status === 200 && rebuild.json?.rebuilt === 3, { status: rebuild.status, rebuilt: rebuild.json?.rebuilt ?? null });
    await member(room, 'agent', 'Member', 'manager');
    await syncRoom(room);
    page = await history('owner', room);
    check('Rebuild and re-admission preserve rejected source decisions', page.messages.length === 3
      && !page.messages.some(message => [state.external.late.cid, state.external.readOnly.cid].includes(message.sourceCid)), { visible: page.messages.length });
    state.external.acceptedCount = 3;
    state.external.completed = true;
    await saveState();
  } finally {
    child.stdin.end();
    lines.return?.();
  }
}

try {
  const ready = await request(null, 'GET', '/health/ready');
  check('Real host is ready', ready.status === 200, { status: ready.status });
  if (args.get('verify-restart') === 'true') {
    state = JSON.parse(await readFile(statePath, 'utf8'));
    check('Restart checkpoint is for this host', state.origin === origin && state.expectedAfterAck, { origin });
    const page = await history('agent', state.rooms.primary);
    check('Durable acknowledgement resumes after restart', page.messages.length === 1
      && page.messages[0].sourceUri === state.expectedAfterAck.uri && page.messages[0].sourceCid === state.expectedAfterAck.cid,
    { count: page.messages.length, boundary: page.boundary });
    const captured = await history('agent', state.rooms.primary, state.beforeConcurrentResume);
    check('Protected resume cursor survives restart', captured.messages.some(message => message.sourceCid === state.concurrent.cid), { count: captured.messages.length });
  } else if (args.get('phase') === 'external') {
    await externalProof();
  } else {
    await saveState();
    if (!state.setupDone) for (const [label, room] of Object.entries(state.rooms)) {
      await admin(`Create isolated ${label} room`, '/api/rooms', { key: room, title: `Conversation proof ${label}`, admission: 'InvitationOnly' });
      await admin(`Provision real ${label} Space`, `${roomPath(room)}/provision`, {});
      await member(room, 'manager', 'Manager');
      await member(room, 'agent', 'Member', 'manager');
    }
    state.setupDone = true;
    await saveState();
    if (args.get('phase') !== 'setup') {
    const room = state.rooms.primary;
    state.first = await post('owner', room, 'first', 'A source-backed conversation begins.');
    state.reply = await post('agent', room, 'reply', 'A reply from the other PDS.', state.first);
    check('Real posts carry distinct author repos and exact Space', state.first.uri.includes(`/${accounts.owner.did}/local.tangent.message/`)
      && state.reply.uri.includes(`/${accounts.agent.did}/local.tangent.message/`)
      && state.first.uri.startsWith(`at://${accounts.authority.did}/space/local.tangent.room/${room}/`), { crossPds: accounts.owner.pds !== accounts.agent.pds });
    const repeat = await request('owner', 'POST', `${roomPath(room)}/messages`, { operationId: 'first', text: 'A source-backed conversation begins.' });
    check('Repeated operation returns the same source URI and CID', repeat.status === 200 && repeat.json?.sourceUri === state.first.uri && repeat.json?.sourceCid === state.first.cid, { status: repeat.status });
    const conflict = await request('owner', 'POST', `${roomPath(room)}/messages`, { operationId: 'first', text: 'A conflicting payload.' });
    check('Operation reuse with conflicting payload is rejected', conflict.status === 400, { status: conflict.status });
    const forged = await request('agent', 'POST', `${roomPath(room)}/messages`, { operationId: 'forged', text: 'Cannot supply authorship.', authorDid: accounts.owner.did });
    check('A supplied author field is rejected', forged.status === 400, { status: forged.status });
    const crossReply = await request('agent', 'POST', `${roomPath(state.rooms.secondary)}/messages`, { operationId: 'cross-reply', text: 'Wrong room.', replyTo: state.first });
    check('A reply cannot reference another room', crossReply.status === 400, { status: crossReply.status });
    const noOrigin = await request('agent', 'POST', `${roomPath(room)}/messages`, { operationId: 'no-origin', text: 'Rejected.' }, { origin: null });
    const wrongOrigin = await request('agent', 'POST', `${roomPath(room)}/messages`, { operationId: 'wrong-origin', text: 'Rejected.' }, { origin: 'https://other.example' });
    check('Cookie posting requires the same browser origin', noOrigin.status === 403 && wrongOrigin.status === 403, { missing: noOrigin.status, crossOrigin: wrongOrigin.status });
    const tooLarge = await request('agent', 'POST', `${roomPath(room)}/messages`, { operationId: 'too-large', text: 'é'.repeat(2049) });
    check('Message size is bounded in UTF-8 bytes', tooLarge.status === 400, { status: tooLarge.status });
    const initial = await history('agent', room);
    check('History contains exactly the original and the cross-PDS reply', initial.messages.length === 2 && initial.messages[1].content.replyTo.uri === state.first.uri
      && initial.messages[1].authorDid === accounts.agent.did, { count: initial.messages.length });
    const outsider = await request('outsider', 'GET', `${roomPath(room)}/messages`);
    check('A populated local projection still denies an outsider', outsider.status === 403 && outsider.noStore, { status: outsider.status, noStore: outsider.noStore });

    for (let index = 3; index <= 25; index++) await post('owner', room, `page-${index}`, `Bounded page message ${index}.`);
    const firstPage = await history('agent', room);
    check('A full page contains 20 messages and an opaque continuation', firstPage.messages.length === 20 && firstPage.nextCursor && firstPage.boundary === 25
      && !firstPage.nextCursor.includes(accounts.agent.did) && !firstPage.nextCursor.includes(room), { count: firstPage.messages.length, boundary: firstPage.boundary });
    state.firstPageCursor = firstPage.nextCursor;
    state.concurrent = await post('owner', room, 'concurrent', 'This arrived after the captured boundary.');
    const secondPage = await history('agent', room, firstPage.nextCursor);
    const captured = [...firstPage.messages, ...secondPage.messages];
    check('Concurrent insertion does not enter an already captured read window', secondPage.messages.length === 5 && secondPage.boundary === 25 && secondPage.nextCursor == null
      && new Set(captured.map(message => message.sourceUri + '#' + message.sourceCid)).size === 25
      && captured.every(message => message.sequence <= 25 && message.sourceCid !== state.concurrent.cid), { captured: captured.length, secondPage: secondPage.messages.length, boundary: secondPage.boundary });
    state.beforeConcurrentResume = secondPage.resumeCursor;
    const latest = await history('agent', room, secondPage.resumeCursor);
    check('Resume observes the later insertion once', latest.messages.length === 1 && latest.messages[0].sourceCid === state.concurrent.cid, { count: latest.messages.length, boundary: latest.boundary });
    const flip = Math.floor(firstPage.nextCursor.length / 2);
    const tampered = firstPage.nextCursor.slice(0, flip) + (firstPage.nextCursor[flip] === 'A' ? 'B' : 'A') + firstPage.nextCursor.slice(flip + 1);
    const altered = await request('agent', 'GET', `${roomPath(room)}/messages?cursor=${encodeURIComponent(tampered)}`);
    const actorCursor = await request('owner', 'GET', `${roomPath(room)}/messages?cursor=${encodeURIComponent(firstPage.nextCursor)}`);
    const roomCursor = await request('agent', 'GET', `${roomPath(state.rooms.secondary)}/messages?cursor=${encodeURIComponent(firstPage.nextCursor)}`);
    check('Tampered and cross-identity or cross-room cursors are rejected', altered.status === 400 && actorCursor.status === 400 && roomCursor.status === 400,
      { tampered: altered.status, differentActor: actorCursor.status, differentRoom: roomCursor.status });
    const ack = await request('agent', 'POST', `${roomPath(room)}/read-position`, { cursor: latest.resumeCursor });
    check('Acknowledgement stores the observed read position', ack.status === 200 && ack.json?.sequence === 26, { status: ack.status, sequence: ack.json?.sequence });
    const empty = await history('agent', room);
    check('A subsequent read resumes from durable acknowledgement', empty.messages.length === 0 && empty.boundary === 26, { count: empty.messages.length, boundary: empty.boundary });
    const olderAck = await request('agent', 'POST', `${roomPath(room)}/read-position`, { cursor: secondPage.resumeCursor });
    check('A stale acknowledgement cannot move the read position backwards', olderAck.status === 200 && olderAck.json?.sequence === 26, { sequence: olderAck.json?.sequence });
    state.acknowledgedSequence = 26;
    state.acknowledgedResume = latest.resumeCursor;
    state.expectedAfterAck = await post('owner', room, 'after-ack', 'A durable read-position restart checkpoint.');
    const tail = await history('agent', room);
    check('New content appears after the acknowledged position', tail.messages.length === 1 && tail.messages[0].sourceCid === state.expectedAfterAck.cid, { count: tail.messages.length, boundary: tail.boundary });
    state.finalBoundary = tail.boundary;
    await saveState();
    if (args.get('external') === 'true') await externalProof();
    }
  }
  completed = true;
} catch (error) {
  // Only our stage/status assertions are reported; response bodies and cookie values never enter evidence.
  checks.push({ name: 'Proof completion', passed: false, observed: { stage, error: error.message?.startsWith('Check failed:') || error.message?.startsWith('API transport failure:') ? error.message : error.name } });
  process.exitCode = 1;
} finally {
  await saveState();
  await mkdir(dirname(evidencePath), { recursive: true });
  const evidence = { date: new Date().toISOString(), origin, run: state.run, passed: completed, rooms: state.rooms,
    testNetworkRevision: 'c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae', source: 'Real Tangent HTTP API and actual local PDS records; existing real scoped OAuth sessions.',
    mode: args.get('verify-restart') === 'true' ? 'restart-check' : args.get('phase') ?? 'conversation', secretsIncluded: false,
    checks, limits: ['The upstream credential lifetime is observed from the real issued credential; expiry-time denial was not waited through.',
      'Actual process restart and backup restoration use the private checkpoint and separate receipts.'],
    restartCheckpoint: 'Private .local state contains room/source identifiers and protected read cursors. Existing private cookie files retain authentication.' };
  await writeFile(evidencePath, JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify({ passed: completed, run: state.run, checks: checks.length, failed: checks.filter(item => !item.passed), evidence: evidencePath, checkpoint: statePath }, null, 2));
}
