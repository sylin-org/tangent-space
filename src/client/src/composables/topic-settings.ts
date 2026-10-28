import { computed, getCurrentScope, onScopeDispose, reactive, ref, watch, type Ref, type ShallowRef } from 'vue'
import { rolesRequest } from './roles-api'
import type { SettingsResource, TopicView } from './settings'

type FormState = 'idle' | 'saving' | 'saved' | 'error'

export function topicDraftStorageKey(actor: string, tangent: string, topic: string, section: string): string {
  return 'tangent:topic-settings:' + [actor, tangent, topic, section].map(encodeURIComponent).join(':')
}

export function createTopicSettings(resource: ShallowRef<SettingsResource | null>, actorKey?: Ref<string>) {
  const identity = actorKey ?? ref('')
  const saved = reactive({ title: '', topic: '', allowPostEditing: false, isLocked: false, readAudience: 'Restricted' })
  const details = reactive({ title: '', topic: '' })
  const participation = reactive({ allowPostEditing: false, isLocked: false })
  const audience = ref('Restricted')
  const publishHistory = ref(false)
  const member = reactive({ identifier: '', role: 'Member' })
  const state = reactive<Record<'details' | 'participation' | 'audience' | 'member', FormState>>({
    details: 'idle', participation: 'idle', audience: 'idle', member: 'idle',
  })
  const errors = reactive({ details: '', participation: '', audience: '', member: '' })
  let key = ''
  let generation = 0
  if (getCurrentScope()) onScopeDispose(() => { generation++ })
  let restoring = false
  const storageAvailable = ref(false)

  const storageKey = computed(() => {
    const topic = resource.value?.topic
    return topic && identity.value
      ? topicDraftStorageKey(identity.value, topic.tangentKey, topic.key, 'forms') : ''
  })

  function restoreDraft() {
    storageAvailable.value = false
    if (!storageKey.value) return
    try {
      const raw = sessionStorage.getItem(storageKey.value)
      storageAvailable.value = true
      if (!raw) return
      const draft = JSON.parse(raw) as Record<string, unknown>
      if (typeof draft.title === 'string') details.title = draft.title.slice(0, 120)
      if (typeof draft.topic === 'string') details.topic = draft.topic.slice(0, 2000)
      if (typeof draft.allowPostEditing === 'boolean') participation.allowPostEditing = draft.allowPostEditing
      if (typeof draft.isLocked === 'boolean') participation.isLocked = draft.isLocked
      if (draft.audience === 'Public' || draft.audience === 'Restricted') audience.value = draft.audience
      if (typeof draft.identifier === 'string') member.identifier = draft.identifier.slice(0, 253)
      if (['Member', 'Reader', 'Manager', 'Removed'].includes(String(draft.role))) member.role = String(draft.role)
    } catch { storageAvailable.value = false }
  }

  watch([resource, identity], ([current]) => {
    const topic = current?.topic
    if (!topic) return
    const nextKey = identity.value + ':' + topic.tangentKey + '/' + topic.key
    if (nextKey === key) return
    restoring = true
    key = nextKey
    generation++
    saved.title = details.title = topic.title || ''
    saved.topic = details.topic = topic.topic || ''
    saved.allowPostEditing = participation.allowPostEditing = topic.allowPostEditing === true
    saved.isLocked = participation.isLocked = topic.isLocked === true
    saved.readAudience = audience.value = topic.readAudience || 'Restricted'
    publishHistory.value = false
    member.identifier = ''
    member.role = 'Member'
    for (const name of Object.keys(state) as Array<keyof typeof state>) {
      state[name] = 'idle'
      errors[name] = ''
    }
    restoreDraft()
    restoring = false
  }, { immediate: true })

  const path = computed(() => {
    const topic = resource.value?.topic
    return topic ? '/api/v1/tangents/' + encodeURIComponent(topic.tangentKey) + '/topics/' + encodeURIComponent(topic.key) : ''
  })
  const dirty = {
    details: computed(() => details.title !== saved.title || details.topic !== saved.topic),
    participation: computed(() => participation.allowPostEditing !== saved.allowPostEditing || participation.isLocked !== saved.isLocked),
    audience: computed(() => audience.value !== saved.readAudience),
    member: computed(() => !!member.identifier.trim() || member.role !== 'Member'),
  }
  const publishing = computed(() => saved.readAudience !== 'Public' && audience.value === 'Public')
  const hasDirty = computed(() => Object.values(dirty).some(value => value.value))

  watch(() => [details.title, details.topic, participation.allowPostEditing, participation.isLocked,
    audience.value, member.identifier, member.role, saved.title, saved.topic,
    saved.allowPostEditing, saved.isLocked, saved.readAudience], () => {
    if (restoring || !storageKey.value) return
    try {
      if (!hasDirty.value) sessionStorage.removeItem(storageKey.value)
      else sessionStorage.setItem(storageKey.value, JSON.stringify({
        ...(details.title !== saved.title ? { title: details.title } : {}),
        ...(details.topic !== saved.topic ? { topic: details.topic } : {}),
        ...(participation.allowPostEditing !== saved.allowPostEditing ? { allowPostEditing: participation.allowPostEditing } : {}),
        ...(participation.isLocked !== saved.isLocked ? { isLocked: participation.isLocked } : {}),
        ...(audience.value !== saved.readAudience ? { audience: audience.value } : {}),
        ...(member.identifier.trim() ? { identifier: member.identifier } : {}),
        ...(member.role !== 'Member' ? { role: member.role } : {}),
      }))
      storageAvailable.value = true
    } catch { storageAvailable.value = false }
  }, { flush: 'sync' })

  function discard(name: keyof typeof state) {
    if (name === 'details') Object.assign(details, { title: saved.title, topic: saved.topic })
    if (name === 'participation') Object.assign(participation, { allowPostEditing: saved.allowPostEditing, isLocked: saved.isLocked })
    if (name === 'audience') { audience.value = saved.readAudience; publishHistory.value = false }
    if (name === 'member') Object.assign(member, { identifier: '', role: 'Member' })
    state[name] = 'idle'
    errors[name] = ''
  }

  async function submit(name: keyof typeof state, run: () => Promise<void>) {
    const at = generation
    state[name] = 'saving'
    errors[name] = ''
    try {
      await run()
      if (at === generation) state[name] = 'saved'
    } catch (error) {
      if (at !== generation) return
      errors[name] = (error as Error).message || 'This change could not be saved.'
      state[name] = 'error'
    }
  }

  async function saveDetails() {
    if (!dirty.details.value || state.details === 'saving') return
    const draftTitle = details.title
    const draftTopic = details.topic
    const title = details.title.trim()
    const topic = details.topic.trim()
    const target = path.value
    const at = generation
    if (!title) { errors.details = 'Enter a Topic title.'; state.details = 'error'; return }
    await submit('details', async () => {
      await rolesRequest(target + '/settings', { method: 'PATCH', scope: 'access', body: { title, topic } })
      if (at !== generation) return
      saved.title = title
      saved.topic = topic
      if (details.title === draftTitle) details.title = title
      if (details.topic === draftTopic) details.topic = topic
      const topicView: TopicView | undefined = resource.value?.topic
      if (topicView) { topicView.title = title; topicView.topic = topic }
    })
  }

  async function saveParticipation() {
    if (!dirty.participation.value || state.participation === 'saving') return
    const allowPostEditing = participation.allowPostEditing
    const isLocked = participation.isLocked
    const target = path.value
    const at = generation
    await submit('participation', async () => {
      await rolesRequest(target + '/settings', { method: 'PATCH', scope: 'access', body: { allowPostEditing, isLocked } })
      if (at !== generation) return
      saved.allowPostEditing = allowPostEditing
      saved.isLocked = isLocked
      const topicView = resource.value?.topic
      if (topicView) { topicView.allowPostEditing = allowPostEditing; topicView.isLocked = isLocked }
    })
  }

  async function saveAudience() {
    if (!dirty.audience.value || state.audience === 'saving') return
    if (publishing.value && !publishHistory.value) {
      errors.audience = 'Confirm that existing posts will become public.'
      state.audience = 'error'
      return
    }
    const next = audience.value
    const target = path.value
    const at = generation
    const confirmed = publishing.value && publishHistory.value
    await submit('audience', async () => {
      await rolesRequest(target + '/read-audience', { method: 'PATCH', scope: 'access', body: {
        audience: next, publishExistingHistory: confirmed,
      } })
      if (at !== generation) return
      saved.readAudience = next
      if (resource.value?.topic) resource.value.topic.readAudience = next
      publishHistory.value = false
    })
  }

  async function saveMember() {
    const identifier = member.identifier.trim()
    if (!identifier || state.member === 'saving') return
    const role = member.role
    const topicKey = resource.value?.topic?.key || ''
    const at = generation
    await submit('member', async () => {
      await rolesRequest('/api/topics/' + encodeURIComponent(topicKey) + '/members/' + encodeURIComponent(identifier), {
        method: 'PUT', scope: 'access', body: { role },
      })
      if (at !== generation) return
      if (member.identifier.trim() === identifier && member.role === role) {
        member.identifier = ''
        member.role = 'Member'
      }
    })
  }

  return { saved, details, participation, audience, publishHistory, member, state, errors, dirty, publishing, hasDirty, storageAvailable,
    discard, saveDetails, saveParticipation, saveAudience, saveMember }
}
