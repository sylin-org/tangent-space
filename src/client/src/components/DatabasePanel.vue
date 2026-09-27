<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { storageApi, type StorageState } from '../api'

const emit = defineEmits<{ activated: [restartRequired: boolean] }>()
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
    state.value = await storageApi.get()
    selected.value = state.value.activeProvider
    restartRequired.value = state.value.pendingRestart
    emit('activated', state.value.pendingRestart)
  } catch (error) {
    failed.value = true
    message.value = (error as Error).message
  } finally { pending.value = null }
}

async function testConnection() {
  if (!provider.value || pending.value) return
  invalidate()
  pending.value = 'test'
  try {
    const result = await storageApi.test(selected.value, { ...values })
    testId.value = result.testId
    message.value = result.message || 'Connection tested. This configuration is ready to activate.'
  } catch (error) {
    failed.value = true
    message.value = (error as Error).message
  } finally { pending.value = null }
}

async function activate() {
  if (!testId.value || pending.value) return
  pending.value = 'activate'
  failed.value = false
  try {
    const result = await storageApi.activate(testId.value)
    testId.value = null
    restartRequired.value = result.restartRequired
    if (!result.restartRequired && state.value) state.value.activeProvider = result.provider
    message.value = result.message
    emit('activated', result.restartRequired)
  } catch (error) {
    failed.value = true
    message.value = (error as Error).message
  } finally { pending.value = null }
}

onMounted(load)
</script>

<template>
  <section class="database-panel stack" aria-labelledby="database-heading" :aria-busy="pending !== null">
    <header><h2 id="database-heading">Database</h2><p class="hint">Choose where this Space stores its content.</p></header>
    <p v-if="pending === 'load'" role="status">Loading database settings…</p>
    <template v-if="state">
      <p>Active database: <strong>{{ activeName }}</strong></p>
      <p v-if="state.firstRun" class="hint">For file databases in Docker, use a path in the mounted state directory (default /state).</p>
      <p v-if="!state.firstRun" class="hint">Database setup is complete. Moving this Space to another database requires transferring its existing data.</p>
      <form v-else-if="!restartRequired" class="stack" @submit.prevent="testConnection">
        <fieldset class="stack" :disabled="pending !== null">
          <legend class="visually-hidden">Database connection</legend>
          <label>Provider
            <select v-model="selected" class="input" required>
              <option v-for="item in state.providers" :key="item.id" :value="item.id">{{ item.name }}</option>
            </select>
          </label>
          <label v-for="field in provider?.fields ?? []" :key="selected + ':' + field.name">
            {{ field.label }}<span v-if="!field.required" class="hint"> (optional)</span>
            <input v-model="values[field.name]" class="input" :name="field.name" :required="field.required" :placeholder="field.placeholder ?? undefined"
              :type="field.secret ? 'password' : field.type === 'url' ? 'url' : 'text'"
              :inputmode="field.type === 'number' ? 'numeric' : undefined"
              autocomplete="off" spellcheck="false">
          </label>
          <p v-if="state.changeRequiresRestart" class="hint">Activation saves the tested configuration. The server must restart to use it.</p>
          <div class="database-actions">
            <button class="btn btn-quiet" type="submit" :disabled="!provider">{{ pending === 'test' ? 'Testing…' : 'Test connection' }}</button>
            <button v-if="testId" class="btn btn-primary" type="button" @click="activate">{{ pending === 'activate' ? 'Activating…' : 'Activate tested configuration' }}</button>
          </div>
        </fieldset>
      </form>
    </template>
    <p v-if="message" class="action-status" :class="{ error: failed }" role="status">{{ message }}</p>
    <p v-if="restartRequired" class="hint">Restart the server to apply the saved database configuration.{{ state?.firstRun ? ' Then sign in again to create your first Tangent.' : '' }}</p>
    <button v-if="!state && failed" type="button" class="btn btn-quiet" :disabled="pending !== null" @click="load">Retry</button>
  </section>
</template>

<style scoped>
.database-panel { margin-top: 2rem; padding-top: 1.5rem; border-top: 1px solid var(--line); }
fieldset { margin: 0; padding: 0; border: 0; min-width: 0; }
.database-actions { display: flex; flex-wrap: wrap; gap: .75rem; }
input, select { width: 100%; min-width: 0; }
</style>
