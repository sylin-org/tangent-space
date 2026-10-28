<script setup lang="ts">
import { ref, watch } from 'vue'
import { httpRequest } from '../api'
import { useSite } from '../stores/site'

const props = defineProps<{ caseRef: string; participant: { participantRef: string; displayName: string; actions: string[] } }>()
const emit = defineEmits<{ changed: [] }>()
const site = useSite()
const action = ref(''), reason = ref(''), duration = ref('86400'), busy = ref(false), error = ref(''), saved = ref('')
let generation = 0
watch([() => props.caseRef, () => props.participant.participantRef, () => site.site?.participant?.participantRef], () => {
  generation++; action.value = ''; reason.value = ''; busy.value = false; error.value = ''; saved.value = ''
})
async function apply() {
  if (busy.value || !props.participant.actions.includes(action.value) || !reason.value.trim()) return
  const token = generation, selected = action.value
  busy.value = true; error.value = ''; saved.value = ''
  try {
    const response = await httpRequest('/api/v1/experience/moderation/users/' + encodeURIComponent(props.participant.participantRef) + '/management', {
      method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ action: selected, reason: reason.value.trim(), caseRef: props.caseRef,
        ...(selected === 'timeout' ? { durationSeconds: Number(duration.value) } : {}) }),
    })
    const data = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error(data.message || data.title || 'This participant action could not be applied.')
    if (token !== generation) return
    saved.value = selected === 'timeout' ? `Time out applied until ${new Date(data.until).toLocaleString()}.` : 'Participant banned from this Tangent.'
    action.value = ''; reason.value = ''; emit('changed')
  } catch (cause) { if (token === generation) error.value = (cause as Error).message }
  finally { if (token === generation) busy.value = false }
}
</script>

<template>
  <section class="moderation-decision" aria-label="Reported participant actions">
    <h3>Participant actions</h3>
    <p class="hint">{{ participant.displayName }} · Actions apply within this case’s Tangent and stay in the moderation record.</p>
    <p v-if="saved" class="hint" role="status">{{ saved }}</p>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <div v-if="!action" class="form-actions">
      <button v-if="participant.actions.includes('timeout')" class="btn btn-quiet" type="button" @click="action = 'timeout'">Time out participant</button>
      <button v-if="participant.actions.includes('ban')" class="btn btn-danger" type="button" @click="action = 'ban'">Ban from this Tangent</button>
    </div>
    <form v-else @submit.prevent="apply">
      <p>{{ action === 'timeout' ? 'Pause their posts and new topics. They can still read.' : 'Block their access to this Tangent until a steward lifts the ban.' }}</p>
      <label v-if="action === 'timeout'">Duration<select v-model="duration" class="input" :disabled="busy"><option value="3600">1 hour</option><option value="86400">1 day</option><option value="604800">1 week</option></select></label>
      <label>Reason<textarea v-model="reason" class="input" rows="3" maxlength="280" required :disabled="busy"></textarea></label>
      <div class="form-actions"><button class="btn btn-primary" type="submit" :disabled="busy || !reason.trim()">{{ busy ? 'Applying…' : action === 'timeout' ? 'Confirm time out' : 'Confirm ban' }}</button><button class="btn btn-quiet" type="button" :disabled="busy" @click="action = ''">Cancel</button></div>
    </form>
  </section>
</template>
