// @vitest-environment jsdom
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { writeArticle } from '../article/editor'
import { draftKey } from '../composer'
import { mountIslands } from '../islands'

// The article form as the server renders it, with the composer on it: the visual editor writes Markdown back to the
// form's own field, never rewrites what nobody changed, stays out where it cannot hold the meaning, and an image on its
// way holds the form.

const participant = 'did:plc:bea'
const scope = 'tangent:field-notes'
const attribute = (value: object) => JSON.stringify(value).replaceAll('"', '&quot;')

function page(body = '', cover = '') {
  const composerProps = attribute({ participant, scope, locate: 'https://tangent.example/t/field-notes/', noun: 'article' })
  document.body.innerHTML =
    `<form method="post" action="https://tangent.example/t/field-notes/new" data-island="composer" data-props="${composerProps}">` +
    '<input type="hidden" name="__RequestVerificationToken" value="token"><input type="hidden" name="id" value="3mwccccccccc2">' +
    `<input type="hidden" name="cover" value="${cover}">` +
    '<div data-article-cover></div><textarea name="title" required></textarea><span data-article-minutes>1 min read</span>' +
    `<div class="host"><label>Article</label><textarea name="body" required>${body}</textarea></div>` +
    '<button type="submit">Publish</button></form>'
  mountIslands(document)
  const host = document.querySelector<HTMLElement>('.host')!
  const stop = writeArticle({ host, door: '/', bring: 'https://tangent.example/t/field-notes/media', token: '__RequestVerificationToken', participant, edit: null })
  stops.push(stop)
  return {
    host,
    form: document.querySelector('form')!,
    source: host.querySelector<HTMLTextAreaElement>('textarea')!,
    surface: () => host.querySelector<HTMLElement>('.ProseMirror')!,
    submit: document.querySelector<HTMLButtonElement>('button[type="submit"]')!,
    cover: document.querySelector<HTMLInputElement>('input[name="cover"]')!,
  }
}

const stops: (() => void)[] = []
let fetched: ReturnType<typeof vi.fn>

beforeEach(() => {
  fetched = vi.fn()
  vi.stubGlobal('fetch', fetched)
  URL.createObjectURL = vi.fn(() => 'blob:preview')
  URL.revokeObjectURL = vi.fn()
  // jsdom has no layout, which ProseMirror asks of a range when it scrolls to the selection.
  Range.prototype.getClientRects = () => [] as unknown as DOMRectList
  Range.prototype.getBoundingClientRect = () => new DOMRect()
  document.elementFromPoint = () => null
})

afterEach(() => {
  while (stops.length) stops.pop()!()
  vi.unstubAllGlobals()
  localStorage.clear()
  document.body.innerHTML = ''
})

test('the article opens in the visual view, in the article’s type, and the field stays the form’s', () => {
  const { host, source, surface } = page('## Cotton\n\nIt burns *even*.')

  expect(host.classList.contains('is-visual')).toBe(true)
  expect(source.hidden).toBe(true)
  expect(source.required).toBe(false)
  expect(surface().classList.contains('article-prose')).toBe(true)
  expect(surface().querySelector('h2')!.textContent).toBe('Cotton')
  expect(surface().querySelector('em')!.textContent).toBe('even')
})

test('what nobody changed is never rewritten, and a change writes Markdown back to the field, kept as a draft', () => {
  const written = '* a star bullet\n\n__strong the other way__'
  const { source, surface, host } = page(written)
  expect(host.classList.contains('is-visual')).toBe(true)
  expect(source.value).toBe(written)

  const paragraph = surface().querySelector('li p')!
  paragraph.textContent = 'a star bullet, changed'
  return vi.waitFor(() => {
    expect(source.value).toContain('a star bullet, changed')
    expect(source.value).toContain('**strong the other way**')
    const draft = JSON.parse(localStorage.getItem(draftKey(participant, scope))!)
    expect(draft.body).toBe(source.value)
  })
})

