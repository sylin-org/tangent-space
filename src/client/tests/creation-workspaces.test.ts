// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { useRouter, parseWorkspaceRoute } from '../src/stores/router'
import StartConversationPanel from '../src/components/StartConversationPanel.vue'
import NewTangentPanel from '../src/components/NewTangentPanel.vue'
import { previewMarkdown } from '../src/composables/draft-preview'

afterEach(() => { document.body.innerHTML = ''; sessionStorage.clear(); vi.unstubAllGlobals() })

it('routes dedicated creation workspaces through the SPA', () => {
  expect(parseWorkspaceRoute('/tangents/new')).toEqual({ kind: 'new-tangent' })
  expect(parseWorkspaceRoute('/t/field-notes/new')).toEqual({ kind: 'new-topic', tangent: 'field-notes' })
})

it('keeps Tangent drafts per participant and submits all selected creation fields', async () => {
  sessionStorage.clear()
  const wrapper = mount(NewTangentPanel, { props: { actorKey: 'alice', error: '', busy: false } })
  await wrapper.get('input[name=name]').setValue('Field Notes')
  await wrapper.get('input[name=motto]').setValue('Small things, noticed at work.')
  await wrapper.get('button[aria-label="Moss & Signal"]').trigger('click')
  await wrapper.get('input[value="#3b8065"]').setValue(true)
  await wrapper.get('.creation-audience-summary button').trigger('click')
  await wrapper.get('input[value=Admitted]').setValue(true)
  await wrapper.get('textarea[name=description]').setValue('A place for observations.')
  const data = Object.fromEntries(new FormData(wrapper.get('form').element as HTMLFormElement))
  expect(data).toMatchObject({ name: 'Field Notes', key: 'field-notes', motto: 'Small things, noticed at work.', description: 'A place for observations.', kind: 'articles', accent: '#3b8065', artwork: '/tangent-art/moss-and-signal.png', readAudience: 'Admitted' })
  wrapper.unmount()
  const restored = mount(NewTangentPanel, { props: { actorKey: 'alice', error: '', busy: false } })
  expect((restored.get('input[name=name]').element as HTMLInputElement).value).toBe('Field Notes')
  await restored.setProps({ actorKey: 'bob' })
  expect((restored.get('input[name=name]').element as HTMLInputElement).value).toBe('')
  expect(sessionStorage.getItem('tangent:new-tangent:alice')).toContain('Field Notes')
  restored.unmount()
})

it('previews safely, then publishes the explicitly selected article audience and subtitle', async () => {
  setActivePinia(createPinia()); sessionStorage.clear(); vi.stubGlobal('scrollTo', vi.fn()); vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => { callback(0); return 1 })
  document.body.innerHTML = '<div id="creation-index-slot"></div><div id="creation-path-actions"></div>'
  const router = useRouter(); router.apply('/t/essays/new')
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (_url: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method !== 'POST') return new Response(JSON.stringify({ canPublish: false }))
    writes.push(JSON.parse(String(init.body)))
    return new Response(JSON.stringify({ topicRef: 'https://example.test::essays::published-article', postRef: 'https://example.test::post-one' }))
  }))
  const wrapper = mount(StartConversationPanel, { attachTo: document.body, props: { tangentKey: 'essays', tangentName: 'Essays', actorKey: 'alice', author: 'Alice', audience: 'Public', kind: 'articles', router, dedicated: true }, global: { stubs: { MarkdownComposer: { props: ['modelValue'], emits: ['update:modelValue'], template: '<textarea data-editor :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)"></textarea>' } } } })
  await flushPromises()
  await wrapper.get('input[maxlength="120"]').setValue('A considered article')
  await wrapper.get('input[maxlength="240"]').setValue('A useful subtitle')
  await wrapper.get('[data-editor]').setValue('## The first section\n\nA **bold** thought.\n\n<script>alert(1)</script>')
  const pathButtons = () => Array.from(document.querySelectorAll<HTMLButtonElement>('#creation-path-actions button'))
  pathButtons().find(button => button.textContent?.trim() === 'Preview')!.click(); await nextTick()
  expect(wrapper.get('.creation-article-preview strong').text()).toBe('bold')
  expect(wrapper.find('.creation-article-preview script').exists()).toBe(false)
  expect(writes).toHaveLength(0)
  pathButtons().find(button => button.textContent?.includes('Publish'))!.click(); await nextTick()
  expect(wrapper.find('.creation-publish-panel').exists()).toBe(true)
  expect(writes).toHaveLength(0)
  await wrapper.get('input[type=radio][value=Restricted]').setValue(true)
  await wrapper.get('.creation-publish-panel button[type=submit]').trigger('submit')
  await flushPromises()
  expect(writes).toHaveLength(1)
  expect(writes[0]).toMatchObject({ title: 'A considered article', subtitle: 'A useful subtitle', audience: 'Restricted', format: 'markdown' })
  expect(router.route).toEqual({ kind: 'topic', tangent: 'essays', topic: 'published-article' })
  expect(sessionStorage.getItem('tangent:start:alice:essays')).toBeNull()
  wrapper.unmount()
})

it('removes active HTML and unsafe link/image protocols from draft previews', () => {
  const html = previewMarkdown('<img src=x onerror=alert(1)>\n\n[bad](javascript:alert)\n\n![bad](data:image/svg+xml,evil)\n\n![A view](/media/safe)\n\n*A caption*')
  const document = new DOMParser().parseFromString(html, 'text/html')
  expect(document.querySelector('[onerror]')).toBeNull()
  expect(document.querySelector('a')?.getAttribute('href')).toBe('#')
  expect(document.querySelectorAll('img')).toHaveLength(1)
  expect(document.querySelector('figure figcaption')?.textContent).toBe('A caption')
})
