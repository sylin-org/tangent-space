import { httpRequest } from '../api'
import { computed, reactive, ref } from 'vue'
import { activityCoordinator, type ActivityCoordinator } from '../activity/coordinator'
import type { ActivitySnapshot, ActivityTopic } from '../activity/transport'
import type { Conversation } from './conversation'
import type { Directories } from './directories'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'

// The participant activity consumer, The
// origin connection lives in the coordinator; this maps its snapshots onto the
// directory marks, the elsewhere strip, the open conversation's refresh, and
// the connection badge.

export interface TopicActivity {
  unreadCount: number
  directReplies: number
  lastSequence: number
  readSequence: number
  unreadCountCapped: boolean
}

const EMPTY_ACTIVITY: TopicActivity = { unreadCount: 0, directReplies: 0, lastSequence: 0, readSequence: 0, unreadCountCapped: false }

export function createActivity(options: {
  conversation: Conversation
  directories: Directories
  site: SiteStore
  router: RouterStore
}) {
  const { conversation, directories, site, router } = options

  const activityByTopic = reactive(new Map<string, TopicActivity>())
  const connection = reactive({
    label: 'Connecting…',
    state: 'connecting',
    title: '',
    overviewNote: '',
  })
  const needsRefresh = ref(true)
  const recovering = ref(false)

  let transport: ActivityCoordinator | null = null
  let signature = ''
  let automaticRecoveries = 0

  const activityFor = (key: string): TopicActivity => activityByTopic.get(key) ?? EMPTY_ACTIVITY

  function connectionStatus(message: string, state = connection.state): void {
    const badgeLabels: Record<string, string> = {
      live: 'Live updates', polling: 'Polling for updates', reconnecting: 'Reconnecting…', paused: 'Updates paused', stopped: 'Offline', error: 'Offline',
    }
    connection.label = badgeLabels[state] ?? 'Connecting…'
    connection.state = state
    connection.title = message
  }

  /** The open conversation refreshes when its events arrive; the directory re-marks itself reactively. */
  async function applySnapshot(snapshot: ActivitySnapshot, context: { signal?: AbortSignal; isCurrent(): boolean }): Promise<boolean> {
    const selectedTopic = conversation.selectedTopic.value
    const previousSequence = selectedTopic ? activityFor(selectedTopic.key).lastSequence : undefined
    const next = JSON.stringify({ topics: snapshot.topics, truncated: snapshot.topicsTruncated, reset: snapshot.resetRequired })
    const changed = next !== signature
    signature = next
    activityByTopic.clear()
    for (const topic of Array.isArray(snapshot.topics) ? snapshot.topics : []) {
      if (typeof topic.topicKey === 'string') {
        activityByTopic.set(topic.topicKey, {
          unreadCount: topic.unreadCount,
          directReplies: topic.directReplies,
          lastSequence: topic.lastSequence,
          readSequence: topic.readSequence,
          unreadCountCapped: topic.unreadCountCapped,
        })
      }
    }
    connection.overviewNote = snapshot.topicsIncomplete ? 'This overview is incomplete because the directory reached its current scan limit.'
      : snapshot.topicsTruncated ? 'This overview shows up to 100 visible topics.'
        : snapshot.resetRequired ? 'Earlier activity markers have been reset.' : ''

    // The open selectedTopic reads on; a watch/mute policy may also remove topics from
    // activity. Absence is not an ACL result — re-read a missing open selectedTopic.
    const selected = selectedTopic ? activityByTopic.get(selectedTopic.key) : undefined
    if (selectedTopic?.canRead && !selected && (changed || snapshot.resetRequired)) {
      const key = selectedTopic.key
      try {
        const current = await httpRequest(`/api/v1/tangents/${encodeURIComponent(selectedTopic.tangentKey)}/topics/${encodeURIComponent(key)}`, {
          credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }, signal: context.signal,
        })
        if (!context.isCurrent()) return false
        if (!current.ok) throw Object.assign(new Error('topic-read-failed'), { status: current.status })
        const value = (await current.json()) as Record<string, unknown>
        if (context.isCurrent() && conversation.selectedTopic.value?.key === key) conversation.selectedTopic.value = value as never
      } catch (error) {
        if (!context.isCurrent()) return false
        const status = (error as Error & { status?: number }).status
        if ([401, 403, 404].includes(status ?? 0)) conversation.selectedTopic.value = null
        else throw error
      }
    }

    const shouldRefresh =
      selectedTopic &&
      (snapshot.resetRequired ||
        (Array.isArray(snapshot.events) && snapshot.events.some(event => event.topicKey === selectedTopic.key)) ||
        selected?.lastSequence !== previousSequence)
    const messageEvent =
      Array.isArray(snapshot.events) && snapshot.events.some(event => event.topicKey === selectedTopic?.key && ['PostEdited', 'PostDeleted'].includes(event.kind))
    if ((messageEvent || snapshot.resetRequired || needsRefresh.value || (router.route.kind === 'post' && shouldRefresh)) && selectedTopic?.canRead) {
      await conversation.refreshOpenHistory(false, context)
    } else if (shouldRefresh && selectedTopic.canRead && !conversation.nextCursor && conversation.resumeCursor) {
      await conversation.historyPage(conversation.resumeCursor, undefined, selectedTopic.key, false, context)
    }

    if (!context.isCurrent()) return false
    if (snapshot.resetRequired || snapshot.events?.some(event => event.kind === 'ParticipantChanged')) {
      await site.loadServer().catch(() => {})
    }
    if (
      snapshot.resetRequired ||
      snapshot.events?.some(event => ['TangentChanged', 'TopicChanged', 'MembershipChanged', 'ParticipantChanged'].includes(event.kind))
    ) {
      await directories.loadTangents().catch(() => {})
    }
    return context.isCurrent()
  }

  /** The server pushed an identity change: re-run the welcome flow as the current identity. */
  function reloadIdentity(): void {
    if (recovering.value) return
    recovering.value = true
    stop()
    conversation.reset()
    activityByTopic.clear()
    signature = ''
    connectionStatus('Your signed-in account changed. Updating…', 'connecting')
    automaticRecoveries += 1
    if (automaticRecoveries > 1) {
      connectionStatus('Your session or access needs attention. Sign in or retry to reconnect.', 'error')
      return
    }
    void site.load()
  }

  /** The profile stream decorates names and avatars across the page (profile-live's feed). */
  const profileEvents = ref<Array<Record<string, unknown>>>([])

  function start(): void {
    const participantRef = site.site?.participant?.participantRef
    if (!participantRef || recovering.value || ['sign-in', 'onboarding'].includes(router.route.kind)) return
    if (!transport) {
      transport = activityCoordinator.create({
        onSnapshot: async (snapshot, context) => {
          if (recovering.value || !context.isCurrent() || context.participant !== site.site?.participant?.participantRef) return false
          try {
            const accepted = await applySnapshot(snapshot, context)
            if (accepted) {
              automaticRecoveries = 0
              needsRefresh.value = false
            }
            return accepted
          } catch (error) {
            if (context.isCurrent()) {
              needsRefresh.value = true
              signature = ''
            }
            throw error // Failed invalidation must not advance the transport checkpoint.
          }
        },
        onIdentity: () => reloadIdentity(),
        onEvent(name, value) {
          if (name === 'profile') profileEvents.value = [...profileEvents.value.slice(-31), value as Record<string, unknown>]
        },
        onState: state => {
          const live = state.status === 'live'
          if (recovering.value) return
          if (live) {
            const message =
              state.mode === 'poll' ? 'Activity is connected using polling. Live streaming will be retried.' : 'Live activity is connected.'
            connectionStatus(message + (connection.overviewNote ? ' ' + connection.overviewNote : ''), state.mode === 'poll' ? 'polling' : 'live')
          } else if (state.status === 'paused') connectionStatus('Updates are paused while this page is hidden.', 'paused')
          else if (state.status === 'reconnecting') connectionStatus('Activity is reconnecting. Displayed information may be stale.', 'reconnecting')
          else if (state.status === 'identity') connectionStatus('Your session is being checked. Displayed information may be stale.', 'connecting')
          else connectionStatus('Activity is connecting. Displayed information may be stale.', state.status)
        },
      })
    }
    transport.setVisible(!document.hidden)
    transport.start({ participant: participantRef })
  }

  function stop(): void {
    needsRefresh.value = true
    transport?.stop()
  }

  /** Subscribe the shared connection to the profile-cache announcements for these DIDs. */
  function setProfiles(dids: string[]): boolean {
    return transport?.setProfiles(dids) ?? false
  }

  function setVisible(value: boolean): void {
    transport?.setVisible(value)
  }

  /** The elsewhere strip: other Topics with unread activity or replies to the viewer. */
  const elsewhere = computed(() => {
    const selectedTopic = conversation.selectedTopic.value
    return directories.tangents
      .flatMap(tangent => (tangent.topics ?? []).map(topic => ({ tangent, topic, activity: activityFor(topic.key) })))
      .filter(entry => entry.topic.key !== selectedTopic?.key && (entry.activity.unreadCount > 0 || entry.activity.directReplies > 0))
      .sort((a, b) => b.activity.directReplies - a.activity.directReplies)
  })

  return {
    activityByTopic, connection, elsewhere, profileEvents, needsRefresh, recovering,
    activityFor, start, stop, setVisible, connectionStatus, setProfiles,
  }
}

export type Activity = ReturnType<typeof createActivity>
export type { ActivityTopic }
