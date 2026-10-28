<script setup lang="ts">
import type { SettingsChanges } from '../composables/settings-changes'
defineProps<{ changes: SettingsChanges }>()
</script>
<template>
  <div v-if="changes.count.value" class="unified-savebar" aria-label="Unsaved settings">
    <div><strong>{{ changes.count.value }} unsaved {{ changes.count.value === 1 ? 'change' : 'changes' }}</strong><p role="status">{{ changes.feedback.value || 'Unsaved changes stay in this tab.' }}</p></div>
    <button type="button" class="btn btn-quiet" :disabled="changes.busy.value" @click="changes.discard">Discard</button>
    <button type="button" class="btn btn-primary" :disabled="changes.busy.value" @click="changes.save">{{ changes.busy.value ? 'Saving…' : 'Save changes' }}</button>
  </div>
</template>
