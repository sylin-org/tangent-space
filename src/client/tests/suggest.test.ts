// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest'
import { mountIslands } from '../islands'
import { suggesting, typedAt, type Suggestion } from '../suggest'

// Typing @ or # in a body field offers who and where from the Tangent's suggestions, as a list under the caret that the
// keys move through and pick from; never a dialog.

const people: Suggestion[] = [
  { kind: 'person', insert: 'bea.example', name: 'Bea', note: null },
  { kind: 'role', insert: 'moderator', name: 'Moderator', note: '2 people' },
]

afterEach(() => {
  document.body.replaceChildren()
  vi.restoreAllMocks()
})

test('a sign that begins a word, and what follows it, is what the caret stands after', () => {
  expect(typedAt('Hi @be')).toEqual({ sign: '@', prefix: 'be' })
  expect(typedAt('See #')).toEqual({ sign: '#', prefix: '' })
  expect(typedAt('(@mo')).toEqual({ sign: '@', prefix: 'mo' })
  expect(typedAt('ada@bsky')).toBeNull()
  expect(typedAt('## heading')).toBeNull()
  expect(typedAt('Hi @be ')).toBeNull()
})

test('the list opens under the field, the keys move through it, and Enter writes the pick after its sign', async () => {
  const read = vi.spyOn(suggesting, 'read').mockResolvedValue(people)
  document.body.innerHTML = `<form><textarea id="body" data-island="suggest" data-props='{"address":"/t/workshop/suggest?topic=lanterns"}'></textarea></form>`
  mountIslands(document)
  const field = document.querySelector<HTMLTextAreaElement>('#body')!
  field.focus()
  field.value = 'Thanks @b'
  field.setSelectionRange(9, 9)
  field.dispatchEvent(new Event('input', { bubbles: true }))
  await vi.waitFor(() => expect(document.querySelector('.suggestions')?.hasAttribute('hidden')).toBe(false))

  expect(read.mock.calls[0]![0]).toBe('/t/workshop/suggest?topic=lanterns&q=%40b')
  const options = [...document.querySelectorAll('[role="option"]')]
  expect(options.map((row) => row.textContent)).toEqual(['Bea@bea.example', 'Moderator@moderatorrole · 2 people'])
  expect(field.getAttribute('aria-expanded')).toBe('true')

  field.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
  expect(options[1]!.getAttribute('aria-selected')).toBe('true')
  field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
  expect(field.value).toBe('Thanks @moderator ')
  expect(document.querySelector('.suggestions')?.hasAttribute('hidden')).toBe(true)

  field.value = 'Thanks @moderator #'
  field.setSelectionRange(19, 19)
  field.dispatchEvent(new Event('input', { bubbles: true }))
  await vi.waitFor(() => expect(document.querySelector('.suggestions')?.hasAttribute('hidden')).toBe(false))
  field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  expect(document.querySelector('.suggestions')?.hasAttribute('hidden')).toBe(true)
})
