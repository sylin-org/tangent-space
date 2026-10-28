<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { assertSetupIdentity, SetupIdentityError, storageApi, type StorageState } from '../api'

const props = withDefaults(defineProps<{ guided?: boolean; expectedDid: string }>(), { guided: false })
const emit = defineEmits<{ activated: [restartRequired: boolean]; state: [state: StorageState]; continue: []; identityChanged: [] }>()
const accountChanged = ref(false)
const choosing = ref(!props.guided)
const filter = ref('')
const filterInput = ref<HTMLInputElement | null>(null)
const customHeading = ref<HTMLElement | null>(null)
const connectionFields = ref<HTMLElement | null>(null)
const state = ref<StorageState | null>(null)
const selected = ref('')
const values = reactive<Record<string, string>>({})
const pending = ref<'load' | 'test' | 'activate' | null>(null)
const message = ref('')
const failed = ref(false)
const testId = ref<string | null>(null)
const restartRequired = ref(false)
const provider = computed(() => state.value?.providers.find(item => item.id === selected.value))
const activeName = computed(() => state.value?.providers.find(item => item.id === state.value?.activeProvider)?.name ?? state.value?.activeProvider)
const choices = computed(() => state.value?.providers.filter(item => item.name.toLocaleLowerCase().includes(filter.value.toLocaleLowerCase())) ?? [])
async function openChoices() {
  choosing.value = true
  await nextTick()
  customHeading.value?.scrollIntoView({ block: 'start' })
  filterInput.value?.focus({ preventScroll: true })
}
async function pick(id: string) { selected.value = id; await nextTick(); connectionFields.value?.querySelector('input')?.focus({ preventScroll: true }) }

function invalidate() {
  testId.value = null
  message.value = ''
  failed.value = false
}
watch(selected, () => {
  for (const key of Object.keys(values)) delete values[key]
  invalidate()
})
watch(values, invalidate)

async function load() {
  pending.value = 'load'
  failed.value = false
  try {
    await assertSetupIdentity(props.expectedDid)
    state.value = await storageApi.get()
    selected.value = state.value.activeProvider
    restartRequired.value = state.value.pendingRestart
    emit('activated', state.value.pendingRestart)
    emit('state', state.value)
  } catch (error) {
    if (error instanceof SetupIdentityError) { accountChanged.value = true; emit('identityChanged') }
    failed.value = true
    message.value = (error as Error).message
  } finally { pending.value = null }
}

async function testConnection() {
  if (!provider.value || provider.value.availableForSetup === false || pending.value) return
  const expectedDid = props.expectedDid
  invalidate()
  pending.value = 'test'
  try {
    await assertSetupIdentity(expectedDid)
    const result = await storageApi.test(selected.value, { ...values }, expectedDid)
    testId.value = result.testId
    message.value = result.message || 'Connection tested. This configuration is ready to activate.'
  } catch (error) {
    if (error instanceof SetupIdentityError) { accountChanged.value = true; emit('identityChanged') }
    failed.value = true
    message.value = (error as Error).message
  } finally { pending.value = null }
}

async function activate() {
  if (!testId.value || pending.value) return
  const expectedDid = props.expectedDid
  pending.value = 'activate'
  failed.value = false
  try {
    await assertSetupIdentity(expectedDid)
    const result = await storageApi.activate(testId.value, expectedDid)
    testId.value = null
    restartRequired.value = result.restartRequired
    if (state.value) { state.value.pendingRestart = result.restartRequired; emit('state', state.value) }
    if (!result.restartRequired && state.value) state.value.activeProvider = result.provider
    message.value = result.message
    emit('activated', result.restartRequired)
  } catch (error) {
    if (error instanceof SetupIdentityError) { accountChanged.value = true; emit('identityChanged') }
    failed.value = true
    message.value = (error as Error).message
  } finally { pending.value = null }
}

onMounted(load)
</script>

