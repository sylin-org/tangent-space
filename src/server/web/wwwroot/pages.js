'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const parts = location.pathname.split('/').filter(Boolean).map(value => { try { return decodeURIComponent(value); } catch (_) { return ''; } });
  const route = { kind: 'home' };
  if (['onboarding', 'sign-in', 'tangents', 'settings'].includes(parts[0])) route.kind = parts[0];
  else if (parts[0] === 'u' && parts[1]) { route.kind = 'participant'; route.identifier = parts[1]; }
  else if (parts[0] === 't' && parts[1]) {
    route.tangent = parts[1];
    if (parts[2] === 'settings') route.kind = 'tangent-settings';
    else if (parts[2] === 'topics') {
      route.topic = parts[3];
      route.kind = parts[4] === 'settings' ? 'topic-settings' : parts[3] ? 'topic' : 'topics';
    }
    else { route.kind = 'post'; route.post = parts[2]; }
  }
  document.body.dataset.page = route.kind;
  const signInUrl = () => '/sign-in/?return=' + encodeURIComponent(location.pathname + location.search + location.hash);
  $('nav-sign-in').href = signInUrl();
  const tangentUrl = key => '/t/' + encodeURIComponent(key) + '/topics';
  const topicUrl = (key, topic) => tangentUrl(key) + '/' + encodeURIComponent(topic);
  const link = (title, href, className = '') => { const a = document.createElement('a'); a.textContent = title; a.href = href; a.className = className; return a; };
  const control = (title, details, focus) => {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-quiet'; button.textContent = title;
    button.onclick = () => {
      for (const id of details) { $(id).hidden = false; $(id).open = true; }
      const target = $(focus); target.scrollIntoView({ behavior: 'smooth', block: 'center' });
      (target.querySelector('input,textarea,button') || target).focus({ preventScroll: true });
    };
    return button;
  };
  let currentSite, currentTangent, currentTopic;
  function hero(site, tangent, topic) {
    currentSite = site;
    if (['settings', 'tangent-settings', 'topic-settings'].includes(route.kind)) { $('server-welcome').hidden = true; $('place').hidden = true; return; }
    if (route.kind === 'participant') { $('server-welcome').hidden = true; $('server-settings-toggle').hidden = true; return; }
    if (tangent) currentTangent = tangent;
    if (topic) currentTopic = topic;
    tangent = currentTangent; topic = currentTopic;
    const server = site.server || {};
    const nested = ['topics', 'topic', 'post'].includes(route.kind);
    const reading = ['topic', 'post'].includes(route.kind);
    const title = reading ? topic?.title || 'Opening the conversation…' : nested ? tangent?.name || 'Opening this Tangent…' : route.kind === 'tangents' ? 'Find your next conversation.' : server.name || site.name;
    $('server-welcome-title').textContent = title;
    $('hero-eyebrow').textContent = reading ? 'Topic' : nested ? 'Tangent · Topics' : route.kind === 'tangents' ? 'The Tangent collection' : 'The bulletin board';
    $('server-welcome-message').textContent = reading ? topic?.topic || '' : nested ? tangent?.description || '' : server.welcomeMessage || '';
    const byline = reading ? '' : nested ? tangent?.motto : server.byline;
    $('hero-byline').textContent = byline || ''; $('hero-byline').hidden = !byline;
    $('server-welcome-message').hidden = !$('server-welcome-message').textContent || $('server-welcome-message').textContent.trim() === (byline || '').trim();
    $('server-motd').textContent = server.motd || ''; $('server-motd').hidden = route.kind !== 'home' || !server.motd;
    // A Topic belongs visually to its Tangent. Keep that shared place visible while
    // the compact reading hero supplies the Topic's own title and breadcrumbs.
    const image = nested ? tangent?.artwork || server.coverImageUrl : server.coverImageUrl;
    const valid = typeof image === 'string' && (/^https:\/\//i.test(image) || /^\/(?!\/)/.test(image)) && !/[\u0000-\u001f\\]/.test(image);
    $('hero-image').hidden = !valid;
    if (valid) { $('hero-image').src = image; $('hero-image').onerror = () => { $('hero-image').hidden = true; }; }
    else $('hero-image').removeAttribute('src');
    const crumbs = $('hero-breadcrumbs'); crumbs.replaceChildren();
    if (route.kind !== 'home') crumbs.append(link(server.name || site.name, '/'));
    if (reading && tangent) crumbs.append(link(tangent.name, tangentUrl(tangent.key)));
    if (route.kind === 'post' && topic) crumbs.append(link(topic.title, topicUrl(topic.tangentKey, topic.key)));
    if (route.kind !== 'home') { const last = document.createElement('span'); last.textContent = route.kind === 'post' ? 'Post' : nested ? reading ? topic?.title || 'Topic' : tangent?.name || 'Topics' : 'Tangents'; last.setAttribute('aria-current','page'); crumbs.append(last); }
    crumbs.hidden = !crumbs.children.length;
    const actions = $('hero-actions'); actions.replaceChildren();
    if (route.kind === 'home') {
      if (site.onboarding !== 'complete') actions.append(link(site.participant?.isOwner ? 'Finish setting up' : 'Set up this server', '/onboarding/', 'btn btn-primary'));
    }
    if (!site.participant && site.established) actions.append(link('Sign in to take part', signInUrl(), 'btn btn-primary'));
    if (route.kind === 'post' && topic) actions.append(link('Open conversation', topicUrl(topic.tangentKey, topic.key), 'btn btn-quiet'));
    if (route.kind === 'topics' && tangent?.canCreateTopic) actions.append(control('Create Topic', ['community-settings', 'site-setup'], 'create-room'));
    if (route.kind === 'topics' && tangent?.canManage) actions.append(link('⚙ Tangent settings', '/t/' + encodeURIComponent(tangent.key) + '/settings', 'btn btn-quiet'));
    if (reading && topic?.canManage) actions.append(link('⚙ Topic settings', topicUrl(topic.tangentKey, topic.key) + '/settings', 'btn btn-quiet'));
    $('server-welcome').hidden = false;
    document.title = title + ' · Tangent Space';
  }
  function prepare(site) {
    window.TangentAtmosphere?.configure(site.server, site.participant);
    document.body.dataset.signedIn = String(!!site.participant);
    $('nav-sign-in').href = signInUrl();
    // Route-owned decoration must never survive under the next URL while its
    // policy-checked data is still loading.
    currentTangent = currentTopic = undefined;
    if (!['onboarding', 'sign-in'].includes(route.kind)) document.body.classList.remove('onboarding-active');
    if (route.kind !== 'onboarding') $('onboarding').hidden = true;
    if (route.kind !== 'participant') $('participant-profile').hidden = true;
    const settingsRoute = ['settings', 'tangent-settings', 'topic-settings'].includes(route.kind);
    $('settings-page').hidden = !settingsRoute;
    $('settings-sign-in').hidden = !settingsRoute || !!site.participant;
    $('activity-status').hidden = !site.participant || ['onboarding', 'sign-in'].includes(route.kind);
    const authentication = ['onboarding', 'sign-in'].includes(route.kind);
    $('nav-sign-in').hidden = !!site.participant;
    const account = $('nav-account');
    account.hidden = !site.participant;
    if (site.participant) {
      const person = site.participant;
      account.querySelector('summary').setAttribute('aria-label', 'Account menu for ' + (person.handle || 'you'));
      const name = $('nav-account-name');
      name.textContent = person.displayName || 'Your account';
      name.dataset.profileDid = person.did || '';
      name.dataset.profilePart = 'name';
      name.dataset.profileFallback = 'Your account';
      delete name.dataset.profileVersion;
      $('nav-account-handle').textContent = person.handle ? '@' + person.handle.replace(/^@/, '') : '';
      // The mark decorates from the same stream as the name beside it. Wiring it here rather
      // than off tangent:welcome is what makes it appear during onboarding, where that event
      // is never dispatched.
      const mark = $('nav-account-mark');
      mark.textContent = (person.handle || 'You').slice(0, 1).toUpperCase();
      mark.dataset.profileDid = person.did || '';
      mark.dataset.profilePart = 'avatar';
      mark.dataset.profileFallback = person.handle || 'You';
      delete mark.dataset.profileVersion;
      $('nav-profile').href = '/u/' + encodeURIComponent(person.did || 'tangent:local:' + person.participantRef);
      $('nav-account-role').textContent = person.isOwner ? 'Server owner' : 'Signed in';
      $('nav-sign-out').hidden = !site.signOut;
    }
    $('state-anonymous').hidden = authentication ? !!site.participant : true;
    const requested = new URLSearchParams(location.search).get('return');
    const returnTo = requested && /^\/(?!\/)/.test(requested) && !/[\\\u0000-\u001f]/.test(requested) ? requested : '/';
    $('sign-in-form').elements.namedItem('return').value = route.kind === 'onboarding' ? '/onboarding/' : returnTo;
    if (route.kind === 'sign-in') {
      if (site.participant) { location.replace(returnTo.startsWith('/sign-in') ? '/' : returnTo); return; }
      document.body.classList.add('onboarding-active');
      $('anonymous-title').textContent = 'Welcome back.';
      $('anonymous-copy').textContent = 'Sign in to ' + site.name + ' with your Atmosphere account.';
      $('note-unestablished').hidden = true;
    }
    if (!authentication) hero(site);
  }
  function unavailable(status) {
    currentTangent = currentTopic = undefined;
    const denied = status === 403;
    $('server-welcome-title').textContent = status === 401 ? 'Sign in to continue.'
      : denied ? 'You don’t have access to this.' : 'This conversation isn’t available.';
    $('server-welcome-message').textContent = status === 401 ? 'Use your Atmosphere account to see what’s here for you.'
      : denied ? 'Your account can’t see this content. It may belong to another account, or access to it was removed.' : 'It may have moved, or your account may not have access.';
    $('hero-byline').hidden = $('server-motd').hidden = $('hero-image').hidden = true;
    $('hero-breadcrumbs').replaceChildren(link(currentSite?.name || 'Home', '/'), link('Tangents', '/tangents/'));
    $('hero-actions').replaceChildren(denied ? link('Explore Tangents', '/tangents/', 'btn btn-primary') : link('Back to Tangents', '/tangents/', 'btn btn-quiet'));
    $('hero-actions').append(link(denied ? 'Back to home' : 'Home', '/', 'btn btn-quiet'));
    if (status === 401 || !currentSite?.participant) $('hero-actions').append(link('Sign in', signInUrl(), 'btn btn-primary'));
    document.title = (denied ? 'No access' : 'Conversation unavailable') + ' · Tangent Space';
  }
  window.TangentPages = { route, hero, prepare, tangentUrl, topicUrl, unavailable };
  $('create-tangent-open').addEventListener('click', () => {
    for (const id of ['community-settings', 'tangent-setup']) { $(id).hidden = false; $(id).open = true; }
    $('create-tangent').scrollIntoView({ behavior: 'smooth', block: 'center' });
    $('create-tangent').elements.namedItem('name').focus({ preventScroll: true });
  });
  $('nav-sign-out').addEventListener('click', () => $('sign-out-form').requestSubmit());
  document.addEventListener('click', event => { if (!$('nav-account').contains(event.target)) $('nav-account').open = false; });
  $('nav-account').addEventListener('keydown', event => {
    if (event.key === 'Escape') { $('nav-account').open = false; $('nav-account').querySelector('summary').focus(); }
  });
  for (const [formId, titleField] of [['create-tangent', 'name'], ['create-room', 'title']]) {
    const form = $(formId), title = form.elements.namedItem(titleField), key = form.elements.namedItem('key');
    let automatic = true;
    key.addEventListener('input', () => { automatic = !key.value; });
    title.addEventListener('input', () => {
      if (automatic) key.value = title.value.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 64).replace(/-$/, '');
    });
    form.addEventListener('reset', () => { automatic = true; });
  }
})();