test('where the visual view cannot hold the meaning the source view stands, says why, and keeps every byte', () => {
  const table = 'Before\n\n| Name | Value |\n| --- | --- |\n| One | Two |'
  const { host, source, surface } = page(table)

  expect(host.classList.contains('is-visual')).toBe(false)
  expect(source.hidden).toBe(false)
  expect(source.value).toBe(table)
  expect(surface().closest<HTMLElement>('.article-surface-wrap')!.hidden).toBe(true)
  expect(host.querySelector('.article-status')!.textContent).toContain('cannot preserve')

  // Offering the visual view again asks again, and the words stay as they are.
  host.querySelector<HTMLButtonElement>('.article-views button[title="Visual"]')!.click()
  expect(host.classList.contains('is-visual')).toBe(false)
  expect(source.value).toBe(table)
})

test('the source view is the field itself, and going back to the visual view takes what was typed there', () => {
  const { host, source, surface } = page('First.')
  host.querySelector<HTMLButtonElement>('.article-views button[title="Markdown source"]')!.click()
  expect(source.hidden).toBe(false)

  source.value = '## Second\n\n> A quote.'
  source.dispatchEvent(new Event('input', { bubbles: true }))
  host.querySelector<HTMLButtonElement>('.article-views button[title="Visual"]')!.click()

  expect(host.classList.contains('is-visual')).toBe(true)
  expect(surface().querySelector('h2')!.textContent).toBe('Second')
  expect(surface().querySelector('blockquote')!.textContent).toBe('A quote.')
})

test('a draft that comes back opens where it was, the cover with it', () => {
  localStorage.setItem(draftKey(participant, scope), JSON.stringify({ id: '3mwccccccccc2', body: '## Kept\n\nFrom before.', title: 'Kept title', cover: 'abc', sent: false }))
  const { surface, source, cover } = page()

  expect(source.value).toBe('## Kept\n\nFrom before.')
  expect(surface().querySelector('h2')!.textContent).toBe('Kept')
  expect(cover.value).toBe('abc')
  expect(document.querySelector<HTMLImageElement>('[data-article-cover] img')!.getAttribute('src')).toBe('/media/abc')
  expect(document.querySelector<HTMLTextAreaElement>('textarea[name="title"]')!.value).toBe('Kept title')
})

test('a cover comes in the page: it shows at once, the form waits for it, and the field holds its id', async () => {
  let answer!: (response: Response) => void
  fetched.mockImplementation(() => new Promise<Response>((resolve) => (answer = resolve)))
  const { submit, cover } = page('Words.')

  const offer = document.querySelector<HTMLButtonElement>('.article-cover-offer')!
  expect(offer.textContent).toBe('Add a cover')
  const chooser = document.querySelector<HTMLInputElement>('[data-article-cover] input[type="file"]')!
  Object.defineProperty(chooser, 'files', { configurable: true, value: [new File(['x'], 'lamp.jpg', { type: 'image/jpeg' })] })
  chooser.dispatchEvent(new Event('change'))

  expect(document.querySelector<HTMLImageElement>('[data-article-cover] img')!.src).toBe('blob:preview')
  expect(submit.disabled).toBe(true)
  const [url, init] = fetched.mock.calls[0] as [string, RequestInit]
  expect(url).toBe('https://tangent.example/t/field-notes/media')
  expect((init.body as FormData).get('__RequestVerificationToken')).toBe('token')
  expect((init.body as FormData).get('file')).toBeInstanceOf(File)

  answer(new Response(JSON.stringify({ id: 'abc', reference: '/media/abc', address: 'https://tangent.example/media/abc' }), { status: 201 }))
  await vi.waitFor(() => expect(cover.value).toBe('abc'))
  expect(submit.disabled).toBe(false)
  expect(document.querySelector<HTMLImageElement>('[data-article-cover] img')!.getAttribute('src')).toBe('/media/abc')
  expect(JSON.parse(localStorage.getItem(draftKey(participant, scope))!).cover).toBe('abc')

  document.querySelector<HTMLButtonElement>('.article-cover-actions button[title="Remove the cover"]')!.click()
  expect(cover.value).toBe('')
  expect(document.querySelector('.article-cover-offer')).not.toBeNull()
})

