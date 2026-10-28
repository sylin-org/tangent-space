// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createConversation } from '../src/composables/conversation'
import { createModeration } from '../src/composables/moderation'
import { useRouter } from '../src/stores/router'
import { useSite } from '../src/stores/site'
import StewardshipPanel from '../src/components/StewardshipPanel.vue'

const serverRef = 'http://server.example'

function topicResource(steward: boolean) {
  return {
    serverRef, stewardship: steward,
  }
}

const casesPage = {
      cases: [
        { caseRef: `${serverRef}::small-hours::first::case_${'a'.repeat(64)}`, state: 'open', testimonyCount: 2, firstReportedAt: new Date(Date.now() - 3600000).toISOString() },
        { caseRef: 'garbage', state: 'open', testimonyCount: 0 },
      ],
      nextPage: null,
}

function stubFetch(steward: boolean) {
  return vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    if (url.includes('?limit=1')) return json(topicResource(steward))
    if (url.includes('/moderation/cases?page=')) return json(casesPage)
    if (url.endsWith('/reports')) return json({ accepted: true })
    throw new Error('unexpected fetch ' + url)
  })
}

async function mountStewardship(steward: boolean) {
  setActivePinia(createPinia())
  vi.stubGlobal('fetch', stubFetch(steward))
  const site = useSite()
  const router = useRouter()
  router.apply('/t/small-hours/topics/first')
  site.state = 'signedIn'
  const conversation = createConversation(router)
  // Pretend the Topic opened, with the fields the wire carries.
  conversation.selectedTopic.value = {
    key: 'first', tangentKey: 'small-hours', title: 'First', canRead: true, canWrite: false, isLocked: false,
    policyRevision: 3, sitePolicyRevision: 1,
  } as never
  const moderation = createModeration(conversation)
  moderation.wireViewer(() => 'viewer-1')
  const wrapper = mount(StewardshipPanel, { props: { moderation }, global: { plugins: [createPinia()] } })
  await flushPromises()
  return { wrapper, moderation }
}

describe('the stewardship panel', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', '/t/small-hours/topics/first')
  })

  it('appears for a steward and lists the well-formed cases only', async () => {
    const { wrapper } = await mountStewardship(true)
    const rows = wrapper.findAll('.moderation-case-row')
    expect(rows.length).toBe(1)
    expect(rows[0]!.text()).toContain('2 participant reports')
    expect(wrapper.text()).toContain('Current')
  })

  it('stays hidden when the Topic grants no stewardship', async () => {
    const { moderation } = await mountStewardship(false)
    expect(moderation.stewardshipAvailable.value).toBe(false)
    expect(moderation.cases.value.length).toBe(0)
  })

  it('lets an ordinary participant send a report without reading the private case queue', async () => {
    const { moderation } = await mountStewardship(false)
    moderation.openReport('post-42', 'Another participant')
    moderation.reportStatement.value = 'This post shares someone else’s private information.'
    await moderation.submitReport()
    expect(moderation.reportVisible.value).toBe(false)
    expect(moderation.announcement.value).toBe('Report sent privately to this Topic’s stewards.')
    expect(moderation.reportStatus.value).toBe('')
    const requests = vi.mocked(fetch).mock.calls
    const report = requests.find(([url]) => String(url).endsWith('/reports'))
    expect(JSON.parse(String(report?.[1]?.body))).toMatchObject({
      postRef: `${serverRef}::small-hours::first::post-42`,
      statement: 'This post shares someone else’s private information.',
    })
    expect(requests.some(([url]) => String(url).includes('/moderation/cases'))).toBe(false)
  })
})
