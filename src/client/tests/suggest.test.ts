// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest'
import { markdownOf, namedAt } from '../named'
import { attachSuggestions, suggesting, textareaSurface, typedAt, type Suggestion } from '../suggest'

// Typing @ or # in a body field offers who and where from the Tangent's suggestions, as a list under the caret that the
// keys move through and pick from; never a dialog. A pick writes who or what it means, linked to its target.

const people: Suggestion[] = [
  { kind: 'person', insert: 'bea.example', target: '/p/0193b2c4-0000-7000-8000-00000000000b', name: 'Bea', note: null },
  { kind: 'role', insert: 'moderator', target: 'role:moderator', name: 'Moderators', note: '2 people' },
]

afterEach(() => {
  document.body.replaceChildren()
  vi.restoreAllMocks()
})

function field(address = '/t/workshop/suggest?topic=lanterns') {
  document.body.innerHTML = `<form><textarea id="body"></textarea></form>`
  const element = document.querySelector<HTMLTextAreaElement>('#body')!
  const stop = attachSuggestions(textareaSurface(element), address)
  return { element, stop }
}

async function type(element: HTMLTextAreaElement, words: string) {
  element.focus()
  element.value = words
  element.setSelectionRange(words.length, words.length)
  element.dispatchEvent(new Event('input', { bubbles: true }))
  await vi.waitFor(() => expect(document.querySelector('.suggestions')?.hasAttribute('hidden')).toBe(false))
}

test('a sign that begins a word, and what follows it, is what the caret stands after', () => {
  expect(typedAt('Hi @be')).toEqual({ sign: '@', prefix: 'be' })
  expect(typedAt('See #')).toEqual({ sign: '#', prefix: '' })
  expect(typedAt('(@mo')).toEqual({ sign: '@', prefix: 'mo' })
  expect(typedAt('ada@bsky')).toBeNull()
  expect(typedAt('## heading')).toBeNull()
  expect(typedAt('Hi @be ')).toBeNull()
})

test('the list opens under the field, the keys move through it, and Enter writes who the pick means', async () => {
  const read = vi.spyOn(suggesting, 'read').mockResolvedValue(people)
  const { element } = field()
  await type(element, 'Thanks @b')

  expect(read.mock.calls[0]![0]).toBe('/t/workshop/suggest?topic=lanterns&q=%40b')
  const options = [...document.querySelectorAll('[role="option"]')]
  expect(options.map((row) => row.textContent)).toEqual(['Bea@bea.example', 'Moderators@moderatorrole · 2 people'])
  expect(element.getAttribute('aria-expanded')).toBe('true')

  element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
  expect(element.value).toBe('Thanks [@Bea](/p/0193b2c4-0000-7000-8000-00000000000b) ')
  expect(document.querySelector('.suggestions')?.hasAttribute('hidden')).toBe(true)

  await type(element, element.value + 'and @mo')
  element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
  expect(document.querySelectorAll('[role="option"]')[1]!.getAttribute('aria-selected')).toBe('true')
  element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))
  expect(element.value).toBe('Thanks [@Bea](/p/0193b2c4-0000-7000-8000-00000000000b) and [@Moderators](role:moderator) ')

  await type(element, element.value + '#')
  element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  expect(document.querySelector('.suggestions')?.hasAttribute('hidden')).toBe(true)
})

test('a picked Tangent is linked to its address', async () => {
  vi.spyOn(suggesting, 'read').mockResolvedValue([{ kind: 'tangent', insert: 'general', target: '/t/general', name: 'General', note: null }])
  const { element } = field()
  await type(element, 'Try #ge')
  element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
  expect(element.value).toBe('Try [#General](/t/general) ')
})

test('a name holding Markdown’s marks is written escaped and reads back as it is', async () => {
  for (const name of ['Lu]men', 'Lu*men*', 'snake_case_', 'back\\slash', '[x](y)', 'a`b<c>&d'])
    expect(namedAt(markdownOf({ words: '@' + name, target: '/p/0193b2c4-0000-7000-8000-00000000000c' }))?.named).toEqual({ words: '@' + name, target: '/p/0193b2c4-0000-7000-8000-00000000000c' })

  vi.spyOn(suggesting, 'read').mockResolvedValue([{ kind: 'person', insert: 'lumen.example', target: '/p/0193b2c4-0000-7000-8000-00000000000c', name: 'Lu]m*e_n\\', note: null }])
  const { element } = field()
  await type(element, '@lu')
  element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
  expect(element.value).toBe('[@Lu\\]m\\*e\\_n\\\\](/p/0193b2c4-0000-7000-8000-00000000000c) ')
})

test('only a sign, a name and a target that says who or what it means is read as a name', () => {
  expect(namedAt('[@Bea](/p/0193b2c4-0000-7000-8000-00000000000b) rest')?.raw).toBe('[@Bea](/p/0193b2c4-0000-7000-8000-00000000000b)')
  expect(namedAt('[@Bea](https://bea.example)')).toBeNull()
  expect(namedAt('[#Bea](/p/0193b2c4-0000-7000-8000-00000000000b)')).toBeNull()
  expect(namedAt('[@General](/t/general)')).toBeNull()
  expect(namedAt('[Bea](/p/0193b2c4-0000-7000-8000-00000000000b)')).toBeNull()
})
