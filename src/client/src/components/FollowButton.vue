<script setup lang="ts">
import { ref, watch } from 'vue'
import { rolesRequest } from '../composables/roles-api'
import { useSite } from '../stores/site'
const props = defineProps<{ tangent: string; topic?: string; signedIn: boolean }>()
const site = useSite()
const following = ref(false)
const scopeRef = ref('')
const busy = ref(false)
const error = ref('')
let generation = 0
watch(() => [props.tangent, props.topic, props.signedIn, site.site?.participant?.participantRef], async () => {
  const token = ++generation
  following.value = false
  scopeRef.value = ''
  busy.value = false
  error.value = ''
  if (!props.signedIn || !props.tangent) return
  try {
    const result = await rolesRequest<{ following: boolean; scopeRef: string }>('/api/workspace/follow?tangent=' + encodeURIComponent(props.tangent) + (props.topic ? '&topic=' + encodeURIComponent(props.topic) : ''))
    if (token === generation) { following.value = result.following; scopeRef.value = result.scopeRef }
  } catch (cause) { if (token === generation) error.value = (cause as Error).message }
}, { immediate: true })
async function toggle() {
  if (busy.value || !scopeRef.value) return
  const token = generation
  busy.value = true
  const next = !following.value
  try {
    await rolesRequest('/api/v1/experience/watches', { method: 'PUT', body: { scopeRef: scopeRef.value, mode: next ? 'all' : 'none' } })
    if (token === generation) { following.value = next; error.value = '' }
  } catch (cause) { if (token === generation) error.value = (cause as Error).message }
  finally { if (token === generation) busy.value = false }
}
</script>
<template>
  <button v-if="signedIn" class="btn btn-quiet follow-button" type="button" :aria-pressed="following" :disabled="busy || !scopeRef" :title="error || (following ? 'Unfollow' : 'Follow')" @click="toggle">{{ following ? 'Following' : 'Follow' }}</button>
  <span v-if="error" class="follow-error" role="status">{{ error }}</span>
</template>
