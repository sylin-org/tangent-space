// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import MarkdownComposer from '../src/components/MarkdownComposer.vue'

it('opens supported Markdown visually without rewriting source until an edit', async () => {
  const source = '## Article\n\nText with [a link](https://example.com) and ![an image](https://example.com/image.jpg).'
  const wrapper = mount(MarkdownComposer, { props: { modelValue: source, tangentKey: 'essays', identity: 'alice:essays', label: 'Opening thought' } })
  await flushPromises()
  expect(wrapper.find('.markdown-editor-surface').exists()).toBe(true)
  expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  await wrapper.findAll('.markdown-view-switch button')[1]!.trigger('click')
  expect((wrapper.find('.markdown-source').element as HTMLTextAreaElement).value).toBe(source)
  await wrapper.unmount()
})

it('keeps unsupported Markdown in source mode', async () => {
  const source = '| Name | Value |\n| --- | --- |\n| One | Two |'
  const wrapper = mount(MarkdownComposer, { props: { modelValue: source, tangentKey: 'essays', identity: 'alice:essays', label: 'Opening thought' } })
  await flushPromises()
  expect(wrapper.find('.markdown-source').exists()).toBe(true)
  expect(wrapper.text()).toContain('cannot preserve')
  expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  await wrapper.unmount()
})

it('cancels an image upload when its draft identity changes', async () => {
  let finish!: (response: Response) => void
  const fetch = vi.fn((_path: RequestInfo | URL, _init?: RequestInit) => new Promise<Response>(resolve => { finish = resolve }))
  vi.stubGlobal('fetch', fetch)
  const wrapper = mount(MarkdownComposer, { props: { modelValue: '', tangentKey: 'essays', identity: 'alice:essays', label: 'Opening thought' } })
  await flushPromises()
  const field = wrapper.find('input[type="file"]').element as HTMLInputElement
  const file = { type: 'image/png', size: 3, name: 'photo.png', arrayBuffer: async () => new Uint8Array([1, 2, 3]).buffer }
  Object.defineProperty(field, 'files', { configurable: true, value: [file] })
  await wrapper.find('input[type="file"]').trigger('change')
  await flushPromises()
  expect(fetch).toHaveBeenCalledTimes(1)
  expect(JSON.parse(String(fetch.mock.calls[0]![1]?.body))).toMatchObject({ tangentKey: 'essays', fileName: 'photo.png', content: 'AQID' })
  await wrapper.setProps({ identity: 'bob:essays' })
  finish(new Response(JSON.stringify({ url: '/media/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' })))
  await flushPromises()
  expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  expect(wrapper.emitted('busy')?.at(-1)).toEqual([false])
  await wrapper.unmount()
})
