<script setup lang="ts">
import TangentAccentPalette from './TangentAccentPalette.vue'
import { tangentAccent } from '../composables/tangent-accents'
import { registerSettingsChange, useSettingsSubmit } from '../composables/settings-changes'
import { computed, onUnmounted, reactive, ref, watch } from 'vue'
import type { Settings, TangentView } from '../composables/settings'
import type { Directories } from '../composables/directories'
import { rolesRequest } from '../composables/roles-api'
import { settingsOrigin } from '../composables/settings-return'
import CommunityImage from './CommunityImage.vue'
import CommunityHeader from './CommunityHeader.vue'
import CommunityDirectoryIdentity from './CommunityDirectoryIdentity.vue'
import SettingsWorkspaceHeader from './SettingsWorkspaceHeader.vue'
import SettingsSectionTabs from './SettingsSectionTabs.vue'
import ArtworkPicker from './ArtworkPicker.vue'
import AccessPanel from './AccessPanel.vue'
import TangentPeoplePanel from './TangentPeoplePanel.vue'

type Identity = { name: string; kind: 'discussions' | 'articles'; description: string; motto: string; accent: string; artwork: string }
const submitSettings = useSettingsSubmit()
const props = defineProps<{ settings: Settings; directories: Directories }>()
const emit = defineEmits<{ return: [] }>()
const { context, resource, activeTab, settingsTab, actorKey } = props.settings
const tangent = computed(() => context.value?.kind === 'tangent' ? context.value.tangent || '' : '')
const source = computed(() => props.settings.loadedActor.value === actorKey.value ? resource.value?.tangent || null : null)
const scope = computed(() => `${actorKey.value}:${tangent.value}`)
const origin = computed(() => settingsOrigin(actorKey.value, `tangent:${tangent.value}`))
const signInHref = computed(() => '/sign-in/?return=' + encodeURIComponent(window.location.pathname + window.location.search))
const form = reactive<Identity>({ name: '', kind: 'discussions', description: '', motto: '', accent: '#fbbf24', artwork: '' })
const saved = ref<Identity>({ ...form })
const saving = ref(false)
const uploading = ref(false)
const imagePickerOpen = ref(false)
const imageUrlOpen = ref(false)
const feedback = ref('')
const feedbackError = ref(false)
const admission = ref<'Open' | 'Approval'>('Open')
const savedAdmission = ref<'Open' | 'Approval'>('Open')
const admissionSaving = ref(false)
const admissionFeedback = ref('')
const admissionError = ref(false)
let hydration = false
let hydratedScope = ''
let saveToken = 0
let admissionToken = 0
onUnmounted(() => { saveToken++; admissionToken++ })

function snapshot(item: TangentView): Identity {
  return {
    name: item.name || '', description: item.description || '', motto: item.motto || '',
    kind: item.kind === 'articles' ? 'articles' : 'discussions',
    accent: tangentAccent(item.accent || ''),
    artwork: item.artwork || '',
  }
}
function storageKey(): string {
  return `tangent:identity-draft:${encodeURIComponent(actorKey.value)}:${encodeURIComponent(tangent.value)}`
}
function admissionStorageKey(): string {
  return `tangent:admission-draft:${encodeURIComponent(actorKey.value)}:${encodeURIComponent(tangent.value)}`
}
function normalize(value: Identity): Identity {
  return { name: value.name.trim(), kind: value.kind, description: value.description.trim(), motto: value.motto.trim(), accent: value.accent, artwork: value.artwork.trim() }
}
const dirty = computed(() => JSON.stringify(normalize(form)) !== JSON.stringify(saved.value))
const admissionDirty = computed(() => admission.value !== savedAdmission.value)
const preview = computed(() => ({
  name: form.name.trim() || 'Untitled Tangent', description: form.description.trim(),
  artwork: form.artwork.trim(), accent: form.accent,
}))

