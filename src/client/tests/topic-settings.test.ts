// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ref, shallowRef } from 'vue'
import { createTopicSettings } from '../src/composables/topic-settings'
import TopicSettingsPanel from '../src/components/TopicSettingsPanel.vue'
import AccessDecisionRow from '../src/components/AccessDecisionRow.vue'
import AccessPanel from '../src/components/AccessPanel.vue'
import type { SettingsResource } from '../src/composables/settings'

const request = vi.hoisted(() => vi.fn())
vi.mock('../src/composables/roles-api', () => ({ rolesRequest: request, rolesFor: async () => [
  { id: 'role:reader', name: 'Reader', color: '#a98df0' },
] }))

function topicResource(key = 'first'): SettingsResource {
  return {
    name: 'First', description: 'Original', open: `/t/tangent/topics/${key}`, canManage: true,
    tangent: { key: 'tangent', name: 'Tangent' },
    topic: { tangentKey: 'tangent', key, title: 'First', topic: 'Original',
      allowPostEditing: false, isLocked: false, readAudience: 'Restricted', canManage: true },
  }
}

beforeEach(() => { request.mockReset(); request.mockResolvedValue({ accepted: true }); sessionStorage.clear() })

describe('Topic settings', () => {
  it('reopens the access combobox from the keyboard with an announced active option', async () => {
    const wrapper = mount(AccessDecisionRow, { props: {
      decision: { key: 'see', label: 'Who can see', description: 'Readers', bypass: 'Stewards can always see.' },
      modelValue: null, parentTokens: ['role:reader'], effectiveTokens: ['role:reader'], topicContext: true,
      choices: [
        { id: 'role:reader', name: 'Reader', color: '#a98df0' },
        { id: 'role:member', name: 'Member', color: '#a98df0' },
      ],
    } })
    const input = wrapper.find('input[role="combobox"]')
    await input.trigger('focus')
    await input.trigger('keydown', { key: 'Escape' })
    expect(input.attributes('aria-expanded')).toBe('false')
    await input.trigger('keydown', { key: 'ArrowDown' })
    const listbox = wrapper.find('[role="listbox"]')
    expect(listbox.attributes('tabindex')).toBe('-1')
    expect(input.attributes('aria-controls')).toBe(listbox.attributes('id'))
    expect(input.attributes('aria-activedescendant')).toBe(wrapper.findAll('[role="option"]')[0]?.attributes('id'))
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(input.attributes('aria-activedescendant')).toBe(wrapper.findAll('[role="option"]')[1]?.attributes('id'))
    expect(wrapper.findAll('[role="option"]').every(option => option.attributes('tabindex') === '-1')).toBe(true)
    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([['role:member']])
  })

  it('saves details and participation independently using persisted values', async () => {
    const resource = shallowRef<SettingsResource | null>(topicResource())
    const form = createTopicSettings(resource)
    form.details.title = 'Renamed'
    form.participation.isLocked = true
    await form.saveDetails()
    expect(request).toHaveBeenCalledWith('/api/v1/tangents/tangent/topics/first/settings', expect.objectContaining({
      body: { title: 'Renamed', topic: 'Original' },
    }))
    expect(form.dirty.participation.value).toBe(true)
    await form.saveParticipation()
    expect(request).toHaveBeenLastCalledWith('/api/v1/tangents/tangent/topics/first/settings', expect.objectContaining({
      body: { allowPostEditing: false, isLocked: true },
    }))
  })

  it('keeps a failed draft and requires named confirmation to publish existing history', async () => {
    const resource = shallowRef<SettingsResource | null>(topicResource())
    const form = createTopicSettings(resource)
    form.details.topic = 'New description'
    request.mockRejectedValueOnce(new Error('Try again'))
    await form.saveDetails()
    expect(form.details.topic).toBe('New description')
    expect(form.state.details).toBe('error')
    expect(form.errors.details).toBe('Try again')
    await form.saveDetails()
    expect(form.state.details).toBe('saved')
    form.audience.value = 'Public'
    await form.saveAudience()
    expect(form.errors.audience).toMatch(/existing posts/)
    expect(request).toHaveBeenCalledTimes(2)
    form.publishHistory.value = true
    await form.saveAudience()
    expect(request).toHaveBeenLastCalledWith('/api/v1/tangents/tangent/topics/first/read-audience', expect.objectContaining({
      body: { audience: 'Public', publishExistingHistory: true },
    }))
  })

  it('restores unsaved forms after navigation and reload, scoped to the participant', async () => {
    const resource = shallowRef<SettingsResource | null>(topicResource())
    const actor = ref('participant-1')
    const form = createTopicSettings(resource, actor)
    form.details.title = 'Draft title'
    form.audience.value = 'Public'
    form.publishHistory.value = true
    resource.value = topicResource('second')
    await flushPromises()
    expect(form.details.title).toBe('First')
    resource.value = topicResource()
    await flushPromises()
    expect(form.details.title).toBe('Draft title')
    expect(form.audience.value).toBe('Public')
    expect(form.publishHistory.value).toBe(false)
    const reopened = createTopicSettings(shallowRef(topicResource()), actor)
    expect(reopened.details.title).toBe('Draft title')
    actor.value = 'participant-2'
    await flushPromises()
    expect(reopened.details.title).toBe('First')
  })

  it('restores only edited fields so newer server values survive a return', async () => {
    const actor = ref('participant-1')
    const original = createTopicSettings(shallowRef(topicResource()), actor)
    original.details.title = 'Draft title'
    const latest = topicResource()
    latest.topic!.topic = 'Description changed elsewhere'
    latest.topic!.isLocked = true
    const reopened = createTopicSettings(shallowRef(latest), actor)
    expect(reopened.details.title).toBe('Draft title')
    expect(reopened.details.topic).toBe('Description changed elsewhere')
    expect(reopened.participation.isLocked).toBe(true)
    expect(reopened.dirty.details.value).toBe(true)
    expect(reopened.dirty.participation.value).toBe(false)
  })

  it('restores unsaved Topic access rules when the page is reopened', async () => {
    request.mockResolvedValue({ selected: { see: null }, parent: { see: ['role:reader'] }, effective: { see: ['role:reader'] } })
    const settings = { activeTab: ref('context'), actorKey: ref('participant-1'), context: ref({
      kind: 'topic', tangent: 'tangent', topic: 'first', title: 'Topic settings', eyebrow: '', intro: '',
      accessPath: '/api/v1/tangents/tangent/topics/first/access',
      decisions: [{ key: 'see', label: 'Who can see', description: 'Read', bypass: 'Stewards can see.' }],
    }) }
    const first = mount(AccessPanel, { props: { settings: settings as never } })
    await flushPromises()
    await first.find('input[role="combobox"]').trigger('focus')
    await first.findAll('[role="option"]').find(option => option.text().includes('Reader'))!.trigger('click')
    expect(first.text()).toContain('Unsaved access rules are kept')
    first.unmount()
    const reopened = mount(AccessPanel, { props: { settings: settings as never } })
    await flushPromises()
    expect(reopened.find('.access-action-state').text()).toBe('Set here')
    expect(reopened.find('.access-chip-row').text()).toContain('Reader')
  })

  it('keeps intended Topic rules and requires explicit history consent after a publication refusal', async () => {
    const current = { selected: { see: ['role:reader'] }, parent: { see: ['everyone'] }, effective: { see: ['role:reader'] } }
    request.mockImplementation((_path: string, options?: { method?: string; body?: unknown }) => {
      if (options?.method === 'PUT') {
        if ((options.body as { publishExistingHistory: boolean }).publishExistingHistory) return Promise.resolve({ ...current, selected: { see: ['everyone'] }, effective: { see: ['everyone'] } })
        return Promise.reject(Object.assign(new Error('Confirm publication of existing posts.'), { code: 'history_publication_required' }))
      }
      return Promise.resolve(current)
    })
    const settings = { activeTab: ref('access'), actorKey: ref('participant-1'), context: ref({
      kind: 'topic', tangent: 'tangent', topic: 'first', title: 'Topic settings', eyebrow: '', intro: '',
      accessPath: '/api/v1/tangents/tangent/topics/first/access',
      decisions: [{ key: 'see', label: 'Who can see', description: 'Read', bypass: 'Stewards can see.' }],
    }) }
    const wrapper = mount(AccessPanel, { props: { settings: settings as never } })
    await flushPromises()
    await wrapper.find('input[role="combobox"]').trigger('focus')
    await wrapper.findAll('[role="option"]').find(option => option.text().includes('everyone'))!.trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('Confirm publication of existing posts.')
    const consent = wrapper.find('input[type="checkbox"]')
    expect(consent.exists()).toBe(true)
    await consent.setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    const saves = request.mock.calls.filter(([, options]) => options?.method === 'PUT')
    expect(saves.map(([, options]) => options.body)).toEqual([
      { access: { see: ['role:reader', 'everyone'] }, publishExistingHistory: false },
      { access: { see: ['role:reader', 'everyone'] }, publishExistingHistory: true },
    ])
  })

  it('does not show an old Topic’s history refusal on the next Topic', async () => {
    let rejectSave!: (error: Error) => void
    request.mockImplementation((_path: string, options?: { method?: string }) => options?.method === 'PUT'
      ? new Promise((_resolve, reject) => { rejectSave = reject })
      : Promise.resolve({ selected: { see: ['role:reader'] }, parent: { see: ['everyone'] }, effective: { see: ['role:reader'] } }))
    const context = ref({
      kind: 'topic', tangent: 'tangent', topic: 'first', title: 'Topic settings', eyebrow: '', intro: '',
      accessPath: '/api/v1/tangents/tangent/topics/first/access',
      decisions: [{ key: 'see', label: 'Who can see', description: 'Read', bypass: 'Stewards can see.' }],
    })
    const wrapper = mount(AccessPanel, { props: { settings: { activeTab: ref('access'), actorKey: ref('participant-1'), context } as never } })
    await flushPromises()
    await wrapper.find('input[role="combobox"]').trigger('focus')
    await wrapper.findAll('[role="option"]').find(option => option.text().includes('everyone'))!.trigger('click')
    await wrapper.find('form').trigger('submit')
    context.value = { ...context.value, topic: 'second', accessPath: '/api/v1/tangents/tangent/topics/second/access' }
    await flushPromises()
    rejectSave(Object.assign(new Error('Publish first history'), { code: 'history_publication_required' }))
    await flushPromises()
    expect(wrapper.text()).not.toContain('Publish first history')
    expect(wrapper.find('input[type="checkbox"]').exists()).toBe(false)
  })

  it('does not apply pending saves to another Topic after navigation', async () => {
    let release!: () => void
    request.mockImplementationOnce(() => new Promise<void>(resolve => { release = resolve }))
    const resource = shallowRef<SettingsResource | null>(topicResource())
    const form = createTopicSettings(resource)
    form.details.title = 'First edited'
    const first = form.saveDetails()
    form.participation.isLocked = true
    const second = form.saveParticipation()
    await flushPromises()
    resource.value = topicResource('second')
    await flushPromises()
    release()
    await Promise.all([first, second])
    expect(request).toHaveBeenCalledTimes(2)
    expect(request.mock.calls.every(call => call[0] === '/api/v1/tangents/tangent/topics/first/settings')).toBe(true)
    expect(form.saved.title).toBe('First')
    expect(form.saved.isLocked).toBe(false)
  })

  it('renders the single routed page with local forms and a way back to the Topic', async () => {
    const resource = shallowRef<SettingsResource | null>(topicResource())
    const wrapper = mount(TopicSettingsPanel, {
      props: { settings: { resource } as never },
      global: { stubs: { AccessPanel: true } },
    })
    expect(wrapper.findAll('h1')).toHaveLength(1)
    expect(wrapper.text()).toContain('Reading & participation')
    expect(wrapper.text()).toContain('People & roles')
    expect(wrapper.find('.settings-workspace-back').exists()).toBe(true)
    await wrapper.find('.settings-workspace-back').trigger('click')
    expect(wrapper.emitted('return')).toHaveLength(1)
    expect(wrapper.findAll('form')).toHaveLength(4)
    expect(wrapper.find('#topic-admin').exists()).toBe(false)
    await wrapper.find('input[value="Public"]').setValue()
    expect(wrapper.text()).toContain('existing posts will become public')
    expect(wrapper.find('.check-row input[type="checkbox"]').exists()).toBe(true)
  })

  it('uses the same forms in the compact Article Settings rail', async () => {
    const resource = shallowRef<SettingsResource | null>(topicResource())
    resource.value!.topic!.presentation = 'article'
    const wrapper = mount(TopicSettingsPanel, {
      props: { settings: { resource } as never, compact: true },
      global: { stubs: { AccessPanel: true } },
    })
    expect(wrapper.findAll('h1')).toHaveLength(0)
    expect(wrapper.find('#article-settings-title').text()).toBe('Article Settings')
    expect(wrapper.findAll('form')).toHaveLength(4)
    expect(wrapper.text()).toContain('Article title')
    await wrapper.find('#article-settings-close').trigger('click')
    expect(wrapper.emitted('return')).toHaveLength(1)
  })
})
