// One decoration stream for the identities rendered on this page. Never polls atproto.
'use strict';
(() => {
  let stream, selection = '', coordinated = false, scheduled = false, ownDid = '';
  const snapshots = new Map();
  const localAvatar = value => typeof value === 'string' && value.startsWith('/api/profile-cache/avatar?');
  function paint(node, profile) {
    const part = node.dataset.profilePart;
    const version = JSON.stringify([profile.displayName, profile.avatar]);
    if (node.dataset.profileVersion === version) return;
    node.dataset.profileVersion = version;
    if (part === 'name') node.textContent = profile.displayName || node.dataset.profileFallback || 'Participant';
    if (part === 'avatar') {
      if (!localAvatar(profile.avatar)) { node.textContent = (profile.displayName || node.dataset.profileFallback || 'P').slice(0, 1).toUpperCase(); return; }
      const image = new Image(); image.alt = '';
      image.onload = () => { if (node.isConnected && node.dataset.profileDid === profile.did && node.dataset.profileVersion === version) node.replaceChildren(image); };
      image.src = profile.avatar;
    }
  }
  function receive(profile) {
    if (!profile?.did || profile.status !== 'loaded') return;
    snapshots.set(profile.did, profile);
    document.querySelectorAll('[data-profile-did]').forEach(node => { if (node.dataset.profileDid === profile.did) paint(node, profile); });
    window.dispatchEvent(new CustomEvent('tangent:profile', { detail: profile }));
  }
  function reconcile() {
    scheduled = false;
    if (document.hidden) {
      selection = ''; coordinated = false; stream?.close(); stream = null;
      window.TangentActivityCoordinator?.setProfiles([]);
      return;
    }
    const nodes = [...document.querySelectorAll('[data-profile-did]')].filter(node => !node.closest('[hidden]'));
    const dids = [...new Set(nodes.map(node => node.dataset.profileDid).filter(did => did?.startsWith('did:')))].slice(0, 64).sort();
    for (const did of snapshots.keys()) if (!dids.includes(did)) snapshots.delete(did);
    for (const node of nodes) { const saved = snapshots.get(node.dataset.profileDid); if (saved) paint(node, saved); }
    const next = dids.join('|');
    const signedIn = document.body.dataset.signedIn === 'true';
    const viaCoordinator = window.TangentActivityCoordinator?.setProfiles(signedIn ? dids : []) === true;
    if (next === selection && viaCoordinator === coordinated) return;
    selection = next; coordinated = viaCoordinator; stream?.close(); stream = null;
    if (!next || !signedIn) return;
    // The shared activity worker multiplexes profile decoration into its single SSE.
    // Browsers without SharedWorker retain this per-tab compatibility stream.
    if (viaCoordinator) return;
    const query = new URLSearchParams(); dids.forEach(did => query.append('did', did));
    stream = new EventSource('/api/profile-cache/events?' + query);
    stream.addEventListener('profile', event => { try { receive(JSON.parse(event.data)); } catch (_) { } });
  }
  function schedule() { if (!scheduled) { scheduled = true; queueMicrotask(reconcile); } }
  new MutationObserver(schedule).observe(document.body, { subtree: true, childList: true, attributes: true,
    attributeFilter: ['data-profile-did', 'hidden', 'data-signed-in'] });
  window.addEventListener('tangent:welcome', event => {
    const did = event.detail?.participant?.did;
    const mark = document.getElementById('nav-account-mark');
    if (mark) {
      mark.dataset.profileDid = did || ''; mark.dataset.profilePart = 'avatar';
      mark.dataset.profileFallback = event.detail?.participant?.handle || 'You';
      delete mark.dataset.profileVersion;
    }
    if (!did) { ownDid = ''; snapshots.clear(); selection = ''; coordinated = false; stream?.close(); stream = null; }
    if (did && ownDid !== did) {
      ownDid = did;
      fetch('/api/participants/me/profile', { credentials: 'same-origin', cache: 'no-store' })
        .then(response => response.ok ? response.json() : null).then(profile => { if (ownDid === did) receive(profile); }).catch(() => {});
    }
    schedule();
  });
  window.addEventListener('tangent:profile-stream', event => receive(event.detail));
  window.addEventListener('tangent:live-ready', schedule);
  document.addEventListener('visibilitychange', schedule);
  window.addEventListener('pagehide', () => { stream?.close(); stream = null; selection = ''; coordinated = false; window.TangentActivityCoordinator?.setProfiles([]); });
  window.addEventListener('pageshow', schedule);
})();
