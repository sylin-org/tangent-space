<script setup lang="ts">
// The Access tab presents the context's decisions, each answerable
// with @everyone, @authenticated or the server's roles, saved as one access map.
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { Settings, SettingsContext } from '../composables/settings'
import { rolesFor, rolesRequest, type RoleOption } from '../composables/roles-api'
import { topicDraftStorageKey } from '../composables/topic-settings'
import { registerSettingsChange, useSettingsSubmit } from '../composables/settings-changes'
import AccessDecisionRow from './AccessDecisionRow.vue'

const submitSettings = useSettingsSubmit()
const props = defineProps<{ settings: Settings }>()
const emit = defineEmits<{ saved: [] }>()
const { activeTab, context } = props.settings

interface AccessView {
  selected?: Record<string, string[] | null>
  parent?: Record<string, string[]>
  effective?: Record<string, string[]>
}

const view = ref<AccessView>({})
const choices = ref<RoleOption[]>([])
const draft = reactive<Record<string, string[] | null>>({})
const loading = ref(true)
const loadingMessage = ref('Loading access…')
const loadingError = ref(false)
const feedback = ref('')
const feedbackState = ref<'' | 'success' | 'error'>('')
const bottomStatus = ref('')
const saving = ref(false)
const historyRequired = ref(false)
const publishHistory = ref(false)
const storageAvailable = ref(false)
let restoring = false
const dirty = computed(() => context.value?.decisions.some(decision =>
  JSON.stringify(draft[decision.key] ?? null) !== JSON.stringify(view.value.selected?.[decision.key] ?? null)) ?? false)
let loadToken = 0
let saveToken = 0
let loadedFor = ''

const localTokens = (tokens?: string[]): string[] => (tokens ?? []).filter(token => !String(token).startsWith('global:'))

function signature(c: SettingsContext | null): string {
  return c ? [props.settings.actorKey?.value, c.kind, c.tangent, c.topic].filter(Boolean).join(':') : ''
}

function storageKey(c: SettingsContext | null): string {
  const actor = props.settings.actorKey?.value
  if (!actor || !c) return ''
  if (c.kind === 'tangent' && c.tangent) return `tangent:access-draft:${encodeURIComponent(actor)}:${encodeURIComponent(c.tangent)}`
  return c.kind === 'topic' && c.tangent && c.topic
    ? topicDraftStorageKey(actor, c.tangent, c.topic, 'access') : ''
}

function persistDraft() {
  const c = context.value
  const key = storageKey(c)
  if (restoring || loading.value || !c || !key) return
  try {
    if (!dirty.value) sessionStorage.removeItem(key)
    else sessionStorage.setItem(key, JSON.stringify(Object.fromEntries(c.decisions
      .filter(decision => JSON.stringify(draft[decision.key] ?? null) !== JSON.stringify(view.value.selected?.[decision.key] ?? null))
      .map(decision => [decision.key, draft[decision.key]]))))
    storageAvailable.value = true
  } catch { storageAvailable.value = false }
}

function restoreDraft(c: SettingsContext) {
  const key = storageKey(c)
  storageAvailable.value = false
  if (!key) return
  try {
    const raw = sessionStorage.getItem(key)
    storageAvailable.value = true
    if (!raw) return
    const stored = JSON.parse(raw) as Record<string, unknown>
    restoring = true
    for (const decision of c.decisions) {
      if (!Object.hasOwn(stored, decision.key)) continue
      const value = stored[decision.key]
      if (value === null || Array.isArray(value) && value.every(token => typeof token === 'string'))
        draft[decision.key] = value as string[] | null
    }
  } catch { storageAvailable.value = false }
  finally { restoring = false }
}

function fillDraft(c: SettingsContext, current: AccessView) {
  restoring = true
  for (const decision of c.decisions) {
    const inherited = current.selected?.[decision.key] == null
    draft[decision.key] = inherited ? null : [...(current.selected?.[decision.key] ?? [])]
  }
  restoring = false
}

async function load(force = false) {
  const c: SettingsContext | null = context.value
  if (!c) return
  const state = signature(c)
  if (!force && loadedFor === state) return
  const token = ++loadToken
  loading.value = true
  loadingError.value = false
  loadingMessage.value = 'Loading access…'
  try {
    const [accessView, roleOptions] = await Promise.all([
      rolesRequest<AccessView>(c.accessPath, { scope: 'access' }),
      rolesFor(),
    ])
    if (token !== loadToken || state !== signature(context.value)) return
    view.value = accessView
    choices.value = [
      { id: 'everyone', name: 'everyone', color: '#6fd9b3', description: 'Anyone, including visitors' },
      { id: 'authenticated', name: 'authenticated', color: '#d7c8ff', description: 'Signed-in participants' },
      ...(c.kind === 'server' ? [] : [{ id: 'tangent:member', name: 'Tangent members', color: '#5f8c7a', description: 'People and agents admitted to this Tangent' }]),
      ...roleOptions,
    ]
    fillDraft(c, accessView)
    restoreDraft(c)
    loading.value = false
    loadedFor = state
    bottomStatus.value = roleOptions.length >= 100 ? 'Showing the first 100 Space roles.' : ''
  } catch (error) {
    if (token !== loadToken || state !== signature(context.value)) return
    loading.value = true
    loadingError.value = true
    loadingMessage.value = (error as Error).message || 'Access settings could not be loaded.'
  }
}

