const workspaceKinds = new Set(['home', 'tangents', 'topics', 'topic', 'post', 'participant']);

function decodeSegment(value) {
  try {
    const decoded = decodeURIComponent(value);
    return !decoded || /[\\/\u0000-\u001f\u007f]/.test(decoded) ? null : decoded;
  } catch { return null; }
}

export function parseWorkspaceRoute(pathname) {
  const raw = pathname.split('/').filter(Boolean);
  const parts = raw.map(decodeSegment);
  if (parts.some(value => value == null)) return null;
  if (!parts.length) return { kind: 'home' };
  if (parts.length === 1 && parts[0] === 'tangents') return { kind: 'tangents' };
  if (parts.length === 2 && parts[0] === 'u') return { kind: 'participant', identifier: parts[1] };
  if (parts[0] !== 't' || !parts[1]) return null;
  if (parts[2] === 'topics') {
    if (parts.length === 3) return { kind: 'topics', tangent: parts[1] };
    if (parts.length === 4) return { kind: 'topic', tangent: parts[1], topic: parts[3] };
    return null;
  }
  return parts.length === 3 ? { kind: 'post', tangent: parts[1], post: parts[2] } : null;
}

export function workspaceDestination(href, origin) {
  let destination;
  try { destination = new URL(href, origin); } catch { return null; }
  return destination.origin === origin && parseWorkspaceRoute(destination.pathname) ? destination : null;
}

export function createWorkspaceRouter({ window, document }) {
  const route = window.TangentPages?.route;
  if (!route) return null;
  let pendingScroll = null, actor = null, scrollFrame = null;
  const currentHref = () => window.location.pathname + window.location.search + window.location.hash;
  const captureScroll = () => {
    const position = [window.scrollX || 0, window.scrollY || 0];
    const messages = [...(document.querySelectorAll?.('.message[id]') || [])];
    const visible = messages.find(node => node.getBoundingClientRect().bottom > 0);
    return visible ? { position, anchor: visible.id, offset: visible.getBoundingClientRect().top } : { position };
  };
  const rememberScroll = () => window.history.replaceState({
    ...(window.history.state || {}), tangentRoute: currentHref(), tangentActor: actor, tangentScroll: captureScroll()
  }, '');
  const scheduleScroll = () => {
    if (scrollFrame != null) return;
    scrollFrame = window.requestAnimationFrame(() => { scrollFrame = null; rememberScroll(); });
  };
  const apply = destination => {
    const next = parseWorkspaceRoute(destination.pathname);
    if (!next || !workspaceKinds.has(next.kind)) return false;
    for (const key of Object.keys(route)) delete route[key];
    Object.assign(route, next);
    document.body.dataset.page = next.kind;
    window.dispatchEvent(new window.CustomEvent('tangent:navigate', { detail: { href: currentHref() } }));
    return true;
  };
  const navigate = (href, { replace = false } = {}) => {
    const destination = workspaceDestination(href, window.location.origin);
    if (!destination) return false;
    const nextHref = destination.pathname + destination.search + destination.hash;
    if (nextHref === currentHref()) return true;
    rememberScroll();
    pendingScroll = { position: [0, 0] };
    window.history[replace ? 'replaceState' : 'pushState']({
      tangentRoute: nextHref, tangentActor: actor, tangentScroll: pendingScroll
    }, '', nextHref);
    return apply(destination);
  };
  const setActor = value => {
    const next = value || null;
    if (actor !== next) { actor = next; pendingScroll = null; }
    rememberScroll();
  };
  const canonicalize = href => {
    const destination = workspaceDestination(href, window.location.origin);
    const next = destination && parseWorkspaceRoute(destination.pathname);
    if (!next || next.kind !== route.kind) return false;
    const nextHref = destination.pathname + destination.search + destination.hash;
    for (const key of Object.keys(route)) delete route[key];
    Object.assign(route, next);
    window.history.replaceState({ ...(window.history.state || {}), tangentRoute: nextHref }, '', nextHref);
    return true;
  };
  document.addEventListener('click', event => {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const anchor = event.target.closest?.('a[href]');
    if (!anchor || anchor.target || anchor.hasAttribute('download')) return;
    const destination = workspaceDestination(anchor.href, window.location.origin);
    if (!destination || (destination.pathname === window.location.pathname && destination.search === window.location.search && destination.hash !== window.location.hash)) return;
    event.preventDefault();
    navigate(destination.href);
  });
  window.addEventListener('popstate', event => {
    const destination = workspaceDestination(window.location.href, window.location.origin);
    if (!destination) return;
    const compatible = !event.state?.tangentActor || !actor || event.state.tangentActor === actor;
    pendingScroll = compatible ? event.state?.tangentScroll || { position: [0, 0] } : { position: [0, 0] };
    apply(destination);
  });
  window.addEventListener('tangent:route-ready', event => {
    if (!pendingScroll || event.detail?.href !== currentHref()) return;
    const target = pendingScroll; pendingScroll = null;
    window.requestAnimationFrame(() => {
      const anchor = target.anchor && document.getElementById?.(target.anchor);
      if (anchor) {
        const offset = Number.isFinite(target.offset) ? target.offset : 0;
        window.scrollTo(0, (window.scrollY || 0) + anchor.getBoundingClientRect().top - offset);
      } else {
        const position = Array.isArray(target.position) ? target.position : [0, 0];
        window.scrollTo(position[0], position[1]);
      }
    });
  });
  window.addEventListener('scroll', scheduleScroll, { passive: true });
  window.addEventListener('focusin', scheduleScroll);
  window.addEventListener('tangent:welcome', event => setActor(event.detail?.participant?.participantRef));
  if ('scrollRestoration' in window.history) window.history.scrollRestoration = 'manual';
  rememberScroll();
  return { navigate, setActor, canonicalize };
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  const install = () => { window.TangentNavigation ||= createWorkspaceRouter({ window, document }); };
  if (window.TangentPages) install(); else document.addEventListener('DOMContentLoaded', install, { once: true });
}
