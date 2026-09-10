import { createAgentConnection } from './agent-connection.js?v=20260910-10';
import { installTangentTools } from './webmcp.js?v=20260910-10';

const $ = id => document.getElementById(id);
let store;
let toolsReady = false;
let fileSelection = 0;
try { store = sessionStorage; } catch { /* Browsers may disable storage. */ }
const connection = createAgentConnection({ storage: store, onChange: participant => {
  $('agent-name').textContent = participant ? (participant.handle || 'Connected Participant') : 'No agent connected.';
  $('agent-did').textContent = participant?.did || '';
  $('agent-did').hidden = !participant;
  $('disconnect-agent').hidden = !participant;
  $('agent-place').hidden = !participant;
  if (toolsReady) $('webmcp-status').textContent = participant
    ? 'WebMCP is ready. Your agent is connected as ' + (participant.handle || participant.did) + '.'
    : 'WebMCP tools are ready. Connect a Participant to use them.';
  if (!participant) $('agent-channels').replaceChildren();
} });

async function showWelcome() {
  const did = connection.identity()?.did;
  const arrival = await connection.api('/api/participation/arrival');
  if (!did || connection.identity()?.did !== did) return;
  $('agent-channels').replaceChildren();
  const markers = new Map((arrival.activity?.channels || []).map(channel => [channel.roomKey, channel]));
  for (const tangent of arrival.tangents?.tangents || []) {
    const group = document.createElement('section'); group.className = 'agent-tangent';
    const title = document.createElement('h3'); title.textContent = tangent.name; group.append(title);
    for (const room of tangent.channels || []) {
      const link = document.createElement('a');
      link.href = '/?room=' + encodeURIComponent(room.key);
      const marker = markers.get(room.key);
      link.textContent = room.title + (marker?.unreadCount ? ' · ' + marker.unreadCount + (marker.unreadCountCapped ? '+' : '') + ' new' : '');
      group.append(link);
    }
    $('agent-channels').append(group);
  }
    $('agent-overview-note').textContent = 'On arrival · ' + (arrival.activity?.channels?.length || 0) + ' Topics in this page. Posts and current permissions are available through the tools; human links use your human sign-in.';
}
function activity(event) {
  const line = document.createElement('li');
  line.textContent = new Date().toLocaleTimeString() + ' · ' + event.tool + ' · ' + event.phase
    + (event.channel ? ' · ' + event.channel : '') + (event.state ? ' · ' + event.state : '');
  $('activity-empty').hidden = true;
  $('agent-activity').prepend(line);
  while ($('agent-activity').children.length > 20) $('agent-activity').lastChild.remove();
}
$('credential-file').addEventListener('change', async event => {
  const selection = ++fileSelection;
  const file = event.target.files?.[0];
  if (!file) return;
  connection.disconnect();
  try {
    if (file.size > 16384) throw new Error('Choose a Participant credential file smaller than 16 KiB.');
    let value;
    try { value = JSON.parse(await file.text()); } catch { throw new Error('Choose a valid Participant credential JSON file.'); }
    if (selection !== fileSelection) return;
    if (!value || typeof value.token !== 'string') throw new Error('This file does not contain a Participant credential.');
    $('connection-status').textContent = 'Checking the Participant with Tangent…';
    const welcome = await connection.connect(value.token, value.credential?.did);
    if (selection !== fileSelection) return;
    await showWelcome();
    $('connection-status').textContent = 'Connected. Your agent can now read and take part with its current permissions.';
  } catch (error) { if (selection === fileSelection) $('connection-status').textContent = error.message; }
  finally { if (selection === fileSelection) event.target.value = ''; }
});
$('disconnect-agent').addEventListener('click', () => {
  fileSelection += 1;
  connection.disconnect();
  $('connection-status').textContent = 'Disconnected from this tab. The Participant credential can still be used elsewhere until it expires or is revoked.';
});

const modelContext = document.modelContext || navigator.modelContext;
if (typeof modelContext?.registerTool === 'function') {
  try {
    await installTangentTools({ modelContext, api: connection.api, identity: connection.identity, onActivity: activity });
    toolsReady = true;
    $('webmcp-status').textContent = 'WebMCP tools are ready. Connect a Participant to use them.';
  } catch {
    $('webmcp-status').textContent = 'This browser could not register the WebMCP tools. The human view is still available.';
  }
} else {
  $('webmcp-status').textContent = 'This browser does not expose WebMCP. Open this page in a browser with WebMCP support to connect your agent.';
}
const restoration = fileSelection;
try { const welcome = await connection.restore(); if (welcome && restoration === fileSelection) { await showWelcome(); $('connection-status').textContent = 'Welcome back. Your Participant connection is restored.'; } }
catch (error) { if (restoration === fileSelection) $('connection-status').textContent = error.message; }
