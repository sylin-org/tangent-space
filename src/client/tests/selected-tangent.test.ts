// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { reactive, ref, nextTick } from 'vue'
import { createSelectedTangent } from '../src/composables/selected-tangent'
import type { Directories } from '../src/composables/directories'

it('describes an active Articles Tangent outside the bounded directory page', async () => {
  const key = ref<string | null>('far-essays')
  const actor = ref('visitor')
  const directories = reactive({ tangents: [{ key: 'first', name: 'First', kind: 'discussions' }] }) as Directories
  const fetch = vi.fn(async () => new Response(JSON.stringify({ key: 'far-essays', name: 'Far Essays', kind: 'articles', canCreateTopic: false })))
  vi.stubGlobal('fetch', fetch)
  const selected = createSelectedTangent(directories, () => key.value, () => actor.value)
  await vi.waitFor(() => expect(selected.current.value?.kind).toBe('articles'))
  expect(fetch).toHaveBeenCalledWith('/api/v1/tangents/far-essays', expect.objectContaining({ credentials: 'same-origin' }))
  expect(directories.tangents).toHaveLength(1)
  vi.unstubAllGlobals()
})

it('does not reuse another account or stale route descriptor', async () => {
  const key = ref<string | null>('essays')
  const actor = ref('alice')
  const directories = reactive({ tangents: [] }) as unknown as Directories
  const responses: Array<(value: Response) => void> = []
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => responses.push(resolve))))
  const selected = createSelectedTangent(directories, () => key.value, () => actor.value)
  await nextTick()
  actor.value = 'bob'
  await nextTick()
  responses[0]!(new Response(JSON.stringify({ key: 'essays', name: 'Alice', kind: 'articles' })))
  await nextTick()
  expect(selected.current.value).toBeNull()
  responses[1]!(new Response(JSON.stringify({ key: 'essays', name: 'Bob', kind: 'discussions' })))
  await vi.waitFor(() => expect(selected.current.value?.name).toBe('Bob'))
  key.value = 'another'
  await nextTick()
  expect(selected.current.value).toBeNull()
  vi.unstubAllGlobals()
})
