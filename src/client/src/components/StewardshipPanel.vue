<script setup lang="ts">
// The stewardship panel, ported from moderation.js's rendering: the bounded case
// queue, the case detail with testimony and decisions, and the preview-then-apply
// decision flow.
import { computed, watchEffect } from 'vue'
import type { Moderation } from '../composables/moderation'

const props = defineProps<{ moderation: Moderation }>()

const m = props.moderation

watchEffect(() => {
  document.body.dataset.stewardship = m.stewardshipAvailable.value ? 'true' : 'false'
})
watchEffect(() => {
  document.body.classList.toggle('steward-panel-open', m.panelOpen.value)
})

const deferredMin = computed(() => {
  const local = new Date(Date.now() + 60000 - new Date().getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
})
const deferredMax = computed(() => {
  const local = new Date(Date.now() + 7 * 86400000 - new Date().getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
})

const detailHeading = computed(() => (m.detail.value?.case.state === 'escalated' ? 'With the human Host' : 'A reported post'))

const subjectLink = computed(() => {
  const detail = m.detail.value
  if (!detail) return null
  const subject = m.postId(detail.case.subjectPostRef)
  const tangentKey = detail.case.caseRef.split('::')[1]
  return subject && tangentKey ? `/t/${encodeURIComponent(tangentKey)}/${encodeURIComponent(subject)}` : null
})

const actionable = computed(() => {
  const detail = m.detail.value
  if (!detail) return false
  const actions = Array.isArray(detail.case.allowedActions) ? detail.case.allowedActions : []
  return actions.includes('preview_moderation_action') && actions.includes('apply_moderation_action') && detail.case.subjectAvailable !== false
})

const testimonyShown = computed(() => m.detail.value?.testimonies.length ?? 0)

function testimonyNavOffset(delta: number): number {
  const detail = m.detail.value
  const offset = Number.isInteger(detail?.testimonyOffset) ? detail!.testimonyOffset! : 0
  return Math.max(0, offset + delta)
}
</script>

<template>
  <p class="stewardship-intro">Reports are signals, not verdicts. Review each participant’s account and the reported Post, then defer or involve the accountable human Host.</p>
  <div class="stewardship-tools">
    <button id="stewardship-refresh" class="btn btn-quiet" type="button" :disabled="m.refreshing.value" @click="m.loadCases(false)">Refresh</button>
    <span id="stewardship-status" class="hint" role="status" aria-live="polite" :class="{ error: m.casesStatusError.value }">{{ m.casesStatus.value }}</span>
  </div>
  <ol id="moderation-cases" class="moderation-cases" :hidden="!m.listVisible.value">
    <li v-for="item in m.cases.value" :key="item.caseRef" class="moderation-case-row">
      <button class="moderation-case-button" type="button" @click="m.openCase(item.caseRef, 0)">
        <span class="moderation-case-top">
          <span class="moderation-case-state">{{ item.state || 'open' }}</span>
          <span class="moderation-case-time">{{ m.relativeTime(item.firstReportedAt) }}</span>
        </span>
        <span class="moderation-case-count">{{ item.testimonyCount ?? 0 }} participant report{{ (item.testimonyCount ?? 0) === 1 ? '' : 's' }}</span>
      </button>
    </li>
  </ol>
  <p id="moderation-empty" class="stewardship-empty" :hidden="m.listVisible.value ? !m.emptyVisible.value : true">Nothing needs review here.</p>
  <button id="moderation-more" class="btn btn-quiet" type="button" :hidden="!(m.listVisible.value && m.moreVisible.value)" :disabled="m.loadingMore.value" @click="m.loadCases(true)">More cases</button>

  <section id="moderation-detail" class="moderation-detail" aria-labelledby="moderation-case-heading" :hidden="!m.detailVisible.value">
    <button id="moderation-back" class="moderation-back" type="button" @click="m.showCaseList">← Case list</button>
    <div v-if="m.detail.value" id="moderation-detail-content">
      <h3 id="moderation-case-heading" tabindex="-1">{{ detailHeading }}</h3>
      <p class="moderation-case-summary">
        <a v-if="subjectLink" :href="subjectLink">Open the reported post</a>
        <span> · Case revision {{ m.detail.value.case.revision ?? '—' }} · Post revision {{ String(m.detail.value.case.subjectRevision || 'unavailable').slice(0, 12) }}…</span>
      </p>
      <div class="moderation-metrics">
        <div class="moderation-metric"><strong>{{ m.detail.value.case.testimonyCount ?? 0 }}</strong><span>participant reports</span></div>
        <div class="moderation-metric"><strong>{{ m.detail.value.case.state || 'open' }}</strong><span>state</span></div>
      </div>
      <h4 class="moderation-section-title">Participant testimony</h4>
      <div class="moderation-testimonies">
        <blockquote v-for="(testimony, index) in m.detail.value.testimonies" :key="index" class="moderation-testimony">
          <p>{{ testimony.statement || '' }}</p>
          <footer>{{ testimony.reasonCode || 'report' }} · {{ m.relativeTime(testimony.submittedAt) }}</footer>
        </blockquote>
        <p v-if="!m.detail.value.testimonies.length" class="moderation-terminal">No testimony is available in this page.</p>
      </div>
      <div class="moderation-testimony-nav">
        <button class="btn btn-quiet" type="button" :disabled="(m.detail.value.testimonyOffset ?? 0) === 0" @click="m.openCase(m.detail.value.case.caseRef, testimonyNavOffset(-8))">Previous reports</button>
        <span class="hint">{{ testimonyShown ? `Showing ${(m.detail.value.testimonyOffset ?? 0) + 1}–${(m.detail.value.testimonyOffset ?? 0) + testimonyShown}` : 'No reports in this page' }}</span>
        <button class="btn btn-quiet" type="button" :disabled="m.detail.value.nextTestimonyOffset == null" @click="m.openCase(m.detail.value.case.caseRef, m.detail.value.nextTestimonyOffset ?? 0)">More reports</button>
      </div>
      <template v-if="m.detail.value.decisions?.length">
        <h4 class="moderation-section-title">Recent decisions</h4>
        <div class="moderation-decisions">
          <div v-for="(row, index) in m.detail.value.decisions" :key="index" class="moderation-decision-row">
            <p>{{ row.reason || row.action || 'Decision' }}</p>
            <footer>{{ row.action || 'decision' }} · {{ m.relativeTime(row.appliedAt) }}</footer>
          </div>
        </div>
        <p v-if="m.detail.value.decisionsTruncated === true" class="moderation-terminal">Older decisions are retained but omitted from this bounded view.</p>
      </template>
      <p v-if="!actionable" class="moderation-terminal">
        {{ m.detail.value.case.state === 'escalated' ? 'The accountable human Host now owns the next step.' : 'This case cannot be changed from your current role or Post revision.' }}
      </p>
    </div>
    <form id="moderation-decision" class="moderation-decision" :hidden="!actionable || m.previewVisible.value" @submit.prevent="m.previewDecision">
      <h3 id="moderation-detail-title">Choose the next step</h3>
      <label>Action
        <select v-model="m.decision.action" class="input" name="action" @change="m.onDecisionActionChange">
          <option value="defer">Defer and revisit</option>
          <option value="escalate">Escalate to the human Host</option>
        </select>
      </label>
      <label id="moderation-defer-field" :hidden="!m.decision.deferFieldVisible">Revisit after
        <input v-model="m.decision.deferredUntil" class="input" name="deferredUntil" type="datetime-local" required :min="deferredMin" :max="deferredMax">
      </label>
      <label>Decision note<textarea v-model="m.decision.summary" class="input" name="summary" rows="3" maxlength="280" required placeholder="What should the next steward understand?"></textarea></label>
      <button id="moderation-preview" class="btn btn-quiet" type="submit">Preview decision</button>
    </form>
    <div id="moderation-preview-card" class="moderation-preview-card" :hidden="!m.previewVisible.value">
      <p class="eyebrow">Preview — nothing changed yet</p>
      <p id="moderation-preview-effect">{{ m.previewEffect.value }}</p>
      <div class="moderation-preview-actions">
        <button id="moderation-apply" class="btn btn-primary" type="button" :disabled="m.applying.value" @click="m.applyDecision">{{ m.applyLabel.value }}</button>
        <button id="moderation-preview-cancel" class="btn btn-quiet" type="button" @click="m.cancelPreview">Change</button>
      </div>
    </div>
  </section>
</template>