test('an image pasted into the words stands where it was put while it comes, joins the Markdown once it has, and never writes its preview', async () => {
  let answer!: (response: Response) => void
  fetched.mockImplementation(() => new Promise<Response>((resolve) => (answer = resolve)))
  const { source, surface, submit } = page('Above.\n\nBelow.')

  const paste = new Event('paste', { bubbles: true, cancelable: true }) as Event & { clipboardData: unknown }
  paste.clipboardData = { files: [new File(['x'], 'kiln at dusk.png', { type: 'image/png' })], types: ['Files'], getData: () => '' }
  surface().dispatchEvent(paste)

  expect(surface().querySelector('.article-image-coming img')).not.toBeNull()
  expect(submit.disabled).toBe(true)
  expect(source.value).not.toContain('blob:')

  answer(new Response(JSON.stringify({ id: 'abc', reference: '/media/abc', address: 'https://tangent.example/media/abc' }), { status: 201 }))
  await vi.waitFor(() => expect(source.value).toContain('![kiln at dusk](/media/abc)'))
  expect(source.value).toContain('Above.')
  expect(source.value).toContain('Below.')
  expect(source.value).not.toContain('blob:')
  expect(submit.disabled).toBe(false)
  expect(surface().querySelector('.article-image-coming')).toBeNull()
})

test('an image that does not come says why, inline, and costs no words', async () => {
  const alerted = vi.spyOn(window, 'alert').mockImplementation(() => {})
  fetched.mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Not kept', detail: 'Choose a PNG, JPEG or WebP image.' }), { status: 400 }))
  const { host, source, surface, submit } = page('Words stay.')

  const paste = new Event('paste', { bubbles: true, cancelable: true }) as Event & { clipboardData: unknown }
  paste.clipboardData = { files: [new File(['x'], 'broken.png', { type: 'image/png' })], types: ['Files'], getData: () => '' }
  surface().dispatchEvent(paste)

  await vi.waitFor(() => expect(host.querySelector('.article-status')!.textContent).toBe('Choose a PNG, JPEG or WebP image.'))
  expect(source.value).toBe('Words stay.')
  expect(surface().querySelector('.article-image-coming')).toBeNull()
  expect(submit.disabled).toBe(false)
  expect(alerted).not.toHaveBeenCalled()
})

test('an edit keeps its own draft by the version it was made from, and drops those of an earlier one', () => {
  localStorage.setItem(draftKey(participant, 'edit:post:1'), JSON.stringify({ id: '2222222222222', body: 'From an earlier version.', sent: false }))
  const key = draftKey(participant, 'edit:post:2')
  document.body.innerHTML =
    '<form method="post" action="https://tangent.example/edit"><input type="hidden" name="cover" value="">' +
    '<textarea name="title">Lanterns</textarea><div class="host"><label>Article</label><textarea name="body">First words.</textarea></div>' +
    '<button type="submit">Save</button></form>'
  const open = () => {
    const stop = writeArticle({ host: document.querySelector<HTMLElement>('.host')!, door: '/', bring: null, token: null, participant, edit: 'edit:post:2' })
    stops.push(stop)
  }
  open()
  expect(localStorage.getItem(draftKey(participant, 'edit:post:1'))).toBeNull()
  expect(localStorage.getItem(key)).toBeNull()

  const source = document.querySelector<HTMLTextAreaElement>('textarea[name="body"]')!
  source.value = 'Second words.'
  source.dispatchEvent(new Event('input', { bubbles: true }))
  expect(JSON.parse(localStorage.getItem(key)!).body).toBe('Second words.')

  stops.pop()!()
  source.value = 'First words.'
  open()
  expect(source.value).toBe('Second words.')
  expect(document.querySelector('.ProseMirror p')!.textContent).toBe('Second words.')

  // Back to what the article says, nothing is kept.
  source.value = source.defaultValue
  source.dispatchEvent(new Event('input', { bubbles: true }))
  expect(localStorage.getItem(key)).toBeNull()
})