function persist() {
  if (hydration || !actorKey.value || !tangent.value) return
  try {
    if (dirty.value) sessionStorage.setItem(storageKey(), JSON.stringify(Object.fromEntries(
      (Object.keys(saved.value) as (keyof Identity)[])
        .filter(field => normalize(form)[field] !== saved.value[field])
        .map(field => [field, form[field]]),
    )))
    else sessionStorage.removeItem(storageKey())
  } catch { /* Editing still works when browser storage is unavailable. */ }
}

function hydrate() {
  const item = source.value
  if (!item || !tangent.value || !actorKey.value) return
  hydration = true
  const current = snapshot(item)
  saved.value = current
  Object.assign(form, current)
  try {
    const draft = JSON.parse(sessionStorage.getItem(storageKey()) || 'null') as Partial<Identity> | null
    if (draft && typeof draft === 'object') {
      for (const key of Object.keys(current) as (keyof Identity)[]) {
        if (key === 'kind') continue
        if (typeof draft[key] === 'string') Object.assign(form, { [key]: draft[key] })
      }
      form.kind = draft.kind === 'articles' ? 'articles' : draft.kind === 'discussions' ? 'discussions' : current.kind
    }
  } catch { /* A damaged draft does not hide saved settings. */ }
  admission.value = item.admission === 'Approval' ? 'Approval' : 'Open'
  savedAdmission.value = admission.value
  try {
    const value = sessionStorage.getItem(admissionStorageKey())
    if (value === 'Open' || value === 'Approval') admission.value = value
  } catch { /* Joining remains editable without browser storage. */ }
  imagePickerOpen.value = false
  imageUrlOpen.value = false
  feedback.value = ''
  admissionFeedback.value = ''
  hydration = false
}

watch([scope, source], () => {
  if (scope.value === hydratedScope || !source.value) return
  saveToken++
  admissionToken++
  saving.value = false
  admissionSaving.value = false
  uploading.value = false
  hydrate()
  hydratedScope = scope.value
}, { immediate: true })
watch(form, () => {
  feedback.value = ''
  feedbackError.value = false
  persist()
}, { deep: true, flush: 'sync' })
watch(admission, () => {
  if (hydration) return
  admissionFeedback.value = ''
  admissionError.value = false
  try {
    if (admissionDirty.value) sessionStorage.setItem(admissionStorageKey(), admission.value)
    else sessionStorage.removeItem(admissionStorageKey())
  } catch { /* Joining remains editable without browser storage. */ }
})

function discardIdentity() {
  if (saving.value || uploading.value) return
  Object.assign(form, saved.value)
  persist()
  feedback.value = ''
}

async function saveIdentity() {
  if (!dirty.value || saving.value || uploading.value || !tangent.value || !actorKey.value) return
  const body = normalize(form)
  if (!body.name) { feedback.value = 'Give this Tangent a name.'; feedbackError.value = true; return }
  if (body.artwork && !/^https:\/\//i.test(body.artwork) && !/^\/(tangent-art\/[a-z0-9-]+\.png|artwork\/[a-f0-9]{64}\.(png|jpg|webp))$/i.test(body.artwork)) {
    feedback.value = 'Use an HTTPS image or choose a supplied cover.'
    feedbackError.value = true
    return
  }
  const current = scope.value
  const key = tangent.value
  const token = ++saveToken
  saving.value = true
  feedback.value = 'Saving…'
  try {
    const changed = Object.fromEntries((Object.keys(body) as (keyof Identity)[])
      .filter(field => body[field] !== saved.value[field]).map(field => [field, body[field]]))
    await rolesRequest(`/api/v1/tangents/${encodeURIComponent(key)}`, { method: 'PATCH', body: changed, scope: 'access' })
    if (token !== saveToken || current !== scope.value) return
    saved.value = body
    if (resource.value?.tangent) {
      resource.value = { ...resource.value, name: body.name, description: body.description, tangent: { ...resource.value.tangent, ...body } }
    }
    persist()
    feedback.value = dirty.value ? 'Earlier changes saved. New edits remain unsaved.' : 'About and appearance saved.'
    feedbackError.value = false
    void props.directories.loadTangents().catch(() => {})
  } catch (cause) {
    if (token !== saveToken || current !== scope.value) return
    feedback.value = (cause as Error).message || 'Changes could not be saved.'
    feedbackError.value = true
  } finally {
    if (token === saveToken) saving.value = false
  }
}

