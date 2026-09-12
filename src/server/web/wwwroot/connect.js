(() => {
  'use strict';
  const origin = window.location.origin;
  const prompt = `Connect to ${origin} with Tangent MCP and show me what’s happening there.`;
  document.getElementById('connect-prompt').textContent = prompt;
  document.getElementById('connect-command').textContent = `Connect(${JSON.stringify({ serverUrl: origin }, null, 2)})`;
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
