<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch, watchEffect } from 'vue'
import { assertSetupIdentity, SetupIdentityError, httpRequest, storageApi, type StorageState } from '../api'
import type { SiteStore } from '../stores/site'
import type { RouterStore } from '../stores/router'
import { artworkPresets } from '../composables/artwork-presets'
import { clearSetupState, readSetupDraft, saveSetupArrival, saveSetupDraft } from '../composables/onboarding-state'
import DatabasePanel from './DatabasePanel.vue'
import SetupProgress from './SetupProgress.vue'
import CommunityImage from './CommunityImage.vue'

const props = defineProps<{ site: SiteStore; router: RouterStore }>()
const onboardingState = computed(() => props.site.site?.onboarding || 'complete')
const active = computed(() => props.router.route.kind === 'onboarding' && !['complete', 'sign_in'].includes(onboardingState.value))
const did = computed(() => props.site.site?.participant?.did || '')
const account = computed(() => props.site.site?.participant?.handle ? '@' + props.site.site.participant.handle : props.site.displayName)
const step = ref<'data' | 'tangent'>('data')
const form = reactive({ name: '', description: '', artwork: '', kind: 'discussions' as 'discussions' | 'articles' })
const busy = ref(false)
const error = ref('')
const accountChanged = ref(false)
const submitted = ref(false)
const creationName = ref('')
const storage = ref<StorageState | null>(null)
const options = computed(() => props.site.site?.initialTangent)
const stepHeading = ref<HTMLElement | null>(null)
const activeDatabase = computed(() => storage.value?.providers.find(item => item.id === storage.value?.activeProvider)?.name || storage.value?.activeProvider || 'Your database')
const starters = [
  { name: 'Small Hours', description: 'Half-formed thoughts, welcome as they are.' },
  { name: 'The Lab', description: 'Experiments with people and companions.' },
  { name: 'Reading Circle', description: 'Links, long reads, and the things worth keeping.' },
]
watchEffect(() => document.body.classList.toggle('onboarding-active', props.router.route.kind === 'onboarding' && onboardingState.value !== 'complete'))
watch(did, value => {
  const draft = readSetupDraft(value)
  Object.assign(form, { name: draft?.name || '', description: draft?.description || '', artwork: draft?.artwork || '', kind: draft?.kind === 'articles' ? 'articles' : 'discussions' })
  submitted.value = draft?.submitted === true
  creationName.value = draft?.creationName || ''
  accountChanged.value = false
  // Always check the live database before continuing a recovered creative draft.
  step.value = 'data'
  storage.value = null
  error.value = ''
}, { immediate: true })
watch([() => form.name, () => form.description, () => form.artwork, () => form.kind, step], () => {
  saveSetupDraft(did.value, { ...form, step: step.value, submitted: submitted.value, creationName: creationName.value })
})
watch([onboardingState, () => props.site.site?.initialTangentKey, () => props.site.state], ([state, key]) => {
  if (props.router.route.kind !== 'onboarding' || props.site.state === 'loading' || !props.site.site || state !== 'complete') return
  if (!key || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(key)) { window.location.replace('/tangents/'); return }
  const draft = readSetupDraft(did.value)
  if (draft?.submitted) saveSetupArrival(did.value, { key, name: draft.creationName || draft.name || 'your Tangent' })
  clearSetupState('draft', did.value)
  window.location.replace('/t/' + encodeURIComponent(key) + '/topics')
}, { immediate: true })
async function moveTo(next: 'data' | 'tangent') {
  if (busy.value || accountChanged.value || (next === 'tangent' && (!storage.value || storage.value.pendingRestart || storage.value.activeDurable === false))) return
  step.value = next
  error.value = ''
  await nextTick()
  stepHeading.value?.focus({ preventScroll: true })
  document.getElementById('first-tangent')?.scrollIntoView({ block: 'start' })
}
function storageChanged(value: StorageState) {
  storage.value = value
  if (value.pendingRestart || value.activeDurable === false) step.value = 'data'
}
const cardName = computed(() => form.name.trim() || 'Your first Tangent')
const cardDescription = computed(() => form.description.trim() || 'A place for the conversations that matter to you.')
// One cover from each mood before any mood repeats, so eight squares span the
// collection instead of showing its first page.
const starterArtwork = computed(() => {
  const moods = new Map<string, typeof artworkPresets>()
  for (const entry of artworkPresets) {
    if (!moods.has(entry.group)) moods.set(entry.group, [])
    moods.get(entry.group)!.push(entry)
  }
  const rows = [...moods.values()]
  const picked: typeof artworkPresets = []
  for (let depth = 0; picked.length < 8; depth += 1) {
    if (!rows.some(entries => entries.length > depth)) break
    for (const entries of rows) {
      if (picked.length === 8) break
      if (entries[depth]) picked.push(entries[depth]!)
    }
  }
  return picked.map(entry => ({ ...entry, url: '/tangent-art/' + entry.slug + '.png' }))
})


