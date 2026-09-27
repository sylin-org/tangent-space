// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import BlueskyPublishPanel from '../src/components/BlueskyPublishPanel.vue'

it('publishes only after explicit action and retains the same request identity after an uncertain response', async () => {
  const writes: unknown[] = []
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (String(path).endsWith('/connection')) return new Response(JSON.stringify({ status: 'connected', actorDid: 'did:plc:alice', canPublish: true, connectUrl: null }))
    writes.push(JSON.parse(String(init?.body)))
    if (writes.length === 1) throw new Error('Response lost. Retry to check this publication.')
    return new Response(JSON.stringify({ uri: 'at://did:plc:alice/app.bsky.feed.post/record', cid: 'cid', publishedAt: '2026-09-26', replayed: true }))
  }))
  const wrapper = mount(BlueskyPublishPanel, { props: { postId: 'post-1', accountDid: 'did:plc:alice', accountLabel: '@alice.example' } })
  await flushPromises()
  expect(writes).toEqual([])
  expect(wrapper.text()).toContain('public copy')
  expect(wrapper.text()).toContain('@alice.example')
  await wrapper.get('button.btn-primary').trigger('click')
  await flushPromises()
  await wrapper.get('button.btn-primary').trigger('click')
  await flushPromises()
  expect(writes).toEqual([{ postId: 'post-1', requestId: 'post:post-1' }, { postId: 'post-1', requestId: 'post:post-1' }])
  expect(wrapper.text()).toContain('already published')
})
