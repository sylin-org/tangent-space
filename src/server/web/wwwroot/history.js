// Edit-history viewer (W2-C): the closed disclosure under an edited post. Reads the post's
// recorded pre-edit snapshots from the changelog partition of the generic history surface and
// renders each era verbatim from the snapshot payload — the server's text, facets, timestamps
// and change classification; nothing is reconstructed or recomputed client-side. Plain JS.
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
  /// ofMessageId equality clause in the framework's URL-encoded JSON filter DSL. The server's
  /// per-row gate still applies, so a viewer without authority receives an honest empty array.
  function historyPath(messageId) {
    return '/api/history/messages?set=changelog&size=' + FETCH_SIZE
      + '&filter=' + encodeURIComponent(JSON.stringify({ ofMessageId: messageId }));
  }

  async function readSnapshots(messageId) {
    const response = await fetch(historyPath(messageId),
      { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
    if (!response.ok) return null;
    const rows = await response.json().catch(() => null);
    return Array.isArray(rows) ? rows : null;
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
    return handle ? '@' + handle.replace(/^@/, '') : did;
  }

  /// Compact change chips from the snapshot's own ChangeClass: mention and group deltas
  /// (a retarget appears as remove+add because the diff is over occurrences), surface
  /// distance as a percentage, and the semantic distance only when the axis carried a
  /// number — with the classifier id in the title. Every value comes from the payload.
  function chipHolder(changeClass, context) {
    const holder = element('p', 'history-chips', '');
    const delta = changeClass && typeof changeClass === 'object' ? changeClass.facetDelta : null;
    if (delta && typeof delta === 'object') {
      for (const did of Array.isArray(delta.mentionsRemoved) ? delta.mentionsRemoved : [])
        holder.append(element('span', 'history-chip', '\u2212' + authorLabel(did, context)));
      for (const did of Array.isArray(delta.mentionsAdded) ? delta.mentionsAdded : [])
        holder.append(element('span', 'history-chip', '+' + authorLabel(did, context)));
      for (const name of Array.isArray(delta.groupsRemoved) ? delta.groupsRemoved : [])
        holder.append(element('span', 'history-chip', '\u2212@' + name));
      for (const name of Array.isArray(delta.groupsAdded) ? delta.groupsAdded : [])
        holder.append(element('span', 'history-chip', '+@' + name));
    }
    const surface = typeof changeClass?.surfaceDistance === 'number' && Number.isFinite(changeClass.surfaceDistance)
      ? Math.round(changeClass.surfaceDistance * 100) + '%' : null;
    if (surface !== null) holder.append(element('span', 'history-chip', 'surface ' + surface));
    const semantic = typeof changeClass?.semanticDistance === 'number' && Number.isFinite(changeClass.semanticDistance)
      ? changeClass.semanticDistance : null;
    if (semantic !== null) {
      const chip = element('span', 'history-chip', 'semantic ' + semantic);
      if (typeof changeClass.classifier === 'string' && changeClass.classifier) chip.title = 'Classified by ' + changeClass.classifier;
      holder.append(chip);
    }
    return holder.childElementCount ? holder : null;
  }

  /// One era: verbatim snapshot text — facet decoration only through the shared renderer,
  /// which validates every range against this era's own text and degrades to plain text
  /// otherwise — plus the author label, the era's own timestamp, and the change chips.
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
    const chips = chipHolder(row.changeClass, context);
    if (chips) era.append(chips);
    return era;
  }

  async function fill(details, message, context) {
    const body = element('div', 'history-body', '');
    body.append(element('p', 'hint', 'Loading recorded revisions\u2026'));
    details.append(body);
    const rows = await readSnapshots(message.id).catch(() => null);
    if (!details.isConnected) return;
    body.replaceChildren();
    // An unreachable surface and an honestly empty or gated response read the same way:
    // no recorded revisions, never an error tone.
    const eras = rows ? orderedEras(rows, message.changeId) : [];
    if (!eras.length) {
      body.append(element('p', 'hint', 'No recorded revisions.'));
      return;
    }
    for (const row of eras.slice(0, MAX_ERAS)) body.append(eraNode(row, context));
    if (eras.length > MAX_ERAS || rows.length >= FETCH_SIZE)
      body.append(element('p', 'hint', 'Older revisions omitted.'));
  }

  /// The disclosure rooms.js wires under an edited post: closed until opened, keyboard
  /// accessible as a native summary, and lazy — the scoped fetch runs on first open only.
  /// Opening and closing never touches the composer or its draft.
  function disclosure(message, context) {
    if (!message || typeof message.id !== 'string') return null;
    const details = document.createElement('details');
    details.className = 'source-details history-details';
    const summary = element('summary', '', 'Edited \u00b7 view history');
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
