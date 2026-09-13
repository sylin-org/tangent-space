// Facet composer: @-autocomplete with role groups, client-minted byte-range facets,
// and the read-time renderer with fresh labels (ADR 0008). Plain JS, no dependencies.
'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const encoder = new TextEncoder();
  const utf8Size = code => code < 0x80 ? 1 : code < 0x800 ? 2 : code < 0x10000 ? 3 : 4;
  const byteLength = text => encoder.encode(text).length;

  /// Char index where the given UTF-8 byte offset lands.
  function charIndexAtByte(text, byteOffset) {
    let bytes = 0;
    for (let index = 0; index < text.length; ) {
      if (bytes >= byteOffset) return index;
      const code = text.codePointAt(index);
      bytes += utf8Size(code);
      index += code > 0xffff ? 2 : 1;
    }
    return text.length;
  }

  // ----- state -----
  let targets = [];           // current mentionable targets for the open topic
  let targetRoom = null;      // topic key the targets belong to
  let activeIndex = -1;       // highlighted popup row
  let tokenStart = -1;        // char index where the in-progress @token begins
  let composing = false;      // popup open
  let lookupVersion = 0;

  const draftFacets = new Map(); // room key -> [{kind,start,end,did,value,label}]

  function facetsOf(key) { if (!draftFacets.has(key)) draftFacets.set(key, []); return draftFacets.get(key); }

  function wire(host) {
    const textarea = $('message-text');
    if (!textarea) return;
    textarea.addEventListener('input', () => { sizeFacets(textarea); onInput(textarea); });
    textarea.addEventListener('keydown', event => onKeyDown(event, textarea));
    document.addEventListener('click', event => { if (composing && !$('mention-popup')?.contains(event.target) && event.target !== textarea) close(); });
    textarea.addEventListener('blur', () => setTimeout(() => { if (composing && document.activeElement?.closest?.('#mention-popup') == null) close(); }, 120));
  }

  // ----- popup -----
  function popup() {
    let node = $('mention-popup');
    if (!node) {
      node = document.createElement('div');
      node.id = 'mention-popup';
      node.className = 'mention-popup';
      node.setAttribute('role', 'listbox');
      node.setAttribute('aria-label', 'Mention someone');
      $('message-form').appendChild(node);
    }
    return node;
  }

  function onInput(textarea) {
    const before = textarea.value.slice(0, textarea.selectionStart);
    const match = /(^|[\s(>])@([\w.-]*)$/.exec(before);
    if (!match) return close();
    tokenStart = textarea.selectionStart - match[2].length - 1;
    open(textarea, match[2]);
  }

  async function open(textarea, prefix) {
    const roomKey = window.TangentRooms?.currentRoomKey?.();
    if (!roomKey) return close();
    const version = ++lookupVersion;
    let list;
    try { list = await window.TangentRooms.mentionables(roomKey, prefix); }
    catch (_) { if (version === lookupVersion) close(); return; }
    if (version !== lookupVersion || roomKey !== window.TangentRooms?.currentRoomKey?.() || document.activeElement !== textarea) return;
    targets = list || [];
    if (!targets.length) return close();
    composing = true; activeIndex = 0;
    const node = popup();
    node.replaceChildren(...targets.map((entry, index) => {
      const row = document.createElement('div');
      row.className = 'mention-row' + (index === 0 ? ' active' : '');
      row.setAttribute('role', 'option');
      row.id = 'mention-option-' + index;
      row.setAttribute('aria-selected', String(index === 0));
      row.dataset.index = String(index);
      const label = document.createElement('span');
      label.className = 'mention-label';
      label.textContent = (entry.kind === 'group' ? '@' + entry.label : '@' + entry.label);
      const meta = document.createElement('span');
      meta.className = 'mention-meta';
      meta.textContent = entry.kind === 'group'
        ? (entry.count != null ? entry.count + ' member' + (entry.count === 1 ? '' : 's') : 'role group')
        : [entry.badge, entry.classification].filter(Boolean).join(' · ') || 'participant';
      row.append(label, meta);
      row.addEventListener('mousedown', event => event.preventDefault());
      row.addEventListener('click', () => select(index, textarea));
      return row;
    }));
    node.hidden = false;
    textarea.setAttribute('aria-controls', node.id);
    textarea.setAttribute('aria-expanded', 'true');
    textarea.setAttribute('aria-activedescendant', 'mention-option-0');
    position(textarea, node);
  }

  function position(textarea, node) {
    // Anchor below the caret line; measured after content exists.
    requestAnimationFrame(() => {
      const form = $('message-form').getBoundingClientRect();
      const caret = textarea.selectionStart;
      const mirror = document.createElement('div');
      const style = getComputedStyle(textarea);
      for (const key of ['fontFamily', 'fontSize', 'lineHeight', 'padding', 'width', 'whiteSpace', 'wordBreak']) mirror.style[key] = style[key];
      mirror.style.position = 'absolute'; mirror.style.visibility = 'hidden';
      mirror.textContent = textarea.value.slice(0, caret);
      const marker = document.createElement('span'); marker.textContent = '\u200b';
      mirror.appendChild(marker); document.body.appendChild(mirror);
      const left = Math.min(marker.offsetLeft, form.width - node.offsetWidth - 8);
      document.body.removeChild(mirror);
      node.style.left = Math.max(0, left) + 'px';
      node.style.top = Math.min(textarea.offsetTop + textarea.offsetHeight + 4, form.height - node.offsetHeight - 4) + 'px';
    });
  }

  function onKeyDown(event, textarea) {
    if (!composing) return;
    const node = popup();
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      activeIndex = (activeIndex + (event.key === 'ArrowDown' ? 1 : -1) + targets.length) % targets.length;
      node.querySelectorAll('.mention-row').forEach((row, index) => { row.classList.toggle('active', index === activeIndex); row.setAttribute('aria-selected', String(index === activeIndex)); });
      textarea.setAttribute('aria-activedescendant', 'mention-option-' + activeIndex);
      node.querySelectorAll('.mention-row')[activeIndex]?.scrollIntoView({ block: 'nearest' });
    } else if (event.key === 'Enter' || event.key === 'Tab') {
      event.preventDefault();
      select(activeIndex, textarea);
    } else if (event.key === 'Escape') {
      close();
    }
  }

  function select(index, textarea) {
    const entry = targets[index];
    if (!entry) return close();
    const insert = '@' + entry.insert;
    const after = textarea.value.slice(textarea.selectionStart);
    const beforeKeep = textarea.value.slice(0, tokenStart);
    const start = (beforeKeep + insert).length;
    textarea.value = beforeKeep + insert + ' ' + after;
    const caret = start + 1;
    textarea.setSelectionRange(caret, caret);
    const roomKey = window.TangentRooms?.currentRoomKey?.();
    if (roomKey) {
      const byteStart = byteLength(textarea.value.slice(0, tokenStart));
      facetsOf(roomKey).push({
        kind: entry.kind === 'group' ? 'group' : 'mention',
        start: byteStart,
        end: byteStart + byteLength(insert),
        ...(entry.kind === 'group' ? { value: entry.value } : { did: entry.did }),
        label: entry.insert
      });
    }
    close();
    textarea.focus();
    window.TangentRooms?.size();
    sizeFacets(textarea);
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
  }

  function close() { lookupVersion++; composing = false; activeIndex = -1; const node = $('mention-popup'); if (node) node.hidden = true; const input = $('message-text'); input?.setAttribute('aria-expanded', 'false'); input?.removeAttribute('aria-activedescendant'); }

  // ----- facet bookkeeping -----
  /// Drop facets whose range no longer matches their recorded label (edits shift ranges;
  /// a degraded mention simply becomes plain text the server parser may still resolve).
  function sizeFacets(textarea) {
    const roomKey = window.TangentRooms?.currentRoomKey?.();
    if (!roomKey) return;
    const list = facetsOf(roomKey);
    const kept = [];
    for (const facet of list) {
      const from = charIndexAtByte(textarea.value, facet.start);
      const to = charIndexAtByte(textarea.value, facet.end);
      if (textarea.value.slice(from, to) === '@' + facet.label) kept.push(facet);
    }
    draftFacets.set(roomKey, kept);
  }

  /// The wire package for the current draft: validated facets only.
  function wireFacets(roomKey, text) {
    return facetsOf(roomKey || '').filter(facet => {
      const from = charIndexAtByte(text, facet.start);
      const to = charIndexAtByte(text, facet.end);
      return text.slice(from, to) === '@' + facet.label;
    }).map(facet => ({
      kind: facet.kind, start: facet.start, end: facet.end,
      ...(facet.did ? { did: facet.did } : {}), ...(facet.value ? { value: facet.value } : {})
    }));
  }

  function clearFacets(roomKey) { draftFacets.delete(roomKey); }
  function clearAll() { draftFacets.clear(); targets = []; targetRoom = null; close(); }

  /// Insert a reply mention for a known author (Discord dynamics: reply carries the @).
  function replyMention(authorValue, handle) {
    const textarea = $('message-text');
    const roomKey = window.TangentRooms?.currentRoomKey?.();
    if (!textarea || !roomKey || !handle) return;
    const insert = '@' + handle + ' ';
    const byteStart = 0;
    if (textarea.value.startsWith(insert)) return;
    for (const facet of facetsOf(roomKey)) { facet.start += byteLength(insert); facet.end += byteLength(insert); }
    textarea.value = insert + textarea.value;
    textarea.setSelectionRange(insert.length, insert.length);
    facetsOf(roomKey).push({ kind: 'mention', start: byteStart, end: byteStart + byteLength(insert.trim()), did: authorValue, label: handle });
    window.TangentRooms?.size();
    sizeFacets(textarea);
  }

  // ----- renderer -----
  /// Build the message text node: verbatim words with facet ranges decorated.
  /// Mentions show the fresh label from `resolved` and link to the internal profile;
  /// unresolved identities fall back to the raw bytes. Groups/tags are styled spans.
  function renderFacetedText(text, facets, resolved) {
    const paragraph = document.createElement('p');
    paragraph.className = 'message-text';
    if (!Array.isArray(facets) || !facets.length) { paragraph.textContent = text; return paragraph; }
    const ordered = [...facets].sort((left, right) => left.start - right.start);
    let byteCursor = 0;
    for (const facet of ordered) {
      const from = charIndexAtByte(text, byteCursor);
      const start = charIndexAtByte(text, facet.start);
      const end = charIndexAtByte(text, facet.end);
      if (facet.start < byteCursor || end <= start) continue; // overlap or stale: skip
      if (start > from) paragraph.appendChild(document.createTextNode(text.slice(from, start)));
      const segment = text.slice(start, end);
      if (facet.kind === 'mention' && facet.did) {
        const resolution = resolved?.[facet.did];
        const anchor = document.createElement('a');
        anchor.className = 'mention';
        anchor.href = resolution?.profileUrl?.startsWith('/u/') ? resolution.profileUrl : '/u/' + encodeURIComponent(resolution?.value || facet.did);
        anchor.textContent = resolution?.handle ? '@' + resolution.handle.replace(/^@/, '') : resolution?.displayName ? '@' + resolution.displayName : segment;
        if (resolution?.classification) anchor.title = resolution.classification + (resolution.handle ? ' · ' + resolution.handle : '');
        paragraph.appendChild(anchor);
      } else if (facet.kind === 'group') {
        const span = document.createElement('span');
        span.className = 'mention-group';
        span.textContent = segment;
        span.title = 'Role group: resolves to its current holders';
        paragraph.appendChild(span);
      } else if (facet.kind === 'tag') {
        const span = document.createElement('span');
        span.className = 'mention-tag';
        span.textContent = segment;
        paragraph.appendChild(span);
      } else {
        paragraph.appendChild(document.createTextNode(segment));
      }
      byteCursor = facet.end;
    }
    const tail = charIndexAtByte(text, byteCursor);
    if (tail < text.length) paragraph.appendChild(document.createTextNode(text.slice(tail)));
    return paragraph;
  }

  window.TangentFacets = { wire, wireFacets, clearFacets, clearAll, replyMention, renderFacetedText, charIndexAtByte };
  // Deferred scripts run after DOM parsing: the composer exists; wire immediately.
  if (document.getElementById('message-text')) wire(document);
})();
