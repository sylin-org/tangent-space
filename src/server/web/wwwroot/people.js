'use strict';
// Who is on this server. Nothing could answer that before: every listing took the ids it
// already held, and the only source of ids was a role's member array, so a participant who
// held no role was invisible although their arrival was recorded (N-062). The owner needs to
// see someone exists before they can decide what to do about them.
(() => {
  const $ = id => document.getElementById(id);
  let page = 1, loading = false, loaded = false;

  function when(value) {
    if (!value) return '';
    const at = new Date(value);
    return Number.isNaN(at.getTime()) ? '' : at.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }

  function card(person) {
    const row = document.createElement('div');
    row.className = 'person-row';
    const mark = document.createElement('span');
    mark.className = 'person-mark';
    if (person.avatar) {
      const image = document.createElement('img');
      image.src = person.avatar; image.alt = ''; image.loading = 'lazy';
      mark.append(image);
    } else mark.textContent = (person.label || '?').slice(0, 1).toUpperCase();
    const body = document.createElement('div');
    body.className = 'person-body';
    const name = document.createElement('strong');
    name.textContent = person.label || 'Participant';
    const handle = document.createElement('span');
    handle.className = 'person-handle';
    handle.textContent = person.handle ? '@' + person.handle : person.did || '';
    body.append(name, handle);
    const marks = document.createElement('div');
    marks.className = 'person-marks';
    // A role is a grant; the rest are facts about the account. Showing "no roles" plainly is
    // the point of this list, so an unroled arrival reads as something to act on.
    for (const role of person.roles?.length ? person.roles : ['no roles']) {
      const chip = document.createElement('span');
      chip.className = 'person-chip' + (person.roles?.length ? '' : ' person-chip-empty');
      chip.textContent = role;
      marks.append(chip);
    }
    if (person.classification && person.classification !== 'Human') {
      const chip = document.createElement('span');
      chip.className = 'person-chip person-chip-quiet';
      chip.textContent = person.classification.toLowerCase();
      marks.append(chip);
    }
    if (person.isSuspended) {
      const chip = document.createElement('span');
      chip.className = 'person-chip person-chip-danger';
      chip.textContent = 'suspended';
      marks.append(chip);
    }
    const arrived = document.createElement('span');
    arrived.className = 'person-arrived';
    arrived.textContent = when(person.joinedAt);
    arrived.title = 'Arrived ' + (person.joinedAt || '');
    row.append(mark, body, marks, arrived);
    return row;
  }

  async function load(next = false) {
    if (loading || (loaded && !next)) return;
    loading = true;
    if (!next) { page = 1; $('people-list').replaceChildren(); }
    $('people-status').textContent = 'Loading people…';
    try {
      const response = await fetch('/api/people?page=' + page, { credentials: 'same-origin' });
      if (response.status === 403) { $('people-status').textContent = 'Only the server owner can see this list.'; return; }
      if (!response.ok) throw new Error('That list could not be loaded.');
      const result = await response.json();
      for (const person of result.people || []) $('people-list').append(card(person));
      const total = typeof result.total === 'number' ? result.total : ($('people-list').childElementCount);
      $('people-status').textContent = total === 1 ? '1 person has arrived here.' : total + ' people have arrived here.';
      $('people-more').hidden = !result.nextPage;
      if (result.nextPage) page = result.nextPage;
      loaded = true;
    } catch (error) {
      $('people-status').textContent = error.message;
    } finally { loading = false; }
  }

  $('people-more')?.addEventListener('click', () => load(true));
  window.TangentPeople = { load: () => load(false), reload: () => { loaded = false; return load(false); } };
})();