<template>
  <section class="database-panel stack" :class="{ 'database-guided': guided }" aria-label="Database setup" :aria-busy="pending !== null">
    <header v-if="!guided"><h2 id="database-heading">Database</h2><p class="hint">Choose where this Space stores its content.</p></header>
    <p v-if="pending === 'load'" role="status">Loading database settings…</p>
    <template v-if="state">
      <div v-if="guided && !restartRequired && !choosing" class="database-ready">
        <span class="database-emblem" aria-hidden="true"><svg viewBox="0 0 32 32" fill="none"><ellipse cx="16" cy="7" rx="10" ry="4"/><path d="M6 7v9c0 2.2 4.5 4 10 4s10-1.8 10-4V7M6 16v9c0 2.2 4.5 4 10 4s10-1.8 10-4v-9"/></svg></span>
        <div><p class="database-ready-label">Storage for this Host <span class="database-status">{{ state.activeDurable === false ? 'Needs a durable database' : 'Ready to use' }}</span></p><h2>{{ activeName }}</h2><p v-if="state.activeDurable === false" role="alert">{{ state.activeStorageIssue || 'Choose durable storage before creating your first Tangent.' }}</p><p v-else>Already configured on this Host.<br>No connection details needed.</p></div>
        <span v-if="state.activeDurable !== false" class="database-selected" aria-label="Selected">✓</span>
      </div>
      <p v-else-if="!guided">Active database: <strong>{{ activeName }}</strong></p>
      <div v-if="guided && restartRequired" class="database-restart" role="status"><span class="setup-summary-label">Choice saved</span><h2>One restart, then you’re ready.</h2><p>Restart this Host to apply your database choice. Then sign in again with the same account to finish your first Tangent.</p><p class="hint">Your name and artwork draft stays in this browser tab. Database credentials are never saved in the browser.</p><button class="btn btn-primary" type="button" :disabled="pending !== null" @click="load">{{ pending === 'load' ? 'Checking…' : 'Check Host' }}</button><a class="btn btn-quiet" href="/sign-in/?return=%2Fonboarding%2F">Sign in again</a></div>
      <p v-if="!state.firstRun" class="hint">Database setup is complete. Moving this Space to another database requires transferring its existing data.</p>
      <div v-if="guided && !restartRequired && !choosing" class="database-next"><button class="btn btn-primary" type="button" :disabled="pending !== null || failed || accountChanged || state.activeDurable === false" @click="emit('continue')">Continue with {{ activeName }} <span aria-hidden="true">→</span></button><button v-if="state.firstRun" class="btn btn-quiet" type="button" :disabled="accountChanged" @click="openChoices">{{ state.activeDurable === false ? 'Choose a durable database' : 'Choose another database' }}</button></div>
      <form v-if="state.firstRun && !restartRequired && choosing" class="stack database-custom" @submit.prevent="testConnection">
        <header v-if="guided" ref="customHeading" class="database-custom-heading"><h2>Connect your database</h2><p class="hint">Choose an installed provider. We’ll test the connection before you save it.</p><p class="database-current">Currently using <strong>{{ activeName }}</strong><button type="button" :disabled="pending !== null || accountChanged" @click="choosing = false; invalidate()">Keep {{ activeName }}</button></p></header>
        <fieldset class="stack" :disabled="pending !== null || accountChanged">
          <legend class="visually-hidden">Database connection</legend>
          <template v-if="guided"><label class="database-search">Find a database<input ref="filterInput" v-model="filter" class="input" type="search" placeholder="Search installed providers" autocomplete="off"></label><div class="database-provider-grid" role="group" aria-label="Installed database providers"><button v-for="item in choices" :key="item.id" type="button" class="database-provider" :aria-pressed="selected === item.id" :disabled="item.availableForSetup === false" :title="item.unavailableReason ?? undefined" @click="pick(item.id)"><span>{{ item.name }}</span><span v-if="item.availableForSetup === false" class="database-provider-detail">{{ item.unavailableReason || 'Unavailable for setup' }}</span><span v-else-if="selected === item.id" class="database-provider-detail">Selected</span></button></div><p v-if="!choices.length" class="hint">No installed providers match that search.</p></template>
          <label v-else>Provider
            <select v-model="selected" class="input" required>
              <option v-for="item in state.providers" :key="item.id" :value="item.id" :disabled="item.availableForSetup === false">{{ item.name }}</option>
            </select>
          </label>
          <div ref="connectionFields" class="stack database-fields"><h3 v-if="guided">{{ provider?.name }} connection</h3>
          <label v-for="field in provider?.fields ?? []" :key="selected + ':' + field.name">
            {{ field.label }}<span v-if="!field.required" class="hint"> (optional)</span>
            <input v-model="values[field.name]" class="input" :name="field.name" :required="field.required" :placeholder="field.placeholder ?? undefined"
              :type="field.secret ? 'password' : field.type === 'url' ? 'url' : 'text'"
              :inputmode="field.type === 'number' ? 'numeric' : undefined"
              autocomplete="off" spellcheck="false">
          </label>
          </div>
          <p v-if="state.firstRun" class="hint">For file databases in Docker, use a path in the mounted state directory (default /state).</p>
          <p v-if="state.changeRequiresRestart" class="hint">Saving a new database choice requires restarting Tangent before you create your Space.</p>
          <div class="database-actions">
            <button class="btn" :class="guided && !testId ? 'btn-primary' : 'btn-quiet'" type="submit" :disabled="!provider || provider.availableForSetup === false">{{ pending === 'test' ? 'Testing connection…' : 'Test connection' }}</button>
            <button v-if="testId" class="btn btn-primary" type="button" @click="activate">{{ pending === 'activate' ? 'Saving…' : guided ? 'Save database choice' : 'Activate tested configuration' }}</button>
            <button v-if="guided" class="btn btn-quiet" type="button" @click="choosing = false; invalidate()">Keep {{ activeName }}</button>
          </div>
        </fieldset>
      </form>
    </template>
    <p v-if="message" class="action-status" :class="{ error: failed }" role="status">{{ message }}</p>
    <p v-if="restartRequired && !guided" class="hint">Restart the server to apply the saved database configuration.{{ state?.firstRun ? ' Then sign in again to create your first Tangent.' : '' }}</p>
    <button v-if="failed && !accountChanged" type="button" class="btn btn-quiet" :disabled="pending !== null" @click="load">Retry</button>
  </section>
</template>

<style scoped>
fieldset { margin: 0; padding: 0; border: 0; min-width: 0; }
.database-actions { display: flex; flex-wrap: wrap; gap: .75rem; }
input, select { width: 100%; min-width: 0; }
</style>
