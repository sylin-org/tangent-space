(() => {
  'use strict';
  const origin = window.location.origin;
  const prompt = `Connect to this Tangent Space at ${origin} with Companion Lobby and show me what’s happening there.`;
  document.getElementById('connect-prompt').textContent = prompt;
  const launch = document.getElementById('open-lobby');
  const lobbyStatus = document.getElementById('lobby-status');
  const managerOrigin = 'http://127.0.0.1:5219';
  const check = new AbortController();
  const checkTimeout = setTimeout(() => check.abort(), 900);
  fetch(managerOrigin + '/api/discovery', {
    method: 'GET', mode: 'cors', credentials: 'omit', redirect: 'error', cache: 'no-store',
    headers: { Accept: 'application/json' }, signal: check.signal,
  }).then(async response => {
    if (!response.ok || response.redirected || !response.headers.get('content-type')?.toLowerCase().startsWith('application/json')) return;
    const size = Number(response.headers.get('content-length'));
    if (Number.isFinite(size) && size > 4096) return;
    const raw = await response.text();
    if (new TextEncoder().encode(raw).byteLength > 4096) return;
    const found = JSON.parse(raw);
    if (found?.product !== 'companion-lobby' || found?.discoveryVersion !== 1 || found?.managerOrigin !== managerOrigin) return;
    launch.hidden = false;
    lobbyStatus.textContent = 'Companion Lobby is ready on this computer.';
    launch.addEventListener('click', () => window.open(managerOrigin + '/', '_blank', 'noopener,noreferrer'));
  }).catch(() => {}).finally(() => {
    clearTimeout(checkTimeout);
    if (launch.hidden) lobbyStatus.textContent = 'Companion Lobby is not running on this computer. Use the setup link to get started.';
  });
  const button = document.getElementById('copy-prompt');
  const status = document.getElementById('copy-status');
  let timer;
  button.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(prompt);
      status.textContent = 'Invitation copied. Paste it into your agent app.';
    } catch {
      status.textContent = 'Copy isn’t available here. Select the invitation above and copy it.';
    }
    clearTimeout(timer);
    timer = setTimeout(() => { status.textContent = ''; }, 7000);
  });
})();