async function saveAdmission() {
  if (!admissionDirty.value || admissionSaving.value || !tangent.value) return
  const key = tangent.value
  const current = scope.value
  const token = ++admissionToken
  const choice = admission.value
  admissionSaving.value = true
  admissionFeedback.value = 'Saving…'
  try {
    await rolesRequest(`/api/v1/tangents/${encodeURIComponent(key)}/admission`, { method: 'PUT', body: { admission: choice }, scope: 'access' })
    if (token !== admissionToken || current !== scope.value) return
    savedAdmission.value = choice
    try { sessionStorage.removeItem(admissionStorageKey()) } catch { /* Saved on server. */ }
    if (resource.value?.tangent) resource.value = { ...resource.value, tangent: { ...resource.value.tangent, admission: choice } }
    admissionFeedback.value = choice === 'Open' ? 'Joining is open.' : 'Joining requires approval.'
    admissionError.value = false
    void props.directories.loadTangents().catch(() => {})
  } catch (cause) {
    if (token !== admissionToken || current !== scope.value) return
    admissionFeedback.value = (cause as Error).message || 'Joining could not be saved.'
    admissionError.value = true
  } finally {
    if (token === admissionToken) admissionSaving.value = false
  }
}

const tabs = [
  { id: 'context', label: 'About & appearance' },
  { id: 'people', label: 'People' },
  { id: 'access', label: 'Access' },
]
const coordinated = registerSettingsChange({ label: 'Tangent details', count: () => Object.keys(saved.value).filter(field => normalize(form)[field as keyof Identity] !== saved.value[field as keyof Identity]).length, busy: () => saving.value || uploading.value, save: saveIdentity, discard: discardIdentity, status: () => feedback.value })
registerSettingsChange({ label: 'Joining', count: () => admissionDirty.value ? 1 : 0, busy: () => admissionSaving.value, save: saveAdmission, discard: () => { admission.value = savedAdmission.value }, status: () => admissionFeedback.value })
</script>

