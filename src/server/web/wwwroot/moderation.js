'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const apiRoot = '/api/v1/experience';
  let scope, topicContext, scopeSignature = '', generation = 0, nextPage, selected, pendingPreview;
  let reportTarget, reportRequestId, reportTrigger;

  function allowed(subject, action) {
    const actions = subject?.permissions?.allowedActions || subject?.allowedActions;
    return Array.isArray(actions) && actions.includes(action);
  }

  function element(tag, className, value) {
    const node = document.createElement(tag); node.className = className;
    if (value !== undefined) node.textContent = value;
    return node;
  }

  function announce(message, error = false) {
    const node = $('action-status');
    if (!node) return;
    node.textContent = message || ''; node.hidden = !message; node.classList.toggle('error', error);
  }

  function status(message, error = false) {
    const node = $('stewardship-status');
    node.textContent = message || ''; node.classList.toggle('error', error);
  }

  async function request(path, body, method = 'GET', whole = false) {
    const response = await fetch(path, {
      method, credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body)
    });
    const value = await response.json().catch(() => ({}));
    const problem = value?.result?.problem || value?.problem;
    if (!response.ok || value?.status === 'blocked' || problem) {
      const error = new Error(problem?.message || value?.message || value?.title
        || (response.status === 403 ? 'Your current role no longer permits this.' : 'This request could not be completed.'));
      error.status = response.status; error.code = problem?.code || value?.code;
      throw error;
    }
    return whole ? value : value?.result?.data;
  }

  function caseId(reference) {
    if (!scope || typeof reference !== 'string') return null;
    const prefix = `${scope.serverRef}::${scope.tangentKey}::${scope.key}::case_`;
    const value = reference.startsWith(prefix) ? reference.slice(prefix.length) : '';
    return /^[a-f0-9]{64}$/.test(value) ? value : null;
  }

  function postId(reference) {
    if (!scope || typeof reference !== 'string') return null;
    const prefix = `${scope.serverRef}::${scope.tangentKey}::${scope.key}::`;
    const value = reference.startsWith(prefix) ? reference.slice(prefix.length) : '';
    return /^[A-Za-z0-9-]{1,64}$/.test(value) ? value : null;
  }

  function relativeTime(value) {
    const then = Date.parse(value), difference = Date.now() - then;
    if (!Number.isFinite(then)) return 'Recently reported';
    const minutes = Math.max(0, Math.floor(difference / 60000));
    if (minutes < 1) return 'Just reported';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    return `${Math.floor(hours / 24)}d ago`;
  }

  function closePanel() {
    const wasOpen = document.body.classList.contains('steward-panel-open');
    document.body.classList.remove('steward-panel-open');
    $('stewardship-open')?.setAttribute?.('aria-expanded', 'false');
    if (wasOpen) $('stewardship-open')?.focus?.();
  }

  function openPanel() {
    if (!scope) return;
    document.body.classList.add('steward-panel-open');
    $('stewardship-open')?.setAttribute?.('aria-expanded', 'true');
    $('stewardship-title')?.focus?.({ preventScroll: true });
    $('stewardship-panel')?.scrollIntoView?.({ block: 'start' });
  }

  function clearPanel() {
    nextPage = undefined; selected = pendingPreview = undefined;
    $('moderation-cases').replaceChildren();
    $('moderation-cases').hidden = false;
    $('moderation-detail-content').replaceChildren();
    $('moderation-detail').hidden = true;
    $('moderation-empty').hidden = true;
    $('moderation-more').hidden = true;
    $('moderation-decision').hidden = true;
    $('moderation-preview-card').hidden = true;
    status('');
  }

  async function topic(value, participant) {
    const signature = value ? `${participant?.participantRef || ''}:${value.tangentKey}:${value.key}:${value.policyRevision ?? ''}:${value.sitePolicyRevision ?? ''}` : '';
    if (signature === scopeSignature) return;
    scopeSignature = signature; generation++;
    const version = generation;
    scope = topicContext = undefined;
    document.body.dataset.stewardship = 'false';
    $('stewardship-panel').hidden = true;
    $('stewardship-open').hidden = true;
    closeReport(true); closePanel(); clearPanel();
    if (!value?.key || !value?.tangentKey) return;
    try {
      const envelope = await request(`${apiRoot}/topics/${encodeURIComponent(value.key)}?limit=1`, undefined, 'GET', true);
      if (version !== generation || signature !== scopeSignature) return;
      const serverRef = envelope?.place?.serverRef;
      const actions = envelope?.place?.allowedActions;
      let canonical;
      try { canonical = new URL(serverRef); } catch (_) { canonical = undefined; }
      if (!canonical || !['http:', 'https:'].includes(canonical.protocol) || canonical.origin !== serverRef
          || !Array.isArray(actions)) throw new Error('The Topic response did not include a canonical server reference.');
      topicContext = { ...value, serverRef };
      const steward = envelope?.capabilities?.stewardship === true && actions.includes('list_moderation_cases');
      if (!steward) return;
      scope = topicContext;
      document.body.dataset.stewardship = 'true';
      $('stewardship-panel').hidden = false;
      $('stewardship-open').hidden = false;
      await loadCases(false);
    } catch (error) {
      if (version !== generation) return;
      topicContext = undefined;
      scopeSignature = '';
      if (error.status !== 401 && error.status !== 403) announce(error.message, true);
    }
  }

  function renderCase(item) {
    const id = caseId(item?.caseRef);
    if (!id) return null;
    const row = element('li', 'moderation-case-row');
    const button = element('button', 'moderation-case-button'); button.type = 'button';
    const top = element('span', 'moderation-case-top');
    top.append(element('span', 'moderation-case-state', item.state || 'open'),
      element('span', 'moderation-case-time', relativeTime(item.firstReportedAt)));
    const count = Number.isInteger(item.testimonyCount) ? item.testimonyCount : 0;
    button.append(top, element('span', 'moderation-case-count', `${count} participant report${count === 1 ? '' : 's'}`));
    button.addEventListener('click', () => openCase(item.caseRef, 0));
    row.append(button); return row;
  }

  async function loadCases(append = false) {
    if (!scope) return;
    const version = generation, current = scope, page = append ? nextPage : 1;
    if (!page) return;
    const button = append ? $('moderation-more') : $('stewardship-refresh');
    button.disabled = true; status(append ? 'Loading more…' : 'Checking…');
    try {
      const data = await request(`${apiRoot}/topics/${encodeURIComponent(current.key)}/moderation/cases?page=${page}`);
      if (version !== generation || current !== scope) return;
      if (!data || !Array.isArray(data.cases)) throw new Error('The case queue response was incomplete.');
      if (!append) $('moderation-cases').replaceChildren();
      for (const item of data.cases) { const row = renderCase(item); if (row) $('moderation-cases').append(row); }
      nextPage = data.nextPage;
      $('moderation-more').hidden = !nextPage;
      $('moderation-empty').hidden = $('moderation-cases').children.length > 0;
      status(data.saturated ? 'Some cases reached their bounded history.'
        : data.atLeast ? 'More cases are available; showing a bounded window.' : 'Current');
    } catch (error) {
      if (version !== generation) return;
      status(error.message, true);
      if (error.status === 403) topic(undefined);
    } finally {
      if (version === generation) button.disabled = false;
    }
  }

  function metric(label, value) {
    const node = element('div', 'moderation-metric');
    node.append(element('strong', '', String(value)), element('span', '', label)); return node;
  }

  function renderDetail(data) {
    selected = data; pendingPreview = undefined;
    const item = data.case || {}, content = $('moderation-detail-content'); content.replaceChildren();
    const heading = element('h3', '', item.state === 'escalated' ? 'With the human Host' : 'A reported post');
    heading.id = 'moderation-case-heading'; heading.tabIndex = -1; content.append(heading);
    const summary = element('p', 'moderation-case-summary');
    const subject = postId(item.subjectPostRef);
    if (subject) {
      const link = element('a', '', 'Open the reported post'); link.href = `/t/${encodeURIComponent(scope.tangentKey)}/${encodeURIComponent(subject)}`;
      summary.append(link, document.createTextNode ? document.createTextNode(' · ') : element('span', '', ' · '));
    }
    summary.append(element('span', '', `Case revision ${item.revision ?? '—'} · Post revision ${String(item.subjectRevision || 'unavailable').slice(0, 12)}…`));
    content.append(summary);
    const metrics = element('div', 'moderation-metrics');
    metrics.append(metric('participant reports', item.testimonyCount ?? 0), metric('state', item.state || 'open')); content.append(metrics);

    content.append(element('h4', 'moderation-section-title', 'Participant testimony'));
    const testimonies = element('div', 'moderation-testimonies');
    for (const testimony of data.testimonies || []) {
      const quote = element('blockquote', 'moderation-testimony');
      quote.append(element('p', '', testimony.statement || ''),
        element('footer', '', `${testimony.reasonCode || 'report'} · ${relativeTime(testimony.submittedAt)}`)); testimonies.append(quote);
    }
    if (!testimonies.children.length) testimonies.append(element('p', 'moderation-terminal', 'No testimony is available in this page.'));
    content.append(testimonies);
    const nav = element('div', 'moderation-testimony-nav');
    const offset = Number.isInteger(data.testimonyOffset) ? data.testimonyOffset : 0;
    const previous = element('button', 'btn btn-quiet', 'Previous reports'); previous.type = 'button'; previous.disabled = offset === 0;
    previous.addEventListener('click', () => openCase(item.caseRef, Math.max(0, offset - 8)));
    const next = element('button', 'btn btn-quiet', 'More reports'); next.type = 'button'; next.disabled = data.nextTestimonyOffset == null;
    next.addEventListener('click', () => openCase(item.caseRef, data.nextTestimonyOffset));
    const shown = (data.testimonies || []).length;
    nav.append(previous, element('span', 'hint', shown ? `Showing ${offset + 1}–${offset + shown}` : 'No reports in this page'), next); content.append(nav);

    if (Array.isArray(data.decisions) && data.decisions.length) {
      content.append(element('h4', 'moderation-section-title', 'Recent decisions'));
      const decisions = element('div', 'moderation-decisions');
      for (const decision of data.decisions) {
        const row = element('div', 'moderation-decision-row');
        row.append(element('p', '', decision.reason || decision.action || 'Decision'),
          element('footer', '', `${decision.action || 'decision'} · ${relativeTime(decision.appliedAt)}`)); decisions.append(row);
      }
      content.append(decisions);
      if (data.decisionsTruncated === true)
        content.append(element('p', 'moderation-terminal', 'Older decisions are retained but omitted from this bounded view.'));
    }
    const actions = Array.isArray(item.allowedActions) ? item.allowedActions : [];
    const actionable = actions.includes('preview_moderation_action') && actions.includes('apply_moderation_action') && item.subjectAvailable !== false;
    $('moderation-decision').hidden = !actionable;
    $('moderation-preview-card').hidden = true;
    if (actionable) resetDecisionForm();
    else content.append(element('p', 'moderation-terminal', item.state === 'escalated'
      ? 'The accountable human Host now owns the next step.' : 'This case cannot be changed from your current role or Post revision.'));
  }

  async function openCase(reference, offset = 0) {
    const id = caseId(reference); if (!id || !scope) return;
    const version = generation; status('Opening case…');
    try {
      const data = await request(`${apiRoot}/moderation/cases/${id}?testimonyOffset=${encodeURIComponent(offset)}&testimonyLimit=8&decisionLimit=8`);
      if (version !== generation || !scope) return;
      if (!data?.case || !Array.isArray(data.testimonies)) throw new Error('The case response was incomplete.');
      $('moderation-cases').hidden = $('moderation-empty').hidden = $('moderation-more').hidden = true;
      $('moderation-detail').hidden = false; renderDetail(data); status('');
      $('moderation-case-heading')?.focus?.({ preventScroll: true });
    } catch (error) {
      if (version !== generation) return;
      status(error.message, true);
      if (error.status === 403) topic(undefined);
    }
  }

  function showCaseList() {
    selected = pendingPreview = undefined;
    $('moderation-detail').hidden = true; $('moderation-cases').hidden = false;
    $('moderation-empty').hidden = $('moderation-cases').children.length > 0;
    $('moderation-more').hidden = !nextPage;
    $('stewardship-refresh').focus();
  }

  function localDateTime(date) {
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
    return local.toISOString().slice(0, 16);
  }

  function resetDecisionForm() {
    const form = $('moderation-decision'), now = new Date(), input = form.elements.namedItem('deferredUntil');
    form.elements.namedItem('action').value = 'defer'; form.elements.namedItem('summary').value = '';
    input.min = localDateTime(new Date(now.getTime() + 60000));
    input.max = localDateTime(new Date(now.getTime() + 7 * 86400000));
    input.value = localDateTime(new Date(now.getTime() + 86400000)); input.required = true; input.disabled = false;
    $('moderation-defer-field').hidden = false; $('moderation-preview-card').hidden = true;
  }

  function decisionBody() {
    const form = $('moderation-decision'), action = form.elements.namedItem('action').value;
    const summary = form.elements.namedItem('summary').value.trim();
    if (!summary) throw new Error('Add a short decision note.');
    const body = { action, summary, expectedCaseRevision: selected.case.revision,
      expectedSubjectRevision: selected.case.subjectRevision };
    if (action === 'defer') {
      const value = form.elements.namedItem('deferredUntil').value;
      if (!value || !Number.isFinite(Date.parse(value))) throw new Error('Choose when this case should return.');
      body.deferredUntil = new Date(value).toISOString();
    }
    return body;
  }

  async function previewDecision(event) {
    event.preventDefault(); if (!selected || !scope) return;
    const id = caseId(selected.case.caseRef), button = $('moderation-preview');
    try {
      const body = decisionBody(); button.disabled = true; status('Preparing preview…');
      const data = await request(`${apiRoot}/moderation/cases/${id}/previews`, body, 'POST');
      if (!data?.case || data.case.revision !== selected.case.revision) throw new Error('The preview did not match the open case.');
      pendingPreview = { body, requestId: undefined };
      $('moderation-preview-effect').textContent = data.effect || 'Review this decision before applying it.';
      $('moderation-preview-card').hidden = false; $('moderation-decision').hidden = true; status('Preview ready');
      $('moderation-apply').focus();
    } catch (error) {
      status(error.message, true);
      if (error.status === 409) await openCase(selected.case.caseRef, selected.testimonyOffset || 0);
      if (error.status === 403) topic(undefined);
    } finally { button.disabled = false; }
  }

  async function applyDecision() {
    if (!pendingPreview || !selected || !scope) return;
    const id = caseId(selected.case.caseRef), button = $('moderation-apply');
    pendingPreview.requestId ||= crypto.randomUUID();
    button.disabled = true; button.textContent = 'Applying…'; status('Applying decision…');
    try {
      const data = await request(`${apiRoot}/moderation/cases/${id}/actions`,
        { ...pendingPreview.body, requestId: pendingPreview.requestId }, 'POST');
      if (!data?.case) throw new Error('The saved decision response was incomplete.');
      announce(data.case.state === 'escalated' ? 'Case escalated to the human Host.' : 'Case deferred for later review.');
      renderDetail(data); await loadCases(false); status('Decision saved');
    } catch (error) {
      button.textContent = 'Retry apply'; status(error.message, true);
      if (error.status === 409) await openCase(selected.case.caseRef, selected.testimonyOffset || 0);
      if (error.status === 403) topic(undefined);
    } finally { button.disabled = false; if (button.textContent === 'Applying…') button.textContent = 'Apply decision'; }
  }

  function openReport(message, context) {
    reportTarget = { message, room: context?.room, authorName: context?.authorName };
    // The caller supplies the current Topic even when this participant is not a steward.
    if (!reportTarget.room?.key || !reportTarget.room?.tangentKey) return;
    reportRequestId = crypto.randomUUID(); reportTrigger = context?.trigger;
    if (context?.menu) context.menu.open = false;
    $('report-context').textContent = `Post by ${context?.authorName || 'this participant'}`;
    $('report-form').elements.namedItem('statement').value = '';
    $('report-form').elements.namedItem('reasonCode').value = 'conduct';
    $('report-status').textContent = ''; $('report-submit').textContent = 'Send report'; $('report-submit').disabled = false;
    const panel = $('report-panel'); panel.hidden = false; panel.scrollIntoView({ block: 'nearest' });
    $('report-form').elements.namedItem('statement').focus();
  }

  function closeReport(silent = false) {
    $('report-panel').hidden = true;
    if (!silent) $('report-status').textContent = '';
    $('report-form').elements.namedItem('statement').value = '';
    reportTarget = undefined; reportRequestId = undefined;
    const trigger = reportTrigger; reportTrigger = undefined;
    if (!silent) trigger?.focus?.();
  }

  function postActions(container, message, context) {
    if (!container || !message || !allowed(message, 'reportPost')) return;
    const button = element('button', 'btn btn-quiet', 'Report post'); button.type = 'button';
    button.addEventListener('click', () => openReport(message, { ...context, trigger: button }));
    container.append(button);
  }

  async function submitReport(event) {
    event.preventDefault(); if (!reportTarget || !reportRequestId) return;
    const form = $('report-form'), button = $('report-submit'), room = reportTarget.room;
    const statement = form.elements.namedItem('statement').value.trim();
    if (!statement) { $('report-status').textContent = 'Describe the concern in your own words.'; return; }
    if (!topicContext || topicContext.key !== room.key || topicContext.tangentKey !== room.tangentKey) {
      $('report-status').textContent = 'The Topic context is still loading. Try again in a moment.'; return;
    }
    const postRef = `${topicContext.serverRef}::${room.tangentKey}::${room.key}::${reportTarget.message.id}`;
    button.disabled = true; button.textContent = 'Sending…'; $('report-status').textContent = 'Sending a private signal…';
    try {
      const data = await request(`${apiRoot}/topics/${encodeURIComponent(room.key)}/reports`, {
        requestId: reportRequestId, postRef, reasonCode: form.elements.namedItem('reasonCode').value, statement
      }, 'POST');
      const message = data?.alreadyReported ? 'You already reported this post; your original report remains in the case.'
        : data?.accepted ? 'Report sent privately to this Topic’s stewards.'
          : data?.caseSaturated ? 'This case reached its report limit, so your report was not added. Contact the human Host if the concern is urgent.'
            : 'Your report was not added. Please try again or contact the human Host.';
      closeReport(true); announce(message);
      if (scope?.key === room.key) await loadCases(false);
    } catch (error) {
      $('report-status').textContent = error.message; button.textContent = 'Retry report';
    } finally { button.disabled = false; }
  }

  $('stewardship-open').addEventListener('click', openPanel);
  $('stewardship-close').addEventListener('click', closePanel);
  $('stewardship-refresh').addEventListener('click', () => loadCases(false));
  $('moderation-more').addEventListener('click', () => loadCases(true));
  $('moderation-back').addEventListener('click', showCaseList);
  $('moderation-decision').addEventListener('submit', previewDecision);
  $('moderation-decision').elements.namedItem('action').addEventListener('change', event => {
    const defer = event.target.value === 'defer'; $('moderation-defer-field').hidden = !defer;
    const input = $('moderation-decision').elements.namedItem('deferredUntil');
    input.required = defer; input.disabled = !defer;
  });
  $('moderation-apply').addEventListener('click', applyDecision);
  $('moderation-preview-cancel').addEventListener('click', () => {
    pendingPreview = undefined; $('moderation-preview-card').hidden = true; $('moderation-decision').hidden = false;
    $('moderation-preview').focus();
  });
  $('report-form').addEventListener('submit', submitReport);
  $('report-close').addEventListener('click', () => closeReport());
  $('report-cancel').addEventListener('click', () => closeReport());
  $('report-panel').addEventListener('keydown', event => { if (event.key === 'Escape') closeReport(); });
  window.TangentModeration = { topic, postActions };
})();
