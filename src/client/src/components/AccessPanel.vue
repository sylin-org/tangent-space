<script setup lang="ts">
// The Access tab, ported from access.js: the context's decisions, each answerable
// with @everyone, @authenticated or the server's roles, saved as one access map.
import { onMounted, reactive, ref, watch } from 'vue'
import type { Settings, SettingsContext } from '../composables/settings'
import { rolesFor, rolesRequest, type RoleOption } from '../composables/roles-api'
import AccessDecisionRow from './AccessDecisionRow.vue'

const props = defineProps<{ settings: Settings }>()
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
let loadToken = 0
let loadedFor = ''

const localTokens = (tokens?: string[]): string[] => (tokens ?? []).filter(token => !String(token).startsWith('global:'))

function signature(c: SettingsContext | null): string {
  return c ? [c.kind, c.tangent, c.topic].filter(Boolean).join(':') : ''
}

function fillDraft(c: SettingsContext, current: AccessView) {
  for (const decision of c.decisions) {
    const inherited = c.kind === 'topic' && current.selected?.[decision.key] == null
    draft[decision.key] = inherited ? null : [...(current.selected?.[decision.key] ?? [])]
  }
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
    choices.value = roleOptions
    fillDraft(c, accessView)
    loading.value = false
    loadedFor = state
    bottomStatus.value = roleOptions.length >= 100 ? 'Showing the first 100 server roles.' : ''
  } catch (error) {
    if (token !== loadToken || state !== signature(context.value)) return
    loading.value = true
    loadingError.value = true
    loadingMessage.value = (error as Error).message || 'Access settings could not be loaded.'
  }
}

async function save() {
  const c: SettingsContext | null = context.value
  if (!c) return
  feedback.value = 'Saving…'
  feedbackState.value = ''
  const state = signature(c)
  try {
    const body = Object.fromEntries(c.decisions.map(decision => [decision.key, draft[decision.key]]))
    const saved = await rolesRequest<AccessView>(c.accessPath, { method: 'PUT', body, scope: 'access' })
    if (state !== signature(context.value)) return
    view.value = saved
    fillDraft(c, saved)
    feedback.value = 'Access saved.'
    feedbackState.value = 'success'
    bottomStatus.value = 'Access saved.'
  } catch (error) {
    feedback.value = (error as Error).message
    feedbackState.value = 'error'
  }
}

watch(activeTab, name => {
  if (name === 'access') void load()
})

onMounted(() => {
  // A deep link can land on this tab already active; the watch never fires then.
  if (activeTab.value === 'access') void load()
})
</script>

<template>
  <div v-if="loading" id="access-loading" class="access-loading" role="status" :class="{ error: loadingError }">{{ loadingMessage }}</div>
  <div v-else id="access-decisions" class="access-decisions">
    <form class="access-map-card" @submit.prevent="save">
      <AccessDecisionRow
        v-for="decision in context?.decisions ?? []"
        :key="decision.key"
        v-model="draft[decision.key]"
        :decision="decision"
        :choices="choices"
        :parent-tokens="localTokens(view.parent?.[decision.key] || view.effective?.[decision.key])"
        :topic-context="context?.kind === 'topic'"
      />
      <div class="access-actions">
        <button class="btn btn-primary" type="submit">Save access</button>
        <p class="access-save-feedback" role="status" :class="feedbackState">{{ feedback }}</p>
      </div>
    </form>
  </div>
  <p id="access-status" class="hint" role="status">{{ bottomStatus }}</p>
</template>
