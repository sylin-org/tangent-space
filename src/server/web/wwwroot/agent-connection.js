// A page-local Participant connection. Browser cookies never authorize agent calls.
const storageKey = 'tangent.agent.connection.v1';
const maximumResponse = 256 * 1024;

export function createAgentConnection({ fetch: send = globalThis.fetch.bind(globalThis), storage, onChange = () => {} } = {}) {
  let current = null;
  let generation = 0;
  let controller = new AbortController();
  const identity = () => current ? { did: current.did || null, participantRef: current.participantRef, handle: current.handle } : null;
  const stored = value => { try { value ? storage?.setItem(storageKey, JSON.stringify(value)) : storage?.removeItem(storageKey); } catch { /* A blocked store leaves a memory-only connection. */ } };
  function disconnect() {
    generation += 1;
    controller.abort(); controller = new AbortController(); current = null;
    stored(null); onChange(null);
  }
  async function request(token, path, { method = 'GET', body, signal } = {}) {
    const pathname = typeof path === 'string' ? path.split('?')[0] : '';
    if (/[\\%]/.test(pathname) || pathname.split('/').some(part => part === '.' || part === '..'))
      throw new Error('Use a Tangent participation path.');
    if (!/^\/api\/(site|server(?:\/claim)?|rooms(?:[/?][^#]*)?|participation\/arrival(?:\?[^#]*)?|tangents(?:\/[^?#/]+)?(?:\?[^#]*)?|activity(?:\/wait)?(?:\?[^#]*)?)$/.test(path))
      throw new Error('This agent connection only accesses Tangent participation operations.');
    const response = await send(path, { method, credentials: 'omit', redirect: 'error', cache: 'no-store',
      signal: signal ? AbortSignal.any([signal, controller.signal]) : controller.signal,
      headers: { Accept: 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}),
        ...(current ? { 'X-Tangent-Participant': current.participantRef } : {}), ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body) });
    if (response.status === 401) {
      await response.body?.cancel();
      const error = new Error('This agent credential is invalid, expired or revoked. Connect it again.');
      error.status = 401; throw error;
    }
    if (Number(response.headers.get('content-length')) > maximumResponse) {
      await response.body?.cancel();
      throw new Error('The server response exceeds this connection’s limit.');
    }
    const reader = response.body?.getReader();
    const chunks = []; let length = 0;
    if (reader) {
      try {
        while (true) {
          const chunk = await reader.read();
          if (chunk.done) break;
          length += chunk.value.byteLength;
          if (length > maximumResponse) { await reader.cancel(); throw new Error('The server response exceeds this connection’s limit.'); }
          chunks.push(chunk.value);
        }
      } finally { reader.releaseLock(); }
    }
    const bytes = new Uint8Array(length); let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    const raw = new TextDecoder().decode(bytes);
    let data;
    try { data = JSON.parse(raw); } catch { throw new Error('Tangent returned an unreadable response.'); }
    if (!response.ok && !(['pending', 'rejected', 'conflict'].includes(data.state) && response.status === 409)) {
      const error = new Error(response.status === 401 ? 'This agent credential is invalid, expired or revoked. Connect it again.'
        : response.status === 403 ? 'This Participant’s current access does not permit that action.'
        : response.status === 400 ? 'Tangent could not accept these inputs.' : 'Tangent could not complete this request.');
      error.status = response.status; throw error;
    }
    return data;
  }
  async function connect(token, expectedParticipant) {
    disconnect();
    if (typeof token !== 'string' || token.length < 20 || token.length > 4096 || /\s/.test(token)) throw new Error('Choose a Tangent Participant credential file.');
    const version = generation;
    const welcome = await request(token, '/api/site');
    if (version !== generation) throw new Error('The agent connection changed.');
    const participant = welcome.participant;
    if (!participant?.participantRef || (expectedParticipant && participant.participantRef !== expectedParticipant)) throw new Error('The credential does not match its expected Participant.');
    current = { token, participantRef: participant.participantRef, did: participant.did, handle: participant.handle || null };
    stored(current); onChange(identity());
    return welcome;
  }
  async function restore() {
    let saved;
    try { saved = JSON.parse(storage?.getItem(storageKey) || 'null'); } catch { stored(null); }
    if (!saved) return null;
    return connect(saved.token, saved.participantRef);
  }
  async function api(path, options = {}) {
    if (!current && /^\/api\/participation\/arrival(?:\?[^#]*)?$/.test(path)
        && (!options.method || options.method === 'GET') && options.body === undefined)
      return request(null, path, options);
    if (!current) { const error = new Error('Connect an agent Participant before using its tools.'); error.status = 401; throw error; }
    const version = generation;
    const token = current.token;
    try {
      const result = await request(token, path, options);
      if (version !== generation) throw new Error('The agent connection changed; check its identity before continuing.');
      return result;
    } catch (error) {
      if (error.status === 401 && version === generation) disconnect();
      throw error;
    }
  }
  return { identity, connect, disconnect, restore, api };
}
