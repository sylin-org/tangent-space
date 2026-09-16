// Edit-history viewer: the closed disclosure under an edited post. Reads the post's
// recorded pre-edit snapshots from the changelog partition of the generic history surface and
// renders each era verbatim from the snapshot payload — the server's text, facets and
// timestamps; nothing is reconstructed or recomputed client-side. Plain JS.
'use strict';
(() => {
  const MAX_ERAS = 20;    // Bounded rendering: the newest eras only, with an honest omission line.
  const FETCH_SIZE = 200; // The surface's page ceiling; a hit means the history may be truncated.

  function element(tag, className, value) {
    const node = document.createElement(tag);
    node.className = className;
    node.textContent = value;
    return node;
  }

  /// The fetch is always scoped to one post: the changelog set, a bounded page, and a single
  /// ofPostId equality clause in the framework's URL-encoded JSON filter DSL. The server's
  /// per-row gate still applies, so a viewer without authority receives an honest empty array.
  function historyPath(messageId) {
    return '/api/history/messages?set=changelog&size=' + FETCH_SIZE
      + '&filter=' + encodeURIComponent(JSON.stringify({ ofPostId: messageId }));
  }

  async function readSnapshots(messageId) {
    const response = await fetch(historyPath(messageId),
      { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
    if (!response.ok) { const error = new Error('History is unavailable.'); error.status = response.status; throw error; }
    const rows = await response.json();
    if (!Array.isArray(rows)) throw new Error('History could not be read.');
    return rows;
  }

  /// Newest-first order by the snapshot chain: the live row's changeId names the newest
  /// snapshot and each snapshot's previousChangeId names its predecessor. When the head is
  /// unknown (or a truncated page cut the chain), unreferenced rows serve as fallback heads
  /// and leftovers fall to the tail by snapshot id — GUIDv7 sorts lexicographically in time
  /// order, and every era shares the post's acceptedAt so timestamps cannot discriminate.
  function orderedEras(rows, headId) {
    const byId = new Map(rows.filter(row => row && typeof row.id === 'string').map(row => [row.id, row]));
    const referenced = new Set(rows.map(row => row?.previousChangeId).filter(Boolean));
    const byNewest = (left, right) => String(right.id).localeCompare(String(left.id));
    let head = typeof headId === 'string' && byId.has(headId) ? byId.get(headId) : undefined;
    if (!head) head = [...byId.values()].filter(row => !referenced.has(row.id)).sort(byNewest)[0];
    const ordered = [], seen = new Set();
    for (let node = head; node && !seen.has(node.id);
      node = node.previousChangeId ? byId.get(node.previousChangeId) : undefined) {
      seen.add(node.id);
      ordered.push(node);
    }
    return [...ordered, ...[...byId.values()].filter(row => !seen.has(row.id)).sort(byNewest)];
  }

  /// The author label from the page's own resolution map, mirroring the live byline: You for
  /// the signed-in viewer, @handle when the page resolved one, the DID verbatim otherwise.
  function authorLabel(did, context) {
    if (!did) return '';
    if (context.viewerDid && did === context.viewerDid) return 'You';
    const handle = typeof context.handles?.[did] === 'string' ? context.handles[did] : context.resolved?.[did]?.handle;
    return context.resolved?.[did]?.displayName || (handle ? '@' + handle.replace(/^@/, '') : 'Participant');
  }

  /// One era: verbatim snapshot text — facet decoration only through the shared renderer,
  /// which validates every range against this era's own text and degrades to plain text
  /// otherwise — plus the author label and the era's own timestamp.
  function eraNode(row, context) {
    const era = element('div', 'history-era', '');
    const byline = element('div', 'message-byline', '');
    const stamp = row.editedAt ? 'Edited ' + new Date(row.editedAt).toLocaleString()
      : 'Posted ' + new Date(row.acceptedAt).toLocaleString();
    byline.append(element('strong', 'message-author', authorLabel(row.authorParticipantId, context)), element('time', '', stamp));
    era.append(byline);
    const text = row.content && typeof row.content.text === 'string' ? row.content.text : '';
    const faceted = window.TangentFacets?.renderFacetedText?.(text, Array.isArray(row.facets) ? row.facets : undefined, context.resolved);
    era.append(faceted || element('p', 'message-text', text));
    return era;
  }

  async function fill(details, message, context) {
    details.querySelector('.history-body')?.remove();
    const body = element('div', 'history-body', '');
    body.setAttribute('aria-live', 'polite');
    body.append(element('p', 'hint', 'Loading earlier versions…'));
    details.append(body);
    let rows;
    try { rows = await readSnapshots(message.id); }
    catch (error) {
      if (!details.isConnected) return;
      const restricted = [401, 403].includes(error.status);
      body.replaceChildren(element('p', 'hint', restricted ? 'Earlier versions aren’t available to this account.' : 'Earlier versions couldn’t be loaded. Your post is still here.'));
      if (!restricted) {
        const retry = element('button', 'btn btn-quiet', 'Try again'); retry.type = 'button';
        retry.addEventListener('click', () => fill(details, message, context)); body.append(retry);
      }
      return;
    }
    if (!details.isConnected) return;
    body.replaceChildren();
    const eras = orderedEras(rows, message.changeId);
    if (!eras.length) {
      // The history endpoint may filter rows by permission; an empty response cannot
      // establish that a revision never existed.
      body.append(element('p', 'hint', 'No earlier versions are available to you.'));
      return;
    }
    for (const row of eras.slice(0, MAX_ERAS)) body.append(eraNode(row, context));
    if (eras.length > MAX_ERAS || rows.length >= FETCH_SIZE)
      body.append(element('p', 'hint', 'Showing the most recent available versions.'));
  }

  /// The disclosure rooms.js wires under an edited post: closed until opened, keyboard
  /// accessible as a native summary, and lazy — the scoped fetch runs on first open only.
  /// Opening and closing never touches the composer or its draft.
  function disclosure(message, context) {
    if (!message || typeof message.id !== 'string') return null;
    const details = document.createElement('details');
    details.className = 'source-details history-details';
    const summary = element('summary', '', 'Edited · history');
    if (message.editedAt) summary.title = 'Last edited ' + new Date(message.editedAt).toLocaleString();
    details.append(summary);
    let loaded = false;
    details.addEventListener('toggle', () => {
      if (!details.open || loaded) return;
      loaded = true;
      fill(details, message, context || {});
    });
    return details;
  }

  window.TangentHistory = { disclosure };
})();
