// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick, watch } from 'vue'
import { useSite } from '../src/stores/site'
import { useRouter } from '../src/stores/router'
import { createConversation } from '../src/composables/conversation'
const json = (value: unknown) => new Response(JSON.stringify(value))
function identity(actor = 'alice') {
  setActivePinia(createPinia()); sessionStorage.clear()
  const site = useSite(); site.state = 'signedIn'
  site.site = { name: 'PRIVATE HOST', established: true, workspaceAvailable: true, signIn: '/auth/atproto/challenge', signOut: '/auth/logout', participant: { participantRef: actor, did: 'did:plc:' + actor, handle: actor + '.test', isOwner: false, joinedAt: '' } }
  site.server = { name: 'PRIVATE HOST', canManageParticipants: true }; return site
}
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); sessionStorage.clear() })

it.each([401, 403])('clears the current protected welcome and authority for HTTP %s', async status => {
  const site = identity(); sessionStorage.setItem('draft:alice:topic', JSON.stringify({ text: 'Held private draft' }))
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status })))
  await site.load()
  expect(site.state).toBe('error'); expect(site.authorizationFailure).toBe(status)
  expect(site.site).toBeNull(); expect(site.server).toBeNull()
  expect(site.announcement).toContain('account or session')
  expect(sessionStorage.getItem('draft:alice:topic')).toContain('Held private draft')
})

it.each([401, 403])('ignores an older HTTP %s failure after a newer welcome succeeds', async status => {
  const site = identity(); const welcome = { ...site.site!, participant: { ...site.site!.participant! } }
  const complete: Array<(response: Response) => void> = []
  vi.stubGlobal('fetch', vi.fn(path => String(path) === '/api/site' ? new Promise<Response>(resolve => complete.push(resolve)) : Promise.resolve(json({ name: 'Authorized Host', canManageParticipants: true }))))
  const older = site.load(), newer = site.load()
  complete[1]!(json(welcome)); await newer
  complete[0]!(new Response('{}', { status })); await older
  expect(site.authorizationFailure).toBeNull(); expect(site.state).toBe('signedIn')
  expect(site.site?.participant?.participantRef).toBe('alice'); expect(site.server?.name).toBe('Authorized Host')
})

it('does not restore protected branding from a server response begun before current authorization denial', async () => {
  const site = identity(); let complete!: (response: Response) => void
  vi.stubGlobal('fetch', vi.fn(path => String(path) === '/api/site' ? Promise.resolve(new Response('{}', { status: 401 })) : new Promise<Response>(resolve => { complete = resolve })))
  const earlier = site.loadServer(); await site.load()
  complete(json({ name: 'Stale private Host', canManageParticipants: true })); await earlier
  expect(site.site).toBeNull(); expect(site.server).toBeNull(); expect(site.authorizationFailure).toBe(401)
})

it.each([503, null])('keeps transport failure distinct from authorization refusal: %s', async status => {
  const site = identity()
  vi.stubGlobal('fetch', vi.fn(async () => { if (status) return new Response('{}', { status }); throw new TypeError('Network offline') }))
  await site.load()
  expect(site.state).toBe('error'); expect(site.authorizationFailure).toBeNull()
  expect(site.site?.participant?.participantRef).toBe('alice'); expect(site.announcement).toContain('could not be loaded')
})

it('recovers the intended author draft after denial and never exposes it to another account', async () => {
  const site = identity(); const alice = { ...site.site!, participant: { ...site.site!.participant! } }!, topic = { key: 'topic', tangentKey: 'tangent', title: 'PRIVATE TOPIC', canRead: true, canWrite: true, isLocked: false }
  const router = useRouter(); router.apply('/t/tangent/topics/topic'); const conversation = createConversation(router)
  const stop = watch(() => site.authorizationFailure, denied => { if (denied) conversation.reset() }, { flush: 'sync' })
  const stopActor = watch(() => site.site?.participant?.participantRef, () => conversation.reset(), { flush: 'sync' })
  conversation.selectedTopic.value = topic; conversation.draft.value = 'Private unsent words'; await nextTick()
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 401 }))); await site.load(); await nextTick()
  expect(conversation.selectedTopic.value).toBeNull(); expect(conversation.draft.value).toBe('')
  expect(sessionStorage.getItem('draft:alice:topic')).toContain('Private unsent words')
  vi.stubGlobal('fetch', vi.fn(async path => String(path) === '/api/site' ? json({ ...alice, participant: { ...alice.participant!, participantRef: 'bob' } }) : String(path) === '/api/server' ? json({}) : String(path).includes('/posts') ? json({ messages: [], nextCursor: null, resumeCursor: null }) : json(topic)))
  await site.load(); await conversation.choose('topic'); expect(conversation.draft.value).toBe('')
  vi.stubGlobal('fetch', vi.fn(async path => String(path) === '/api/site' ? json(alice) : String(path) === '/api/server' ? json({}) : String(path).includes('/posts') ? json({ messages: [], nextCursor: null, resumeCursor: null }) : json(topic)))
  await site.load(); await conversation.choose('topic')
  expect(site.authorizationFailure).toBeNull(); expect(conversation.draft.value).toBe('Private unsent words'); stop(); stopActor()
})
