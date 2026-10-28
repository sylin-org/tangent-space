<script setup lang="ts">
import { ref, watch } from 'vue'
import { rolesRequest } from '../composables/roles-api'

const props = defineProps<{ tangent: string; actorKey: string }>()
const emit = defineEmits<{ changed: [] }>()
interface JoiningState { status: string; admission: 'Open' | 'Approval'; canJoin: boolean; canLeave: boolean }
const state = ref<JoiningState | null>(null)
const busy = ref(false), error = ref(''), leaving = ref(false)
let generation = 0
const path = () => '/api/v1/tangents/' + encodeURIComponent(props.tangent) + '/joining'
async function load() {
  const token = ++generation
  state.value = null
  error.value = ''
  leaving.value = false
  if (!props.actorKey || !props.tangent) return
  try {
    const result = await rolesRequest<JoiningState>(path())
    if (token === generation) state.value = result
  } catch (cause) { if (token === generation) error.value = (cause as Error).message }
}
async function change(leave = false) {
  if (busy.value) return
  const token = generation
  busy.value = true
  error.value = ''
  try {
    await rolesRequest(path(), { method: leave ? 'DELETE' : 'POST', body: {} })
    if (token !== generation) return
    await load()
    emit('changed')
  } catch (cause) { if (token === generation) error.value = (cause as Error).message }
  finally { busy.value = false }
}
watch(() => [props.tangent, props.actorKey], load, { immediate: true })
</script>

<template>
  <section v-if="actorKey && (error || state && (state.status !== 'member' || state.canLeave))" class="tangent-joining" aria-label="Tangent membership">
    <template v-if="state?.canJoin">
      <p>{{ state.admission === 'Approval' ? 'A steward approves requests to join this Tangent.' : 'Join this Tangent to become a member.' }} Membership and following are separate choices.</p>
      <button class="btn btn-quiet" type="button" :disabled="busy" @click="change()">{{ busy ? 'Sending…' : state.admission === 'Approval' ? 'Request to join' : 'Join Tangent' }}</button>
    </template>
    <p v-else-if="state?.status === 'pending'" role="status">Your request to join is waiting for a Tangent steward.</p>
    <p v-else-if="state?.status === 'refused'">Your request to join was refused. Contact a Tangent steward.</p>
    <p v-else-if="state?.status === 'removed' || state?.status === 'restricted'">Your participation is restricted. Contact a Tangent steward.</p>
    <template v-else-if="state?.canLeave">
      <p>You are a member of this Tangent.</p>
      <button v-if="!leaving" class="btn btn-quiet" type="button" @click="leaving = true">Leave Tangent</button>
      <div v-else class="form-actions"><p>Leave this Tangent? Your posts stay attributed to you. Member-only conversations may no longer be readable.</p><button class="btn btn-quiet" type="button" :disabled="busy" @click="leaving = false">Stay</button><button class="btn btn-danger" type="button" :disabled="busy" @click="change(true)">Leave Tangent</button></div>
    </template>
    <p v-if="error" class="error" role="alert">{{ error }} <button class="btn btn-quiet" type="button" :disabled="busy" @click="load">Retry</button></p>
  </section>
</template>
