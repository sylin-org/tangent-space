<script setup lang="ts">
// The report form, ported from moderation.js: a private signal to the Topic's
// stewards that creates or adds to a case — it removes nothing and decides nothing.
import { onMounted, onScopeDispose } from 'vue'
import type { Moderation } from '../composables/moderation'

const props = defineProps<{ moderation: Moderation }>()

const m = props.moderation

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') m.closeReport()
}

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
})

onScopeDispose(() => {
  document.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <section id="report-panel" class="report-panel" :hidden="!m.reportVisible.value" aria-labelledby="report-title">
    <form id="report-form" @submit.prevent="m.submitReport">
      <header class="report-heading">
        <div><p class="eyebrow">A private signal to stewards</p><h2 id="report-title">Report this post</h2></div>
        <button id="report-close" class="btn btn-quiet" type="button" aria-label="Close report form" @click="m.closeReport()">Close</button>
      </header>
      <p id="report-context" class="report-context">Post by {{ m.reportAuthor.value }}</p>
      <p class="report-explanation">This creates or adds to a private moderation case. It does not remove the post or decide that a rule was broken.</p>
      <label>What concerns you?
        <select v-model="m.reportReason.value" class="input" name="reasonCode">
          <option value="conduct">Harassment or harmful conduct</option>
          <option value="spam">Spam or manipulation</option>
          <option value="privacy">Privacy or safety</option>
          <option value="other">Something else</option>
        </select>
      </label>
      <label>What should the stewards understand?
        <textarea ref="statementField" v-model="m.reportStatement.value" class="input" name="statement" rows="4" maxlength="512" required placeholder="Describe the concern in your own words."></textarea>
      </label>
      <div class="report-actions">
        <button id="report-submit" class="btn btn-primary" type="submit" :disabled="m.reportBusy.value">Send report</button>
        <button id="report-cancel" class="btn btn-quiet" type="button" @click="m.closeReport()">Cancel</button>
      </div>
      <p id="report-status" class="hint" role="status" aria-live="polite">{{ m.reportStatus.value }}</p>
    </form>
  </section>
</template>
