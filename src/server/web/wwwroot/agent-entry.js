const discoveryUrl = 'http://127.0.0.1:5219/api/discovery';
const operatorOrigin = 'http://127.0.0.1:5219';
const maximumResponse = 4 * 1024;

export async function discoverLocalConnector({ send = globalThis.fetch.bind(globalThis), timeoutMs = 900 } = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await send(discoveryUrl, {
      method: 'GET',
      mode: 'cors',
      credentials: 'omit',
      redirect: 'error',
      cache: 'no-store',
      headers: { Accept: 'application/json' },
      signal: controller.signal
    });
    if (!response.ok || response.redirected || !response.headers.get('content-type')?.toLowerCase().startsWith('application/json')) return null;
    const declaredLength = Number(response.headers.get('content-length'));
    if (Number.isFinite(declaredLength) && declaredLength > maximumResponse) {
      await response.body?.cancel();
      return null;
    }
    const raw = await response.text();
    if (new TextEncoder().encode(raw).byteLength > maximumResponse) return null;
    const discovery = JSON.parse(raw);
    return discovery?.product === 'tangent-space-connector'
      && discovery?.discoveryVersion === 1
      && discovery?.operatorOrigin === operatorOrigin
      ? discovery : null;
  } catch {
    return null;
  } finally {
    clearTimeout(timeout);
  }
}

export async function qualifyAgentEntry(link, options) {
  if (!link || !await discoverLocalConnector(options)) return false;
  link.href = operatorOrigin + '/';
  link.textContent = 'Connect an Agent';
  link.title = 'Open the Tangent connector running on this computer';
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  link.dataset.connector = 'ready';
  return true;
}

if (typeof document !== 'undefined') {
  qualifyAgentEntry(document.getElementById('agent-entry'));
}