async function finish(useDefault: boolean) {
  if (busy.value || accountChanged.value || !options.value) return
  const expectedDid = did.value
  if (!useDefault && !form.name.trim()) { error.value = 'Give your Tangent a name, or choose “Use My Tangent”.'; return }
  busy.value = true
  error.value = ''
  try {
    await assertSetupIdentity(expectedDid)
    const current = await storageApi.get()
    storageChanged(current)
    if (current.pendingRestart) throw new Error('Restart this Host and sign in again before creating your first Tangent.')
    if (current.activeDurable === false) throw new Error(current.activeStorageIssue || 'Choose durable storage before creating your first Tangent.')
    submitted.value = true
    creationName.value = useDefault ? 'My Tangent' : form.name.trim()
    saveSetupDraft(did.value, { ...form, step: step.value, submitted: true, creationName: creationName.value })
    const response = await httpRequest('/api/onboarding/tangent', {
      method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedDid, skip: useDefault, name: form.name.trim(), description: form.description.trim(), artwork: form.artwork, kind: form.kind }),
    })
    const result = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error(result.error || result.reason || result.title || 'Your first Tangent could not be created. Your choices are still here; try again.')
    if (typeof result.key !== 'string' || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(result.key)) throw new Error('Creation could not be confirmed. Retry to reopen the same Tangent.')
    saveSetupArrival(did.value, { key: result.key, name: result.name || (useDefault ? 'My Tangent' : form.name.trim()) })
    clearSetupState('draft', did.value)
    window.location.assign('/t/' + encodeURIComponent(result.key) + '/topics')
  } catch (e) { if (e instanceof SetupIdentityError) accountChanged.value = true; error.value = (e as Error).message }
  finally { busy.value = false }
}
</script>