async function save() {
  const c: SettingsContext | null = context.value
  if (!c || !dirty.value || saving.value) return
  if (historyRequired.value && !publishHistory.value) {
    feedback.value = 'Confirm what happens to existing posts before saving.'
    feedbackState.value = 'error'
    return
  }
  saving.value = true
  feedback.value = 'Saving…'
  feedbackState.value = ''
  const state = signature(c)
  const token = ++saveToken
  try {
    const body = Object.fromEntries(c.decisions.map(decision => [decision.key, draft[decision.key]]))
    const saved = await rolesRequest<AccessView>(c.accessPath, {
      method: 'PUT', body: c.kind === 'server' ? body : { access: body, publishExistingHistory: publishHistory.value }, scope: 'access',
    })
    if (token !== saveToken || state !== signature(context.value)) return
    view.value = saved
    fillDraft(c, saved)
    persistDraft()
    feedback.value = 'Access saved.'
    historyRequired.value = false
    publishHistory.value = false
    feedbackState.value = 'success'
    emit('saved')
  } catch (error) {
    if (token !== saveToken || state !== signature(context.value)) return
    if ((error as { code?: string }).code === 'history_publication_required' && c.kind !== 'server') {
      historyRequired.value = true
      publishHistory.value = false
    }
    feedback.value = (error as Error).message
    feedbackState.value = 'error'
  } finally {
    if (token === saveToken) saving.value = false
  }
}

function discard() {
  const c = context.value
  if (!c) return
  fillDraft(c, view.value)
  persistDraft()
  feedback.value = ''
  feedbackState.value = ''
  historyRequired.value = false
  publishHistory.value = false
}

watch(activeTab, name => {
  if (name === 'access') void load()
})
watch(draft, () => {
  publishHistory.value = false
  if (!restoring) {
    feedback.value = ''
    feedbackState.value = ''
  }
  persistDraft()
}, { deep: true, flush: 'sync' })
watch(context, current => {
  saveToken++
  saving.value = false
  feedback.value = ''
  feedbackState.value = ''
  historyRequired.value = false
  publishHistory.value = false
  if (current && (current.kind === 'topic' || activeTab.value === 'access')) void load()
})
watch(() => props.settings.actorKey?.value, () => {
  saveToken++
  saving.value = false
  feedback.value = ''
  feedbackState.value = ''
  historyRequired.value = false
  publishHistory.value = false
  if (context.value && (context.value.kind === 'topic' || activeTab.value === 'access')) void load()
})

onMounted(() => {
  // A deep link can land on this tab already active; the watch never fires then.
  if (activeTab.value === 'access' || context.value?.kind === 'topic') void load()
})
const coordinated = registerSettingsChange({ label: 'Access', count: () => context.value?.decisions.filter(decision => JSON.stringify(draft[decision.key] ?? null) !== JSON.stringify(view.value.selected?.[decision.key] ?? null)).length || 0, busy: () => saving.value, save, discard, status: () => feedback.value })
</script>

<template>
  <div v-if="loading" id="access-loading" class="access-loading" role="status" :class="{ error: loadingError }">
    {{ loadingMessage }}
    <button v-if="loadingError" class="btn btn-quiet" type="button" @click="load(true)">Retry</button>
  </div>
  <div v-else id="access-decisions" class="access-decisions">
    <form class="access-map-card" @submit.prevent="submitSettings(save)">
      <fieldset class="access-decision-fields" :disabled="saving">
      <AccessDecisionRow
        v-for="decision in context?.decisions ?? []"
        :key="decision.key"
        v-model="draft[decision.key]"
        :decision="decision"
        :choices="choices"
        :parent-tokens="localTokens(view.parent?.[decision.key] || view.effective?.[decision.key])"
        :effective-tokens="view.effective?.[decision.key] ?? []"
        :topic-context="context?.kind === 'topic'"
      />
      </fieldset>
      <label v-if="historyRequired" class="check-row"><input v-model="publishHistory" type="checkbox"> I understand that existing posts made public by these rules will be readable by anyone and copies may persist.</label>
      <div class="access-actions">
        <button v-if="!coordinated" class="btn btn-primary" type="submit" :disabled="!dirty || saving || (historyRequired && !publishHistory)">{{ saving ? 'Saving…' : 'Save access rules' }}</button>
        <button v-if="!coordinated" class="btn btn-quiet" type="button" :disabled="!dirty || saving" @click="discard">Discard</button>
        <p class="access-save-feedback" role="status" :class="feedbackState">{{ feedback || (dirty ? storageAvailable ? 'Unsaved access rules are kept in this browser tab.' : 'Unsaved changes' : '') }}</p>
      </div>
    </form>
  </div>
  <p id="access-status" class="hint" role="status">{{ bottomStatus }}</p>
</template>
