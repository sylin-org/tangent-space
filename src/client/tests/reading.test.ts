// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest'
import { mountIslands } from '../islands'
import { quoted } from '../islands/quote'
import { keepPlace, readPlace } from '../reading'

// What a reader meets beyond a Topic's Posts: a Post's link copied, its earlier versions read when their disclosure
// opens, a passage quoted with its heading, the door's way back to a Topic left mid-way, and the articles filter.

const attribute = (value: object) => JSON.stringify(value).replaceAll('"', '&quot;')

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
  document.body.innerHTML = ''
})

test('Copy link copies the Post’s own address', async () => {
  const writeText = vi.fn(async () => {})
  vi.stubGlobal('navigator', { clipboard: { writeText } })
  const address = 'https://tangent.example/t/discussions/kinds/3mwccccccccc2'
  document.body.innerHTML = `<button hidden data-island="copy" data-props="${attribute({ text: address })}">Copy link</button>`
  mountIslands(document)
  const button = document.querySelector('button')!

  expect(button.hidden).toBe(false)
  button.click()
  await vi.waitFor(() => expect(button.textContent).toBe('Copied'))
  expect(writeText).toHaveBeenCalledWith(address)
})

test('opening a Post’s history reads its earlier versions into the disclosure', async () => {
  const url = 'https://tangent.example/t/discussions/kinds/3mwccccccccc2/history'
  const fetched = vi.fn(async () => new Response('<section class="post-history-versions"><p>Before the edit</p></section>'))
  vi.stubGlobal('fetch', fetched)
  document.body.innerHTML =
    `<details data-island="history" data-props="${attribute({ url })}"><summary>Edited</summary>` +
    `<div class="post-history-body"><a href="${url}">Read its earlier versions</a></div></details>`
  mountIslands(document)
  const details = document.querySelector('details')!

  details.open = true
  details.dispatchEvent(new Event('toggle'))

  await vi.waitFor(() => expect(details.querySelector('.post-history-versions')?.textContent).toBe('Before the edit'))
  expect(fetched).toHaveBeenCalledOnce()
})

test('a quoted passage is a Markdown quote linked to its heading', () => {
  expect(quoted('The seam is the point.\nGold, too.', 'https://tangent.example/t/articles/gold', { id: 'section-the-seam', text: 'The [seam]' }))
    .toBe('> The seam is the point.\n> Gold, too.\n\nFrom [The seam](https://tangent.example/t/articles/gold#section-the-seam)\n\n')
})

test('the door offers the way back to where the reader stopped, while it still answers', async () => {
  const participant = 'did:plc:ada'
  const address = 'https://tangent.example/t/discussions/notes/3mwccccccccc2'
  keepPlace(participant, { topic: '3mwbbbbbbbbb2', title: 'Notes from the workshop', address })
  const fetched = vi.fn(async () => new Response(null, { status: 200 }))
  vi.stubGlobal('fetch', fetched)
  document.body.innerHTML = `<main><p class="door-continue" hidden data-island="continue" data-props="${attribute({ participant })}"><a href="#">Continue reading</a></p></main>`
  mountIslands(document)
  const line = document.querySelector<HTMLElement>('.door-continue')!

  await vi.waitFor(() => expect(line.hidden).toBe(false))
  expect(line.querySelector('a')!.getAttribute('href')).toBe(address)
  expect(line.textContent).toBe('Continue reading Notes from the workshop →')
  expect(fetched).toHaveBeenCalledWith(address, expect.objectContaining({ method: 'HEAD' }))
  expect(readPlace(participant)?.address).toBe(address)
})

test('the articles filter narrows the page’s articles to a month and lets them back', () => {
  document.body.innerHTML =
    '<div class="article-filter" hidden data-island="filter"><button class="article-filter-choice" data-month="2026-09">September 2026</button>' +
    '<button class="article-filter-choice" data-author="did:plc:mara">Mara</button><p class="article-filter-note"></p></div>' +
    '<div class="article-list"><article data-month="2026-09" data-author="did:plc:ada"></article>' +
    '<article data-month="2026-10" data-author="did:plc:mara"></article></div>'
  mountIslands(document)
  const [september] = document.querySelectorAll<HTMLButtonElement>('.article-filter-choice')
  const [first, second] = document.querySelectorAll<HTMLElement>('.article-list > article')

  expect(document.querySelector<HTMLElement>('.article-filter')!.hidden).toBe(false)
  september.click()
  expect([first.hidden, second.hidden]).toEqual([false, true])
  expect(september.getAttribute('aria-pressed')).toBe('true')
  expect(document.querySelector('.article-filter-note')!.textContent).toBe('1 of 2 on this page.')
  september.click()
  expect([first.hidden, second.hidden]).toEqual([false, false])
})
