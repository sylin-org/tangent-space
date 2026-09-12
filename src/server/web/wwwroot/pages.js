'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const parts = location.pathname.split('/').filter(Boolean).map(value => { try { return decodeURIComponent(value); } catch (_) { return ''; } });
  const route = { kind: 'home' };
  if (['onboarding', 'sign-in', 'tangents'].includes(parts[0])) route.kind = parts[0];
  else if (parts[0] === 't' && parts[1]) {
    route.tangent = parts[1];
    if (parts[2] === 'topics') { route.kind = parts[3] ? 'topic' : 'topics'; route.topic = parts[3]; }
    else { route.kind = 'post'; route.post = parts[2]; }
  }
  document.body.dataset.page = route.kind;
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
    if (tangent) currentTangent = tangent;
    if (topic) currentTopic = topic;
    tangent = currentTangent; topic = currentTopic;
    const server = site.server || {};
    const nested = ['topics', 'topic', 'post'].includes(route.kind);
    const reading = ['topic', 'post'].includes(route.kind);
    const title = reading ? topic?.title || 'Opening the conversation…' : nested ? tangent?.name || 'Opening this Tangent…' : route.kind === 'tangents' ? 'Find your next conversation.' : server.name || site.name;
    $('server-welcome-title').textContent = title;
    $('hero-eyebrow').textContent = reading ? 'Topic' : nested ? 'Tangent · Topics' : route.kind === 'tangents' ? 'The Tangent collection' : 'The bulletin board';
    $('server-welcome-message').textContent = reading ? topic?.topic || '' : nested ? tangent?.description || '' : route.kind === 'tangents' ? 'Small worlds. Shared interests. A place to belong.' : server.welcomeMessage || 'Welcome. Make room for the next thought.';
    const byline = nested ? tangent?.motto : server.byline;
    $('hero-byline').textContent = byline || ''; $('hero-byline').hidden = !byline;
    $('server-motd').textContent = server.motd || ''; $('server-motd').hidden = route.kind !== 'home' || !server.motd;
    const image = nested ? tangent?.artwork || server.coverImageUrl : server.coverImageUrl;
    const valid = typeof image === 'string' && (/^https:\/\//i.test(image) || /^\/(?!\/)/.test(image)) && !/[\u0000-\u001f\\]/.test(image);
    $('hero-image').hidden = !valid;
    if (valid) { $('hero-image').src = image; $('hero-image').onerror = () => { $('hero-image').hidden = true; }; }
    else $('hero-image').removeAttribute('src');
    const crumbs = $('hero-breadcrumbs'); crumbs.replaceChildren();
    if (route.kind !== 'home') crumbs.append(link(server.name || site.name, '/'));
    if (nested) crumbs.append(link('Tangents', '/tangents/'));
    if (reading && tangent) crumbs.append(link(tangent.name, tangentUrl(tangent.key)));
    if (route.kind === 'post' && topic) crumbs.append(link(topic.title, topicUrl(topic.tangentKey, topic.key)));
    if (route.kind !== 'home') { const last = document.createElement('span'); last.textContent = route.kind === 'post' ? 'Post' : nested ? reading ? topic?.title || 'Topic' : tangent?.name || 'Topics' : 'Tangents'; last.setAttribute('aria-current','page'); crumbs.append(last); }
    crumbs.hidden = !crumbs.children.length;
    const actions = $('hero-actions'); actions.replaceChildren();
    if (route.kind === 'home') {
      if (site.onboarding !== 'complete') actions.append(link(site.participant?.isOwner ? 'Finish setting up' : 'Set up this server', '/onboarding/', 'btn btn-primary'));
      else actions.append(link('Explore Tangents', '/tangents/', 'btn btn-primary'));
    }
    if (!site.participant && site.established) actions.append(link('Sign in to take part', '/sign-in/?return=' + encodeURIComponent(location.pathname), 'btn btn-quiet'));
    if (route.kind === 'post' && topic) actions.append(link('Open conversation', topicUrl(topic.tangentKey, topic.key), 'btn btn-quiet'));
    if (['home', 'tangents'].includes(route.kind) && site.tangents?.canCreate && site.onboarding === 'complete') actions.append(control('Create Tangent', ['community-settings', 'tangent-setup'], 'create-tangent'));
    if (route.kind === 'topics' && tangent?.canCreateTopic) actions.append(control('Create Topic', ['community-settings', 'site-setup'], 'create-room'));
    if (route.kind === 'topics' && tangent?.canManage) actions.append(control('⚙ Tangent settings', ['community-settings', 'tangent-editor'], 'edit-tangent-form'));
    if (reading && topic?.canManage) actions.append(control('⚙ Topic settings', ['channel-details', 'room-admin'], 'topic-form'));
    $('server-welcome').hidden = false;
    document.title = title + ' · Tangent Space';
  }
  function prepare(site) {
    window.TangentAtmosphere?.configure(site.server, site.participant);
    document.body.dataset.signedIn = String(!!site.participant);
    if (!site.participant) $('activity-status').textContent = 'Sign in for your updates';
    const authentication = ['onboarding', 'sign-in'].includes(route.kind);
    $('nav-sign-in').hidden = !!site.participant;
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
    if (status === 401 || !currentSite?.participant) $('hero-actions').append(link('Sign in', '/sign-in/?return=' + encodeURIComponent(location.pathname), 'btn btn-primary'));
    document.title = (denied ? 'No access' : 'Conversation unavailable') + ' · Tangent Space';
  }
  window.TangentPages = { route, hero, prepare, tangentUrl, topicUrl, unavailable };
})();
