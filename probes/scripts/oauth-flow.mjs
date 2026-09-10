// Disposable S01 driver for the pinned reference PDS's real OAuth browser API.
// It exercises password authentication, CSRF/device binding, consent and callback.
// It does not test browser rendering and must never be used with real accounts.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { parseArgs } from 'node:util';
import { resolve, dirname, sep } from 'node:path';
import { request as httpRequest } from 'node:http';
import { request as httpsRequest } from 'node:https';

const { values } = parseArgs({
  options: {
    accounts: { type: 'string' },
    account: { type: 'string' },
    probe: { type: 'string', default: 'http://127.0.0.1:5180' },
    scope: { type: 'string' },
    negative: { type: 'string' },
    'verify-replay': { type: 'boolean', default: false },
    web: { type: 'boolean', default: false },
    me: { type: 'string', default: '/api/site' },
    'cookie-file': { type: 'string' },
    'use-handle': { type: 'boolean', default: false },
    'start-path': { type: 'string' },
    'initial-cookies': { type: 'string' },
  },
});

const allowedNegativeCases = new Set(['issuer', 'state', 'correlation']);
const cookiesByOrigin = new Map();
const phases = [];

function localUrl(value) {
  const url = new URL(value);
  if (!['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
      || !['http:', 'https:'].includes(url.protocol)
      || url.username || url.password) {
    throw new Error('This disposable OAuth driver only permits loopback URLs.');
  }
  return url;
}

async function request(value, { method = 'GET', body, headers = {} } = {}) {
  const url = localUrl(value);
  const jar = cookiesByOrigin.get(url.origin) ?? new Map();
  cookiesByOrigin.set(url.origin, jar);
  // Node fetch rewrites Sec-Fetch-Mode. The pinned issuer correctly requires
  // navigate for its page and same-origin for its CSRF-protected UI API.
  const payload = body ? JSON.stringify(body) : undefined;
  const result = await new Promise((resolve, reject) => {
    const send = url.protocol === 'https:' ? httpsRequest : httpRequest;
    const outgoing = send(url, { method, headers: {
      Accept: 'application/json, text/html',
      ...(jar.size ? { Cookie: [...jar].map(([k, v]) => `${k}=${v}`).join('; ') } : {}),
      ...(payload ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(payload) } : {}),
      ...headers,
    } }, (incoming) => {
      const chunks = [];
      incoming.on('data', chunk => chunks.push(chunk));
      incoming.on('end', () => resolve({ status: incoming.statusCode,
        headers: incoming.headers, text: Buffer.concat(chunks).toString('utf8') }));
      incoming.on('error', reject);
    });
    outgoing.setTimeout(30_000, () => outgoing.destroy(new Error('Loopback request timed out.')));
    outgoing.on('error', reject);
    outgoing.end(payload);
  });
  for (const cookie of result.headers['set-cookie'] ?? []) {
    const pair = cookie.split(';', 1)[0];
    const index = pair.indexOf('=');
    if (index > 0) {
      if (!pair.slice(index + 1)) jar.delete(pair.slice(0, index));
      else jar.set(pair.slice(0, index), pair.slice(index + 1));
    }
  }
  let json;
  try { json = JSON.parse(result.text); } catch { /* Authorization page is HTML. */ }
  return { response: { status: result.status, ok: result.status >= 200 && result.status < 300,
    location: result.headers.location }, json };
}

function expectStatus(result, label) {
  if (!result.response.ok) {
    // Never include raw auth responses, request URLs, passwords or tokens.
    throw new Error(`${label} failed with HTTP ${result.response.status}.`);
  }
  phases.push(label);
}

