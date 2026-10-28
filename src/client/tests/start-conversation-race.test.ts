// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import StartConversationPanel from '../src/components/StartConversationPanel.vue'
import { createPinia, setActivePinia } from 'pinia'
import { useRouter } from '../src/stores/router'

it('keeps the new Tangent draft when an earlier creation confirms late', async () => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  const router = useRouter()
  router.apply('/t/first/topics')
  let finish!: (response: Response) => void
  vi.stubGlobal('fetch', vi.fn((_path: RequestInfo | URL, init?: RequestInit) => init?.method === 'POST' ? new Promise<Response>(resolve => { finish = resolve }) : Promise.resolve(new Response(JSON.stringify({ canPublish: false })))))
  const wrapper = mount(StartConversationPanel, { props: {
    tangentKey: 'first', tangentName: 'First', author: 'Alice', actorKey: 'alice', audience: 'Members', kind: 'discussions', router,
  } })
  await wrapper.find('button').trigger('click')
  await wrapper.find('input[maxlength="120"]').setValue('First title')
  await wrapper.find('textarea').setValue('First opening')
  const firstSubmit = wrapper.find('form').trigger('submit')
  await flushPromises()
  router.apply('/t/second/topics')
  await wrapper.setProps({ tangentKey: 'second', tangentName: 'Second' })
  await wrapper.find('button').trigger('click')
  await wrapper.find('input[maxlength="120"]').setValue('Second title')
  await wrapper.find('textarea').setValue('Second opening')
  finish(new Response(JSON.stringify({ topicRef: 'https://example.test::first::created' })))
  await firstSubmit
  await flushPromises()
  expect(router.route).toEqual({ kind: 'topics', tangent: 'second' })
  expect((wrapper.find('input[maxlength="120"]').element as HTMLInputElement).value).toBe('Second title')
  expect(sessionStorage.getItem('tangent:start:alice:second')).toContain('Second opening')
  wrapper.unmount()
})

it('restores and submits a long opening thought without clipping it', async () => {
  setActivePinia(createPinia())
  vi.stubGlobal('scrollTo', vi.fn())
  sessionStorage.clear()
  const router = useRouter()
  router.apply('/t/essays/topics')
  const text = 'A long opening paragraph. '.repeat(300).trim()
  sessionStorage.setItem('tangent:start:alice:essays', JSON.stringify({ title: 'Long article', text, format: 'markdown' }))
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method !== 'POST') return new Response(JSON.stringify({ canPublish: false }))
    writes.push(JSON.parse(String(init?.body)))
    return new Response(JSON.stringify({ topicRef: 'https://example.test::essays::long-article' }))
  }))
  const wrapper = mount(StartConversationPanel, { props: {
    tangentKey: 'essays', tangentName: 'Essays', author: 'Alice', actorKey: 'alice', audience: 'Everyone', kind: 'articles', router,
  } })
  await flushPromises()
  await vi.waitFor(() => expect(wrapper.findAll('.markdown-view-switch button')).toHaveLength(2))
  await wrapper.findAll('.markdown-view-switch button')[1]!.trigger('click')
  expect((wrapper.find('.markdown-source').element as HTMLTextAreaElement).value).toBe(text)
  await wrapper.find('form').trigger('submit')
  await flushPromises()
  expect(writes[0]).toMatchObject({ title: 'Long article', text, format: 'markdown' })
  expect(writes[0]).not.toHaveProperty('presentation')
  wrapper.unmount()
})

it('starts an Article with the rich composer and keeps Discussion editors exclusive', async () => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  const router = useRouter()
  router.apply('/t/essays/topics')
  const wrapper = mount(StartConversationPanel, { props: {
    tangentKey: 'essays', tangentName: 'Essays', author: 'Alice', actorKey: 'alice', audience: 'Everyone', kind: 'articles', router,
  } })
  await wrapper.find('button').trigger('click')
  expect(wrapper.find('.composer-format-toggle').attributes('aria-pressed')).toBe('true')
  await vi.waitFor(() => expect(wrapper.find('.markdown-view-switch').exists()).toBe(true))
  expect(wrapper.text()).toContain('The first uploaded image becomes the article cover.')
  await wrapper.find('input[maxlength="120"]').setValue('Draft title')
  await wrapper.setProps({ kind: 'discussions' })
  expect(wrapper.find('.markdown-view-switch').exists()).toBe(true)
  expect(wrapper.find('textarea[placeholder="Give people somewhere to begin…"]').exists()).toBe(false)
  wrapper.unmount()
})