<template>
  <div class="tangent-settings-workspace">
    <SettingsWorkspaceHeader eyebrow="COMMUNITY SETTINGS" :name="source?.name || tangent || 'Community settings'" :artwork="source?.artwork" :accent="source?.accent" :back-label="origin?.label || 'Tangent'" @back="emit('return')" />
    <p v-if="settings.loading.value" role="status" class="hint">Loading community settings…</p>
    <p v-else-if="settings.denied.value" role="status" class="hint error">{{ settings.denied.value }}</p>
    <a v-else-if="!settings.shellVisible.value" class="btn btn-primary" :href="signInHref">Sign in to manage this Tangent</a>
    <template v-else>
      <SettingsSectionTabs :tabs="tabs" :active="activeTab" tab-prefix="tangent" label="Community settings" @select="settingsTab" />
      <section id="tangent-context-panel" class="tangent-settings-section" role="tabpanel" aria-labelledby="tangent-context-tab" v-show="activeTab === 'context'">
        <div class="tangent-section-intro"><h2>About & appearance</h2></div>
        <div class="tangent-identity-layout">
          <form id="tangent-identity-form" class="tangent-identity-form" @submit.prevent="submitSettings(saveIdentity)">
            <div class="tangent-image-setting"><CommunityImage :artwork="form.artwork" :name="preview.name" /><div><strong>Community image</strong><p>Shown beside this Tangent and in its directory listing.</p><button type="button" class="btn btn-quiet" :aria-expanded="imagePickerOpen" @click="imagePickerOpen = !imagePickerOpen">{{ form.artwork ? 'Change image' : 'Choose image' }}</button></div></div>
            <div v-show="imagePickerOpen" class="tangent-image-picker"><ArtworkPicker :key="scope" v-model="form.artwork" scope="tangent" :tangent-key="tangent" @uploading="uploading = $event" /><button type="button" class="tangent-url-toggle" :aria-expanded="imageUrlOpen" @click="imageUrlOpen = !imageUrlOpen">Use an image URL</button><label v-if="imageUrlOpen">HTTPS image URL<input v-model="form.artwork" class="input" type="url" maxlength="2048" placeholder="https://…"></label></div>
            <label>Community name<input v-model="form.name" class="input" required maxlength="120" autocomplete="off"></label>
            <label>Description<textarea v-model="form.description" class="input" rows="3" maxlength="500" placeholder="What brings people together here?"></textarea></label>
            <label>Content kind<select v-model="form.kind" class="input"><option value="discussions">Discussions</option><option value="articles">Articles</option></select><span class="hint">Applies to all threads in this Tangent.</span></label>
            <label>Motto <span class="field-note">Optional short line</span><input v-model="form.motto" class="input" maxlength="240"></label>
            <TangentAccentPalette v-model="form.accent" />
          </form>
          <aside class="tangent-preview-column" aria-label="Community preview" :style="{ '--community-accent': preview.accent }">
            <p class="eyebrow">LIVE PREVIEW · COMMUNITY HEADER</p><CommunityHeader :name="preview.name" :description="preview.description" :artwork="preview.artwork" :accent="preview.accent" preview />
            <p class="eyebrow">TANGENT DIRECTORY</p><div class="tangent-directory-preview"><CommunityDirectoryIdentity :name="preview.name" :description="preview.description" :artwork="preview.artwork" :accent="preview.accent" preview /></div>
            <p class="hint">Your changes appear here while you edit. They reach the community when you save.</p>
          </aside>
          <div v-if="!coordinated" class="tangent-savebar tangent-identity-savebar"><button class="btn btn-primary" type="submit" form="tangent-identity-form" :disabled="!dirty || saving || uploading">{{ saving ? 'Saving…' : 'Save changes' }}</button><button class="btn btn-quiet" type="button" :disabled="!dirty || saving || uploading" @click="discardIdentity">Discard</button><p role="status" :class="{ error: feedbackError }">{{ feedback || (dirty ? 'Unsaved changes are kept in this browser tab.' : '') }}</p></div>
        </div>
      </section>
      <section id="tangent-people-panel" class="tangent-settings-section" role="tabpanel" aria-labelledby="tangent-people-tab" v-show="activeTab === 'people'"><TangentPeoplePanel :tangent="tangent" :actor="actorKey" /></section>
      <section id="tangent-access-panel" class="tangent-settings-section" role="tabpanel" aria-labelledby="tangent-access-tab" v-show="activeTab === 'access'">
        <div class="tangent-section-intro"><h2>Access</h2><p>Choose who can discover, read, contribute, and tend this Tangent. Changing reading access may also affect existing Posts; the editor will ask you to decide before publishing history.</p></div>
        <div class="tangent-admission"><h3>Joining this Tangent</h3><p>Joining gives people access to existing community history under its reading rules.</p><fieldset :disabled="admissionSaving || !source?.canManageAdmission"><legend>How may people join?</legend><label><input v-model="admission" type="radio" value="Open"> Open — people join right away</label><label><input v-model="admission" type="radio" value="Approval"> By request — a steward approves each person</label></fieldset><p v-if="!source?.canManageAdmission" class="hint">Only this Tangent’s owner can change joining.</p><div v-if="source?.canManageAdmission && !coordinated" class="tangent-savebar"><button class="btn btn-primary" type="button" :disabled="!admissionDirty || admissionSaving" @click="saveAdmission">{{ admissionSaving ? 'Saving…' : 'Save joining' }}</button><button class="btn btn-quiet" type="button" :disabled="!admissionDirty || admissionSaving" @click="admission = savedAdmission">Discard</button><p role="status" :class="{ error: admissionError }">{{ admissionFeedback || (admissionDirty ? 'Unsaved joining change' : '') }}</p></div></div>
        <div class="tangent-access-rules"><h3>Reading and participation</h3><AccessPanel :settings="settings" /></div>
      </section>
    </template>
  </div>
</template>