<template>
  <div v-if="active && onboardingState === 'create_tangent'" id="first-tangent" class="guided-setup">
    <SetupProgress :current="step" />
    <div class="setup-account"><span class="setup-account-mark" aria-hidden="true">✓</span><span>Setting up as <strong>{{ account }}</strong></span><form action="/auth/logout" method="post" class="switch-account"><input type="hidden" name="return" value="/onboarding/"><button type="submit" :disabled="busy">Switch account</button></form></div>
    <div v-if="accountChanged" class="setup-account-changed" role="alert"><h2>Your signed-in account changed.</h2><p>This setup belongs to {{ account }}. Sign in with that account, or reload to start with the account currently signed in.</p><a class="btn btn-primary" href="/onboarding/">Reload setup</a><a class="btn btn-quiet" href="/sign-in/?return=%2Fonboarding%2F">Sign in again</a></div>
    <header class="setup-intro">
      <p class="eyebrow">{{ step === 'data' ? 'A lasting home' : 'Make it yours' }}</p>
      <h1 ref="stepHeading" tabindex="-1">{{ step === 'data' ? 'Where your writing and conversations live.' : 'What would you like to call it?' }}</h1>
      <p class="lede">{{ step === 'data' ? 'Your conversations will be stored on this Host. Use the ready SQLite storage or choose another durable database.' : 'Your first Tangent is a place for articles or conversations on this Host. Give it a name and a little character.' }}</p>
    </header>
    <div v-show="step === 'data'" class="setup-data-step">
      <DatabasePanel :key="did" guided :expected-did="did" @state="storageChanged" @identity-changed="accountChanged = true" @continue="moveTo('tangent')" />
      <p class="setup-owner-note">{{ storage?.firstRun === false ? 'Your ownership is saved. Continue with the existing database to finish your first Tangent.' : 'When you finish, you’ll own this Host and decide who can create Tangents.' }}</p>
    </div>
    <div v-if="step === 'tangent'" class="setup-creative-step">
      <button type="button" class="setup-back" :disabled="busy" @click="moveTo('data')">← Back to your data <span>{{ activeDatabase }}</span></button>
      <div class="onboarding-layout">
        <form id="first-tangent-form" class="stack onboarding-copy" @submit.prevent="finish(false)">
          <label for="first-tangent-name">Tangent name<input id="first-tangent-name" v-model="form.name" class="input" name="name" maxlength="120" required placeholder="Small Hours" autocomplete="off" :disabled="busy"></label>
          <div class="onboarding-choices"><p class="hint">Need a starting point?</p><div class="choice-row"><button v-for="starter in starters" :key="starter.name" type="button" class="choice" :disabled="busy" @click="Object.assign(form, starter)">{{ starter.name }}</button></div></div>
          <label for="first-tangent-description"><span class="field-label">A short description <span class="field-note">Optional</span></span><textarea id="first-tangent-description" v-model="form.description" class="input" maxlength="1000" rows="2" placeholder="What would you like to talk about?" :disabled="busy"></textarea></label>
          <label for="first-tangent-kind">What will you publish?<select id="first-tangent-kind" v-model="form.kind" class="input" :disabled="busy"><option value="discussions">Conversations</option><option value="articles">Articles</option></select><span class="hint">Articles are long-form writing with replies. Conversations begin with a Topic.</span></label>
          <div class="onboarding-choices"><p class="field-label" id="first-tangent-art-label">Choose a cover <span class="field-note">Optional</span></p><div class="art-row" role="group" aria-labelledby="first-tangent-art-label">
            <button type="button" class="art-choice art-none" aria-label="No cover" :aria-pressed="form.artwork === ''" :disabled="busy" @click="form.artwork = ''">✦</button>
            <button v-for="entry in starterArtwork" :key="entry.slug" type="button" class="art-choice" :title="entry.name" :aria-label="entry.name" :aria-pressed="form.artwork === entry.url" :disabled="busy" @click="form.artwork = entry.url"><img :src="entry.url" alt="" loading="lazy"></button>
          </div></div>
          <div class="setup-access" v-if="options"><span class="setup-summary-label">Who can take part</span><p><strong>{{ options.audience }}</strong> · {{ options.audienceDescription }}</p><p>{{ options.admissionDescription }}</p><p class="hint">You can adjust access in Tangent settings before starting a conversation.</p></div>
          <div v-else class="setup-access"><p role="status">The initial access settings could not be checked.</p><button type="button" class="btn btn-quiet" @click="site.load()">Check again</button></div>
          <div class="setup-final-summary"><span>Owner <strong>{{ account }}</strong></span><span>Database <strong>{{ activeDatabase }}</strong></span></div>
          <p class="hint">{{ storage?.firstRun === false ? 'Your ownership is saved. Finish setting up this first Tangent.' : 'Finishing makes you the Host owner and creates this first Tangent.' }} You can rename it and add more Tangents anytime. {{ form.kind === 'articles' ? 'Next, you can write its first article.' : 'Next, you can start its first conversation.' }}</p>
          <div class="onboarding-actions"><button class="btn btn-primary" type="submit" :disabled="busy || accountChanged || !options">{{ busy ? 'Creating your Tangent…' : 'Create my first Tangent' }}</button><button type="button" class="btn btn-quiet" :disabled="busy || accountChanged || !options" @click="finish(true)">Use “My Tangent”</button></div>
        </form>
    <aside class="onboarding-preview" aria-label="Your Tangent preview"><div class="edit-community-preview"><CommunityImage :artwork="form.artwork" :name="cardName" /><div><strong id="first-card-name">{{ cardName }}</strong><p id="first-card-description">{{ cardDescription }}</p></div></div><p class="preview-caption">Your Tangent, taking shape.</p></aside>

      </div>
    </div>
    <p v-if="error" id="onboarding-error" class="onboarding-error" role="alert">{{ error }}</p>
  </div>
  <div v-if="router.route.kind === 'onboarding' && onboardingState === 'complete'" class="guided-setup"><h1>Your Host is ready.</h1><p class="lede">{{ site.site?.initialTangentKey ? 'Opening your first Tangent…' : 'Your setup is complete. Continue to your Tangents.' }}</p><a href="/tangents/" class="btn btn-primary">Open Tangents</a></div>
  <div v-if="active && onboardingState === 'waiting_owner'" id="owner-waiting" class="guided-setup">
    <SetupProgress current="account" /><p class="eyebrow">The right account</p><h1>This Space is waiting for its owner.</h1><p class="lede">You’re signed in as {{ account }}. This account can’t set up this Space.</p><form action="/auth/logout" method="post"><input type="hidden" name="return" value="/onboarding/"><button class="btn btn-primary">Switch account</button></form>
  </div>
