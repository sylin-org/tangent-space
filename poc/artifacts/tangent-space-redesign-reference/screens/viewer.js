/* Reference viewer: theme (system | light | dark) and Space accent, from the URL or the bar. */
(function () {
  var root = document.documentElement;
  var q = new URLSearchParams(location.search);
  var theme = q.get('theme') || root.getAttribute('data-default-theme') || 'system';
  var accent = q.get('accent') || 'gold';
  if (q.get('embed') === '1') root.classList.add('embed');
  function apply() {
    if (theme === 'system') root.removeAttribute('data-theme'); else root.setAttribute('data-theme', theme);
    root.setAttribute('data-accent', accent);
    var bs = document.querySelectorAll('[data-set-theme]');
    for (var i = 0; i < bs.length; i++) bs[i].setAttribute('aria-pressed', String(bs[i].getAttribute('data-set-theme') === theme));
    var sel = document.getElementById('ref-accent');
    if (sel) sel.value = accent;
    var links = document.querySelectorAll('a.ref-link, a[data-keep-view]');
    for (var j = 0; j < links.length; j++) {
      var href = links[j].getAttribute('href').split('?')[0];
      links[j].setAttribute('href', href + '?theme=' + theme + '&accent=' + accent);
    }
  }
  apply();
  document.addEventListener('DOMContentLoaded', function () {
    apply();
    document.addEventListener('click', function (e) {
      var b = e.target.closest ? e.target.closest('[data-set-theme]') : null;
      if (b) { theme = b.getAttribute('data-set-theme'); apply(); }
    });
    document.addEventListener('change', function (e) {
      if (e.target && e.target.id === 'ref-accent') { accent = e.target.value; apply(); }
    });
  });
})();