async function run() {
  if (!values.accounts || !values.account) {
    throw new Error('Required: --accounts <ignored-fixture.json> --account <name>.');
  }
  if (values.negative && !allowedNegativeCases.has(values.negative)) {
    throw new Error('--negative must be issuer, state or correlation.');
  }
  if (values.negative === 'correlation' && !values.web) throw new Error('Correlation requires --web.');
  const fixture = JSON.parse(await readFile(values.accounts, 'utf8'));
  const candidates = Array.isArray(fixture) ? fixture : fixture.accounts;
  if (!Array.isArray(candidates)) throw new Error('Fixture must contain an accounts array.');
  const account = candidates.find((candidate) => (candidate.name ?? candidate.role) === values.account);
  if (!account?.did || !account?.password || !(account.handle ?? account.username)) {
    throw new Error('Named disposable account is missing required fields.');
  }
  const pds = localUrl(account.pds).origin;
  const probe = localUrl(values.probe).origin;
  if (values['initial-cookies']) {
    const saved = JSON.parse(await readFile(values['initial-cookies'], 'utf8'));
    if (saved.origin !== probe) throw new Error('Initial cookie origin does not match the application.');
    cookiesByOrigin.set(probe, new Map(saved.cookies));
  }
  let authorizationUrl;
  if (values.web) {
    if (values.scope) throw new Error('Web grants must be selected by the application, not a query scope.');
    if (values['start-path'] && !['/api/connections/rooms', '/api/connections/authority'].includes(values['start-path'])) throw new Error('Unsupported application connection path.');
    const challenge = new URL(values['start-path'] ?? '/auth/atproto/challenge', probe);
    challenge.searchParams.set('identifier', values['use-handle'] ? account.handle : account.did);
    challenge.searchParams.set('return', '/');
    const start = await request(challenge);
    if (![302, 303].includes(start.response.status) || !start.response.location) {
      throw new Error(`Web challenge failed with HTTP ${start.response.status}.`);
    }
    authorizationUrl = localUrl(start.response.location);
    phases.push('Koan challenge, browser correlation and PAR');
  } else {
    const start = await request(`${probe}/oauth/start`, {
      method: 'POST',
      body: { did: account.did, pds, ...(values.scope ? { scope: values.scope } : {}) },
    });
    expectStatus(start, 'OAuth start and PAR');
    authorizationUrl = localUrl(start.json?.authorizationUrl);
  }
  if (authorizationUrl.origin !== pds || authorizationUrl.pathname !== '/oauth/authorize') {
    throw new Error('Authorization destination differs from the test account issuer.');
  }
  const page = await request(authorizationUrl, { headers: {
    'Sec-Fetch-Site': 'cross-site',
    'Sec-Fetch-Mode': 'navigate',
    'Sec-Fetch-Dest': 'document',
  } });
  if ([302, 303].includes(page.response.status) && page.response.location) {
    const denied = localUrl(page.response.location);
    const errorCode = denied.searchParams.get('error');
    if (errorCode && /^[a-z_]+$/.test(errorCode)) {
      throw new Error(`Issuer rejected authorization: ${errorCode}.`);
    }
  }
  expectStatus(page, 'Issuer authorization page');
  const csrf = cookiesByOrigin.get(pds)?.get('csrf-token');
  if (!csrf) throw new Error('Reference issuer did not supply its CSRF cookie.');

  const uiHeaders = {
    Origin: pds,
    Referer: authorizationUrl.href,
    'Sec-Fetch-Mode': 'same-origin',
    'Sec-Fetch-Site': 'same-origin',
    'x-csrf-token': csrf,
  };
  const apiPrefix = `${pds}/@atproto/oauth-provider/~api`;
  const signIn = await request(`${apiPrefix}/sign-in`, {
    method: 'POST',
    headers: uiHeaders,
    body: {
      locale: 'en', username: account.handle ?? account.username,
      password: account.password, remember: false,
    },
  });
  expectStatus(signIn, 'Issuer password authentication');
  if (signIn.json?.account?.did !== account.did || !signIn.json?.ephemeralToken) {
    throw new Error('Issuer authenticated an unexpected account or omitted request binding.');
  }
  const consent = await request(`${apiPrefix}/consent`, {
    method: 'POST',
    headers: {
      ...uiHeaders,
      'x-csrf-token': cookiesByOrigin.get(pds).get('csrf-token'),
      Authorization: `Bearer ${signIn.json.ephemeralToken}`,
    },
    body: { did: account.did },
  });
  expectStatus(consent, 'Issuer consent');
  let callbackUrl = localUrl(consent.json?.url);
  if (callbackUrl.origin === pds && callbackUrl.pathname === '/oauth/authorize/redirect') {
    const redirect = await request(callbackUrl, { headers: {
      Origin: pds, Referer: authorizationUrl.href,
      'Sec-Fetch-Site': 'same-origin', 'Sec-Fetch-Mode': 'navigate', 'Sec-Fetch-Dest': 'document',
    } });
    if (![302, 303].includes(redirect.response.status) || !redirect.response.location) {
      throw new Error(`Issuer callback redirect failed with HTTP ${redirect.response.status}.`);
    }
    callbackUrl = localUrl(redirect.response.location);
    phases.push('Issuer callback redirect');
  }
  const allowedCallbackHosts = new Set(['localhost', '127.0.0.1']);
  const probeUrl = new URL(probe);
  if (!allowedCallbackHosts.has(callbackUrl.hostname)
      || callbackUrl.port !== probeUrl.port
      || callbackUrl.pathname !== (values.web ? '/auth/atproto/callback' : '/oauth/callback')) {
    throw new Error('Issuer callback does not target the native probe.');
  }
  if (callbackUrl.searchParams.has('error')) {
    throw new Error('Issuer denied authorization before token exchange.');
  }
  if (values.negative === 'issuer') callbackUrl.searchParams.set('iss', 'http://127.0.0.1:1');
  if (values.negative === 'state') callbackUrl.searchParams.set('state', 'unknown-probe-state');
  if (values.negative === 'correlation') cookiesByOrigin.delete(callbackUrl.origin);

  const callback = await request(callbackUrl);
  if (values.negative) {
    if (callback.response.status < 400 || callback.response.status >= 500) {
      throw new Error('Invalid callback was not rejected as a client authentication failure.');
    }
    if (values.web && [...(cookiesByOrigin.get(probe)?.keys() ?? [])].some(k => k.startsWith('.AspNetCore.Koan.cookie'))) {
      throw new Error('A rejected callback issued an application cookie.');
    }
    return { passed: true, account: values.account, negative: values.negative,
      status: callback.response.status, phases };
  }
  if (values.web) {
    if (![302, 303].includes(callback.response.status) || new URL(callback.response.location, probe).href !== `${probe}/`) {
      throw new Error(`Koan callback failed to restore the requested destination (HTTP ${callback.response.status}).`);
    }
    if (!values.me.startsWith('/') || values.me.startsWith('//')) throw new Error('Identity path must be local.');
    const me = await request(new URL(values.me, probe));
    expectStatus(me, 'Authenticated application welcome');
    if ((me.json?.participant?.did ?? me.json?.did) !== account.did) throw new Error('Application cookie did not confirm the expected DID.');
    phases.push('Koan cookie carries verified DID');
  } else {
    expectStatus(callback, 'Native token exchange and verified DID');
    if (callback.json?.authenticated !== true || callback.json?.did !== account.did) {
      throw new Error('Native callback did not confirm the expected DID.');
    }
  }
  if (values['verify-replay']) {
    const replay = await request(callbackUrl);
    if (replay.response.status < 400 || replay.response.status >= 500) {
      throw new Error('Replayed callback was not rejected as a client authentication failure.');
    }
    phases.push('Replayed callback rejected');
  }
  if (values['cookie-file']) {
    if (!values.web) throw new Error('Cookie capture requires --web.');
    const path = resolve(values['cookie-file']);
    if (!path.startsWith(resolve('.local') + sep)) throw new Error('Disposable cookie files must stay under ignored .local/.');
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, JSON.stringify({ origin: probe, did: account.did, cookies: [...(cookiesByOrigin.get(probe) ?? [])] }), { mode: 0o600 });
  }
  return { passed: true, account: values.account, did: account.did, pds,
    sdk: values.web ? 'Koan AT connector' : callback.json.sdk, phases };
}

try {
  console.log(JSON.stringify(await run(), null, 2));
} catch (error) {
  console.error(JSON.stringify({ passed: false, phases,
    error: error instanceof Error ? error.message : 'OAuth probe failed.' }, null, 2));
  process.exitCode = 1;
}
