'use strict';

(function () {
  var API_PATH = '/api/site';
  var SIGN_IN_PATH = '/auth/atproto/challenge';
  var SIGN_OUT_PATH = '/auth/logout';

  var stateSections = {
    loading: document.getElementById('state-loading'),
    error: document.getElementById('state-error'),
    anonymous: document.getElementById('state-anonymous'),
    signedIn: document.getElementById('state-signed-in')
  };
  var announcer = document.getElementById('announcer');
  var siteNameEl = document.getElementById('site-name');
  var unestablishedNotes = [
    document.getElementById('note-unestablished'),
    document.getElementById('note-unestablished-signed-in')
  ];
  var signInForm = document.getElementById('sign-in-form');
  var identifierInput = document.getElementById('identifier');
  var signOutForm = document.getElementById('sign-out-form');
  var retryButton = document.getElementById('retry');
  var displayNameEl = document.getElementById('display-name');
  var ownerBadge = document.getElementById('owner-badge');
  var didEl = document.getElementById('did-full');
  var joinedRow = document.getElementById('joined-row');
  var joinedEl = document.getElementById('joined');
  var welcomeNameEl = document.getElementById('welcome-name');

  function show(state) {
    Object.keys(stateSections).forEach(function (key) {
      stateSections[key].hidden = key !== state;
    });
  }

  function announce(message) {
    announcer.textContent = message;
  }

  function isAllowedPath(value, allowed) {
    return typeof value === 'string' && value === allowed;
  }

  function compactDid(did) {
    var parts = did.split(':');
    if (parts.length < 3 || parts[0] !== 'did') {
      return did;
    }
    var tail = parts.slice(2).join(':');
    if (tail.length <= 16) {
      return did;
    }
    return parts[0] + ':' + parts[1] + ':' + tail.slice(0, 8) + '\u2026' + tail.slice(-4);
  }

  function formatJoined(value) {
    if (typeof value !== 'string') {
      return null;
    }
    var parsed = new Date(value);
    if (isNaN(parsed.getTime())) {
      return null;
    }
    try {
      return parsed.toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' });
    } catch (error) {
      return parsed.toISOString().slice(0, 10);
    }
  }

  function parseSite(payload) {
    if (!payload || typeof payload !== 'object') {
      return null;
    }
    if (typeof payload.name !== 'string' || payload.name.length === 0) {
      return null;
    }
    if (typeof payload.established !== 'boolean') {
      return null;
    }
    if (!isAllowedPath(payload.signIn, SIGN_IN_PATH)) {
      return null;
    }
    var participant = payload.participant;
    if (participant === undefined || participant === null) {
      return payload;
    }
    if (typeof participant !== 'object') {
      return null;
    }
    if (typeof participant.did !== 'string' || participant.did.length === 0) {
      return null;
    }
    var handle = participant.handle;
    if (handle !== undefined && handle !== null && typeof handle !== 'string') {
      return null;
    }
    if (typeof participant.isOwner !== 'boolean') {
      return null;
    }
    if (typeof participant.joinedAt !== 'string') {
      return null;
    }
    return payload;
  }

  function render(site) {
    siteNameEl.textContent = site.name;
    siteNameEl.hidden = false;
    document.title = site.name + ' \u00b7 Tangent Space';
    unestablishedNotes.forEach(function (note) {
      note.hidden = site.established;
    });

    var participant = site.participant;
    if (!participant) {
      signInForm.action = SIGN_IN_PATH;
      show('anonymous');
      announce('Welcome. Sign in with your AT account to take part.');
      return;
    }

    displayNameEl.textContent = participant.handle && participant.handle.length > 0
      ? participant.handle
      : compactDid(participant.did);
    welcomeNameEl.textContent = participant.handle && participant.handle.length > 0 ? ', ' + participant.handle : '';
    ownerBadge.hidden = !participant.isOwner;
    didEl.textContent = participant.did;

    var joined = formatJoined(participant.joinedAt);
    joinedRow.hidden = joined === null;
    joinedEl.textContent = joined === null ? '' : joined;

    if (isAllowedPath(site.signOut, SIGN_OUT_PATH)) {
      signOutForm.action = SIGN_OUT_PATH;
      signOutForm.hidden = false;
    } else {
      signOutForm.removeAttribute('action');
      signOutForm.hidden = true;
    }

    show('signedIn');
    announce('You are signed in.');
  }

  function load() {
    show('loading');
    announce('Loading the welcome.');
    fetch(API_PATH, {
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
      cache: 'no-store'
    })
      .then(function (response) {
        if (!response.ok) {
          throw new Error('HTTP ' + response.status);
        }
        return response.json();
      })
      .then(function (payload) {
        var site = parseSite(payload);
        if (!site) {
          throw new Error('Unexpected site payload');
        }
        render(site);
        // Tangents are deliberately a separate, viewer-filtered directory.  The
        // welcome remains useful if a very old server has not exposed it yet;
        // the room client never invents a local replacement.
        if (!site.participant) {
          window.dispatchEvent(new CustomEvent('tangent:welcome', { detail: site }));
          return;
        }
        return fetch('/api/tangents', {
          credentials: 'same-origin', headers: { Accept: 'application/json', 'X-Tangent-Participant': site.participant.did }, cache: 'no-store'
        })
          .then(function (response) { return response.ok ? response.json() : null; })
          .catch(function () { return null; })
          .then(function (tangents) {
            if (tangents && typeof tangents === 'object') site.tangents = tangents;
            window.dispatchEvent(new CustomEvent('tangent:welcome', { detail: site }));
          });
      })
      .catch(function () {
        show('error');
        announce('The welcome could not be loaded.');
      });
  }

  retryButton.addEventListener('click', load);

  signInForm.addEventListener('submit', function () {
    identifierInput.value = identifierInput.value.trim();
  });

  load();
})();
