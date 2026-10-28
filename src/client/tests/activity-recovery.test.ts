// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { createActivity } from '../src/composables/activity'

const transport = vi.hoisted(() => ({
  start: vi.fn(), stop: vi.fn(), setVisible: vi.fn(), setProfiles: vi.fn(), snapshot: vi.fn(),
}))
const create = vi.hoisted(() => vi.fn())
vi.mock('../src/activity/coordinator', () => ({ activityCoordinator: { create } }))

it('reopens activity under the new participant after identity recovery', async () => {
  create.mockReturnValue(transport)
  const conversation = { selectedTopic: ref(null), reset: vi.fn() }
  const site = {
    state: 'signedIn', site: { participant: { participantRef: 'alice' } },
    load: vi.fn(async function (this: { site: { participant: { participantRef: string } } }) {
      this.site.participant.participantRef = 'bob'
    }),
  }
  const onIdentityRecovered = vi.fn(async () => {})
  const activity = createActivity({
    conversation: conversation as never,
    directories: { tangents: [] } as never,
    site: site as never,
    router: { route: { kind: 'home' } } as never,
    onIdentityRecovered,
  })
  activity.start()
  expect(transport.start).toHaveBeenCalledWith({ participant: 'alice' })
  const handlers = create.mock.calls[0]![0] as { onIdentity: () => Promise<void> }
  await handlers.onIdentity()
  expect(conversation.reset).toHaveBeenCalledOnce()
  expect(onIdentityRecovered).toHaveBeenCalledOnce()
  expect(activity.recovering.value).toBe(false)
  expect(transport.start).toHaveBeenLastCalledWith({ participant: 'bob' })
})
