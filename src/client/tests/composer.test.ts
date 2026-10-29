// @vitest-environment jsdom
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { draftKey, isTid, mintTid } from '../composer'
import { mountIslands } from '../islands'
import { composing } from '../islands/composer'

// The composer as the server renders it: a reply form under a Topic's Posts and the form that starts a Topic. Drafts
// are kept by participant and scope, a send goes only on the person's submit, a confirmed Post clears its draft, an
// uncertain send keeps its TID and asks the Post's address before offering to send again, and a refusal reads inline.

const topic = 'https://tangent.example/t/workshop/lanterns'
const rootId = '3mwaaaaaaaar2'
const replyId = '3mwbbbbbbbbb2'
const servedId = '3mwccccccccc2'
const participant = 'did:plc:bea'
const topicKey = draftKey(participant, `topic:${rootId}`)
const attribute = (value: object) => JSON.stringify(value).replaceAll('"', '&quot;')

function replyPage(id = servedId, who = participant) {
  const props = attribute({ participant: who, scope: `topic:${rootId}`, locate: `${topic}/`, noun: 'reply' })
  document.body.innerHTML =
    `<article id="${rootId}" class="post post-root"><div class="post-main"><span class="post-author">Ada</span></div></article>` +
    `<article id="${replyId}" class="post"><div class="post-main"><span class="post-author">Leo</span></div></article>` +
    `<form class="reply-form" method="post" action="${topic}" data-island="composer" data-props="${props}">` +
    '<input type="hidden" name="__RequestVerificationToken" value="token">' +
    `<input type="hidden" name="id" value="${id}"><input type="hidden" name="parent">` +
    '<p class="reply-form-to" data-reply-to hidden>Replying to <a href="#" data-reply-to-post></a> · ' +
    `<a href="${topic}#reply" data-reply-to-topic>reply to the topic instead</a></p>` +
    '<textarea name="body" required></textarea>' +
    '<button type="submit" data-busy-label="Posting…">Post reply</button></form>'
  mountIslands(document)
  return elements()
}

function newTopicPage() {
  const props = attribute({ participant, scope: 'tangent:workshop', noun: 'topic' })
  document.body.innerHTML =
    `<form method="post" action="https://tangent.example/t/workshop/new" data-island="composer" data-props="${props}">` +
    `<input type="hidden" name="__RequestVerificationToken" value="token"><input type="hidden" name="id" value="${servedId}">` +
    '<input name="title"><textarea name="body"></textarea><button type="submit" data-busy-label="Posting…">Post</button></form>'
  mountIslands(document)
  return elements()
}

function elements() {
  const form = document.querySelector('form')!
  return {
    form,
    body: form.querySelector('textarea')!,
    title: form.querySelector<HTMLInputElement>('input[name="title"]'),
    id: form.querySelector<HTMLInputElement>('input[name="id"]')!,
    parent: form.querySelector<HTMLInputElement>('input[name="parent"]'),
    button: form.querySelector('button')!,
    status: () => form.querySelector<HTMLElement>('.composer-status')!,
  }
}

function type(field: HTMLInputElement | HTMLTextAreaElement, value: string) {
  field.value = value
  field.dispatchEvent(new Event('input', { bubbles: true }))
}

const submit = (form: HTMLFormElement) => form.dispatchEvent(new Event('submit', { cancelable: true }))
const stored = (key = topicKey) => JSON.parse(localStorage.getItem(key) ?? 'null')
const json = (status: number, value: object, type = 'application/json') =>
  new Response(JSON.stringify(value), { status, headers: { 'Content-Type': type } })
const sentFields = (call: unknown[]) => Object.fromEntries((call[1] as RequestInit).body as URLSearchParams)

let fetched: ReturnType<typeof vi.fn>
let shown: ReturnType<typeof vi.fn<(address: string) => void>>
const original = { ...composing }

beforeEach(() => {
  fetched = vi.fn()
  vi.stubGlobal('fetch', fetched)
  shown = vi.fn<(address: string) => void>()
  composing.show = shown
})

afterEach(() => {
  Object.assign(composing, original)
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  localStorage.clear()
  document.body.innerHTML = ''
})

test('a draft is kept by participant and scope from every keystroke and comes back with its TID', () => {
  const first = replyPage()
  type(first.body, 'I light mine at six.')

  expect(stored()).toEqual({ id: servedId, body: 'I light mine at six.', sent: false })

  const again = replyPage('3mwdddddddd22')
  expect(again.body.value).toBe('I light mine at six.')
  expect(again.id.value).toBe(servedId)

  const someoneElse = replyPage('3mwdddddddd22', 'did:plc:mo')
  expect(someoneElse.body.value).toBe('')
  expect(someoneElse.id.value).toBe('3mwdddddddd22')

  // Emptied, the draft is gone.
  const emptied = replyPage()
  type(emptied.body, '')
  expect(localStorage.getItem(topicKey)).toBeNull()
  expect(fetched).not.toHaveBeenCalled()
})

