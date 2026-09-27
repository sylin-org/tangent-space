// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import BlueskyDirectory from '../src/components/BlueskyDirectory.vue'

it('offers anonymous readers ordinary links to bounded public author pages', async () => {
  const fetch = vi.fn(async () => new Response(JSON.stringify({ authors: [{
    actorDid: 'did:plc:alice', handle: 'alice.example', refreshedAt: '2026-09-26T12:00:00Z', postsUrl: '/sources/bluesky/did%3Aplc%3Aalice',
  }] })))
  vi.stubGlobal('fetch', fetch)
  const wrapper = mount(BlueskyDirectory)
  await flushPromises()
  expect(fetch).toHaveBeenCalledWith('/api/v1/content/bluesky/authors?limit=25', expect.objectContaining({ method: 'GET' }))
  expect(wrapper.get('a').attributes('href')).toBe('/sources/bluesky/did%3Aplc%3Aalice')
  expect(wrapper.text()).toContain('@alice.example')
  expect(wrapper.find('button').exists()).toBe(false)
})
