// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PostLinkButton from '../src/components/PostLinkButton.vue'

it('copies the canonical Post URL without carrying the current query or fragment', async () => {
  window.history.replaceState({}, '', '/t/lab/topics/essay?section=access#article-heading-2')
  const writeText = vi.fn().mockResolvedValue(undefined)
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })
  const wrapper = mount(PostLinkButton, { props: { tangentKey: 'lab', postId: 'opener', label: 'Copy link to article' } })
  await wrapper.find('button').trigger('click')
  await flushPromises()
  expect(writeText).toHaveBeenCalledWith(window.location.origin + '/t/lab/opener')
  expect(wrapper.find('[role="status"]').text()).toBe('Link copied')
  wrapper.unmount()
  window.history.replaceState({}, '', '/')
})

it('offers a selectable URL when clipboard access fails', async () => {
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } })
  const wrapper = mount(PostLinkButton, { props: { tangentKey: 'lab', postId: 'reply' } })
  await wrapper.find('button').trigger('click')
  await flushPromises()
  expect(wrapper.find('.post-link-fallback a').attributes('href')).toBe('/t/lab/reply')
  expect((wrapper.find('.post-link-fallback input').element as HTMLInputElement).value).toBe(window.location.origin + '/t/lab/reply')
  wrapper.unmount()
})