test('each Post offers a reply to it, whose draft is kept apart from the Topic’s', () => {
  const page = replyPage()
  type(page.body, 'To the topic.')
  const reply = document.querySelector<HTMLButtonElement>(`article[id="${replyId}"] .composer-reply-to`)!

  expect(document.querySelector(`article[id="${rootId}"] .composer-reply-to`)).toBeNull()
  reply.click()

  expect(page.parent!.value).toBe(replyId)
  expect(page.body.value).toBe('')
  expect(isTid(page.id.value) && page.id.value !== servedId).toBe(true)
  const replyingTo = document.querySelector<HTMLElement>('[data-reply-to]')!
  expect(replyingTo.hidden).toBe(false)
  expect(replyingTo.querySelector('[data-reply-to-post]')!.textContent).toBe('Leo')
  type(page.body, 'To Leo.')
  expect(stored(draftKey(participant, `post:${replyId}`))).toMatchObject({ body: 'To Leo.', parent: replyId })

  document.querySelector<HTMLAnchorElement>('[data-reply-to-topic]')!.click()

  expect(page.parent!.value).toBe('')
  expect(replyingTo.hidden).toBe(true)
  expect(page.id.value).toBe(servedId)
  expect(page.body.value).toBe('To the topic.')
  expect(fetched).not.toHaveBeenCalled()
})

test('nothing is sent without the person’s submit, not even a draft that comes back', async () => {
  localStorage.setItem(topicKey, JSON.stringify({ id: servedId, body: 'Kept from before.', sent: false }))
  const page = replyPage()
  type(page.body, 'Kept from before, and more.')
  await new Promise((resolve) => setTimeout(resolve, 20))

  expect(page.body.value).toBe('Kept from before, and more.')
  expect(fetched).not.toHaveBeenCalled()
  expect(page.form.dataset.composer).toBe('ready')
})

test('a submit sends the form as JSON is answered, and clears the draft only once the server confirms the Post', async () => {
  let answer!: (response: Response) => void
  fetched.mockImplementation(() => new Promise<Response>((resolve) => (answer = resolve)))
  const page = replyPage()
  type(page.body, 'I light mine at six.')

  submit(page.form)
  submit(page.form)

  expect(fetched).toHaveBeenCalledOnce()
  const [url, init] = fetched.mock.calls[0] as [string, RequestInit]
  expect(init.method).toBe('POST')
  expect(url).toBe(topic)
  expect(new Headers(init.headers).get('Accept')).toBe('application/json')
  expect(sentFields(fetched.mock.calls[0])).toEqual({ __RequestVerificationToken: 'token', id: servedId, parent: '', body: 'I light mine at six.' })
  expect(page.form.getAttribute('aria-busy')).toBe('true')
  expect(page.body.readOnly).toBe(true)
  expect(page.button.textContent).toBe('Posting…')
  expect(stored()).toEqual({ id: servedId, body: 'I light mine at six.', sent: true })

  answer(json(201, { id: servedId, address: `${topic}/${servedId}` }))

  await vi.waitFor(() => expect(shown).toHaveBeenCalledWith(`${topic}/${servedId}`))
  expect(localStorage.getItem(topicKey)).toBeNull()
})

test('an uncertain submit keeps its TID and asks the Post’s address: found, the draft is cleared and the Post shown', async () => {
  fetched.mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(json(200, { id: rootId }))
  const page = replyPage()
  type(page.body, 'I light mine at six.')

  submit(page.form)

  await vi.waitFor(() => expect(shown).toHaveBeenCalledWith(`${topic}/${servedId}`))
  expect(fetched).toHaveBeenCalledTimes(2)
  const [url, init] = fetched.mock.calls[1] as [string, RequestInit]
  expect(url).toBe(`${topic}/${servedId}`)
  expect(init.method).toBe('GET')
  expect(localStorage.getItem(topicKey)).toBeNull()
})

test('an uncertain submit whose Post is not found is offered again with the same TID, and the retry is the same request', async () => {
  fetched
    .mockResolvedValueOnce(new Response('', { status: 502 }))
    .mockResolvedValueOnce(json(404, { title: 'Not found' }, 'application/problem+json'))
  const page = replyPage()
  type(page.body, 'I light mine at six.')

  submit(page.form)

  await vi.waitFor(() => expect(page.form.dataset.composer).toBe('missing'))
  expect(page.button.textContent).toBe('Send again')
  expect(page.status().hidden).toBe(false)
  expect(page.status().textContent).toContain('wasn’t posted')
  expect(page.id.value).toBe(servedId)
  expect(stored()).toEqual({ id: servedId, body: 'I light mine at six.', sent: false })
  expect(shown).not.toHaveBeenCalled()

  fetched.mockResolvedValueOnce(json(201, { address: `${topic}/${servedId}` }))
  submit(page.form)

  await vi.waitFor(() => expect(shown).toHaveBeenCalledWith(`${topic}/${servedId}`))
  expect(sentFields(fetched.mock.calls[2])).toEqual(sentFields(fetched.mock.calls[0]))
  expect(localStorage.getItem(topicKey)).toBeNull()
})