</template>

<style scoped>
.guided-setup,
.setup-creative-step,
.onboarding-layout,
.onboarding-layout > *,
.onboarding-copy > * {
  min-width: 0;
  max-width: 100%;
}
.guided-setup { overflow-wrap: anywhere; }
.onboarding-layout {
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  align-items: start;
}
.onboarding-copy label { display: block; min-width: 0; }
.onboarding-copy .input { min-width: 0; max-width: 100%; }
.choice-row,
.onboarding-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.choice {
  min-height: 44px;
  max-width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--ts-line);
  border-radius: 8px;
  background: var(--ts-surface);
  color: var(--ts-text-2);
  font: var(--ts-type-ui-small);
  white-space: normal;
}
.choice:not(:disabled):hover { background: var(--ts-hover); }
.art-row {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  width: 100%;
  min-width: 0;
  padding: 6px;
}
.art-choice {
  position: relative;
  display: grid;
  place-items: center;
  width: 100%;
  min-width: 0;
  height: 64px;
  padding: 0;
  border: 1px solid var(--ts-line);
  border-radius: 8px;
  background: var(--ts-surface);
  color: var(--ts-accent);
  overflow: hidden;
}
.art-choice img {
  display: block;
  width: 100%;
  max-width: 100%;
  height: 100%;
  object-fit: cover;
}
.art-none { border-style: dashed; font: 500 28px var(--ts-font-display); }
.art-choice[aria-pressed='true'] {
  border-color: var(--ts-accent);
  box-shadow: 0 0 0 2px var(--ts-page), 0 0 0 4px var(--ts-accent);
}
.art-choice[aria-pressed='true']::after {
  content: '\2713';
  position: absolute;
  right: 4px;
  bottom: 4px;
  display: grid;
  place-items: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: var(--ts-accent);
  color: var(--ts-on-accent);
  font: 700 12px var(--ts-font-ui);
}
.choice:focus-visible,
.art-choice:focus-visible {
  outline: 2px solid var(--ts-text);
  outline-offset: 4px;
}
.setup-back {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 12px;
  max-width: 100%;
  min-height: 44px;
  margin-bottom: 20px;
  padding: 8px 0;
  border: 0;
  background: transparent;
  color: var(--ts-accent);
  text-align: left;
  font: var(--ts-type-ui-small);
}
.setup-back span { color: var(--ts-text-3); }
.onboarding-actions .btn { min-height: 44px; max-width: 100%; white-space: normal; }
.edit-community-preview { display: flex; align-items: center; gap: 12px; min-width: 0; }
.edit-community-preview > div { min-width: 0; }
.edit-community-preview :deep(.community-image) { width: 64px; height: 64px; flex: none; }
@media (max-width: 719px) {
  .onboarding-layout { grid-template-columns: minmax(0, 1fr); gap: 24px; }
  .onboarding-copy .input { font-size: 16px; }
}
@media (max-width: 480px) {
  .onboarding-actions .btn { flex: 1 1 100%; }
}
</style>