test('a reload while a submit was on its way checks the Post’s address before anything else, and never sends by itself', async () => {
  localStorage.setItem(topicKey, JSON.stringify({ id: servedId, body: 'On its way.', sent: true }))
  fetched.mockResolvedValueOnce(json(404, { title: 'Not found' }, 'application/problem+json'))

  const page = replyPage('3mwdddddddd22')

  await vi.waitFor(() => expect(page.form.dataset.composer).toBe('missing'))
  expect(fetched).toHaveBeenCalledOnce()
  expect((fetched.mock.calls[0][1] as RequestInit).method).toBe('GET')
  expect(fetched.mock.calls[0][0]).toBe(`${topic}/${servedId}`)
  expect(page.body.value).toBe('On its way.')
  expect(page.id.value).toBe(servedId)

  // Where the check gets no answer either, it is offered again rather than a send.
  localStorage.setItem(topicKey, JSON.stringify({ id: servedId, body: 'On its way.', sent: true }))
  fetched.mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(json(200, {}))
  const reloaded = replyPage('3mwdddddddd22')

  await vi.waitFor(() => expect(reloaded.form.dataset.composer).toBe('uncertain'))
  expect(reloaded.button.textContent).toBe('Check again')
  submit(reloaded.form)
  await vi.waitFor(() => expect(shown).toHaveBeenCalledWith(`${topic}/${servedId}`))
  expect(fetched.mock.calls.every((call) => (call[1] as RequestInit).method === 'GET')).toBe(true)
})

test('a refusal reads inline above the fields, keeps the words, and opens no dialog', async () => {
  const alerted = vi.spyOn(window, 'alert').mockImplementation(() => {})
  fetched.mockResolvedValueOnce(
    json(403, { title: 'Locked', detail: 'This topic is locked, so no one replies in it.', gate: 'locked' }, 'application/problem+json'),
  )
  const page = replyPage()
  type(page.body, 'Too late?')

  submit(page.form)

  await vi.waitFor(() => expect(page.form.dataset.composer).toBe('refused'))
  expect(page.form.firstElementChild).toBe(page.status())
  expect(page.status().textContent).toBe('This topic is locked, so no one replies in it.')
  expect(page.status().getAttribute('role')).toBe('alert')
  expect(page.body.readOnly).toBe(false)
  expect(page.button.textContent).toBe('Post reply')
  expect(stored()).toEqual({ id: servedId, body: 'Too late?', sent: false })
  expect(alerted).not.toHaveBeenCalled()
  expect(document.querySelector('dialog')).toBeNull()
  expect(shown).not.toHaveBeenCalled()
})

test('a TID that already names other words takes a new one, so sending again posts these words', async () => {
  fetched.mockResolvedValueOnce(json(409, { title: 'Already written', detail: "Post '3mwccccccccc2' already exists with a different body." }))
  const page = replyPage()
  type(page.body, 'Other words.')

  submit(page.form)

  await vi.waitFor(() => expect(page.form.dataset.composer).toBe('refused'))
  expect(page.status().textContent).toContain('already sent with other words')
  expect(isTid(page.id.value) && page.id.value !== servedId).toBe(true)
  expect(stored().id).toBe(page.id.value)
})

test('a new Topic’s draft is kept under its Tangent; with no address to ask, an uncertain send is offered again with its TID', async () => {
  fetched.mockRejectedValueOnce(new TypeError('Failed to fetch'))
  const page = newTopicPage()
  type(page.title!, 'Wicks')
  type(page.body, 'Cotton or hemp?')
  const key = draftKey(participant, 'tangent:workshop')

  expect(stored(key)).toEqual({ id: servedId, title: 'Wicks', body: 'Cotton or hemp?', sent: false })
  submit(page.form)

  await vi.waitFor(() => expect(page.form.dataset.composer).toBe('uncertain'))
  expect(fetched).toHaveBeenCalledOnce()
  expect(page.button.textContent).toBe('Send again')
  expect(page.status().textContent).toContain('Sending again is safe')
  expect(stored(key)).toEqual({ id: servedId, title: 'Wicks', body: 'Cotton or hemp?', sent: true })

  fetched.mockResolvedValueOnce(json(201, { address: 'https://tangent.example/t/workshop/wicks/' + servedId }))
  submit(page.form)

  await vi.waitFor(() => expect(shown).toHaveBeenCalledWith('https://tangent.example/t/workshop/wicks/' + servedId))
  expect(sentFields(fetched.mock.calls[1])).toEqual(sentFields(fetched.mock.calls[0]))
  expect(localStorage.getItem(key)).toBeNull()
})

test('a TID minted here is a TID, later than the one before it', () => {
  const first = mintTid(Date.UTC(2026, 9, 29))
  const second = mintTid(Date.UTC(2026, 9, 29))
  expect(isTid(first) && isTid(second)).toBe(true)
  expect(second > first).toBe(true)
})
