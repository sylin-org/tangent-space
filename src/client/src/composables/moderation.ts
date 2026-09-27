import { httpRequest } from '../api'
import { reactive, ref, watch } from 'vue'
import type { Conversation, Topic } from './conversation'

// The stewardship panel and the report flow, ported from moderation.js: a
// Topic-scoped case queue for those the Topic's capabilities name stewards,
// and the private report signal any participant may send.

const API_ROOT = '/api/v1/experience'

interface CaseItem {
  caseRef: string
  state?: string
  firstReportedAt?: string
  testimonyCount?: number
}

interface CaseDetail {
  case: {
    caseRef: string
    revision?: number
    state?: string
    subjectPostRef?: string
    subjectRevision?: string
    subjectAvailable?: boolean
    testimonyCount?: number
    allowedActions?: string[]
  }
  testimonies: Array<{ statement?: string; reasonCode?: string; submittedAt?: string }>
  testimonyOffset?: number
  nextTestimonyOffset?: number | null
  decisions?: Array<{ reason?: string; action?: string; appliedAt?: string }>
  decisionsTruncated?: boolean
}

interface DecisionBody {
  action: string
  summary: string
  expectedCaseRevision?: number
  expectedSubjectRevision?: string
  deferredUntil?: string
}

async function request<T = unknown>(path: string, body?: unknown, method = 'GET', whole = false): Promise<T> {
  const response = await httpRequest(path, {
    method,
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const value = await response.json().catch(() => ({})) as Record<string, any>
  const problem = value?.result?.problem || value?.problem
  if (!response.ok || value?.status === 'blocked' || problem) {
    const error = new Error(
      problem?.message || value?.message || value?.title
        || (response.status === 403 ? 'Your current role no longer permits this.' : 'This request could not be completed.'),
    ) as Error & { status?: number; code?: string }
    error.status = response.status
    error.code = problem?.code || value?.code
    throw error
  }
  return (whole ? value : value?.result?.data) as T
}

export function createModeration(conversation: Conversation) {
  const { selectedTopic } = conversation

  const stewardshipAvailable = ref(false)
  const panelOpen = ref(false)
  const announcement = ref('')
  const announcementError = ref(false)

  const cases = ref<CaseItem[]>([])
  const casesNextPage = ref<number | null>(null)
  const casesStatus = ref('')
  const casesStatusError = ref(false)
  const listVisible = ref(true)
  const emptyVisible = ref(true)
  const moreVisible = ref(false)
  const refreshing = ref(false)
  const loadingMore = ref(false)

  const detail = ref<CaseDetail | null>(null)
  const detailVisible = ref(false)

  const decision = reactive({ action: 'defer', summary: '', deferredUntil: '', deferFieldVisible: true })
  const previewVisible = ref(false)
  const previewEffect = ref('')
  const applying = ref(false)
  const applyLabel = ref('Apply decision')
  let pendingPreview: { body: DecisionBody; requestId?: string } | null = null

  const reportVisible = ref(false)
  const reportAuthor = ref('')
  const reportStatement = ref('')
  const reportReason = ref('conduct')
  const reportStatus = ref('')
  const reportBusy = ref(false)
  let reportTarget: { messageId: string; tangentKey: string; topicKey: string } | null = null
  let reportRequestId: string | undefined

  let scope: (Topic & { serverRef: string }) | undefined
  let scopeSignature = ''
  let generation = 0

  function allowed(subject: { permissions?: { allowedActions?: string[] } | null } | undefined, action: string): boolean {
    return subject?.permissions?.allowedActions?.includes(action) ?? false
  }

  function caseId(reference: unknown): string | null {
    if (!scope || typeof reference !== 'string') return null
    const prefix = `${scope.serverRef}::${scope.tangentKey}::${scope.key}::case_`
    const value = reference.startsWith(prefix) ? reference.slice(prefix.length) : ''
    return /^[a-f0-9]{64}$/.test(value) ? value : null
  }

  function postId(reference: unknown): string | null {
    if (!scope || typeof reference !== 'string') return null
    const prefix = `${scope.serverRef}::${scope.tangentKey}::${scope.key}::`
    const value = reference.startsWith(prefix) ? reference.slice(prefix.length) : ''
    return /^[A-Za-z0-9-]{1,64}$/.test(value) ? value : null
  }

  function relativeTime(value?: string): string {
    const then = Date.parse(value ?? '')
    if (!Number.isFinite(then)) return 'Recently reported'
    const minutes = Math.max(0, Math.floor((Date.now() - then) / 60000))
    if (minutes < 1) return 'Just reported'
    if (minutes < 60) return `${minutes}m ago`
    const hours = Math.floor(minutes / 60)
    if (hours < 24) return `${hours}h ago`
    return `${Math.floor(hours / 24)}d ago`
  }

  function localDateTime(date: Date): string {
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
    return local.toISOString().slice(0, 16)
  }

  function clearPanel() {
    casesNextPage.value = null
    cases.value = []
    detail.value = null
    detailVisible.value = false
    listVisible.value = true
    emptyVisible.value = true
    moreVisible.value = false
    previewVisible.value = false
    pendingPreview = null
    casesStatus.value = ''
    casesStatusError.value = false
  }

  /** Re-evaluate the stewardship scope when the open selectedTopic changes (moderation.js topic()). */
  async function evaluateScope(value: Topic | null, participantRef: string) {
    const revisions = value ? (value as unknown as { policyRevision?: unknown; sitePolicyRevision?: unknown }) : null
    const signature = value ? `${participantRef}:${value.tangentKey}:${value.key}:${revisions?.policyRevision ?? ''}:${revisions?.sitePolicyRevision ?? ''}` : ''
    if (signature === scopeSignature) return
    scopeSignature = signature
    const version = ++generation
    scope = undefined
    stewardshipAvailable.value = false
    panelOpen.value = false
    closeReport(true)
    clearPanel()
    if (!value?.key || !value?.tangentKey) return
    try {
      const envelope = await request<{ place?: { serverRef?: string; allowedActions?: string[] }; capabilities?: { stewardship?: boolean } }>(
        `${API_ROOT}/topics/${encodeURIComponent(value.key)}?limit=1`, undefined, 'GET', true,
      )
      if (version !== generation || signature !== scopeSignature) return
      const serverRef = envelope?.place?.serverRef
      const actions = envelope?.place?.allowedActions
      let canonical: URL | undefined
      try {
        canonical = serverRef ? new URL(serverRef) : undefined
      } catch {
        canonical = undefined
      }
      if (!canonical || !['http:', 'https:'].includes(canonical.protocol) || canonical.origin !== serverRef || !Array.isArray(actions)) {
        throw new Error('The Topic response did not include a canonical server reference.')
      }
      const steward = envelope?.capabilities?.stewardship === true && actions.includes('list_moderation_cases')
      if (!steward) return
      scope = { ...value, serverRef }
      stewardshipAvailable.value = true
      await loadCases(false)
    } catch (error) {
      if (version !== generation) return
      scopeSignature = ''
      if ((error as Error & { status?: number }).status !== 401 && (error as Error & { status?: number }).status !== 403) {
        announcement.value = (error as Error).message
        announcementError.value = true
      }
    }
  }

  /** The app wires the viewer's participant ref (moderation.js receives it per event). */
  function wireViewer(getRef: () => string) {
    // Immediate: a selectedTopic may already be open when stewardship wires up.
    watch([selectedTopic, getRef], ([value, participantRef]) => {
      void evaluateScope(value, participantRef)
    }, { immediate: true })
  }

  async function loadCases(append = false) {
    if (!scope) return
    const version = generation
    const current = scope
    const page = append ? casesNextPage.value : 1
    if (!page) return
    refreshing.value = !append
    loadingMore.value = append
    casesStatus.value = append ? 'Loading more…' : 'Checking…'
    try {
      const data = await request<{ cases?: CaseItem[]; nextPage?: number | null; saturated?: boolean; atLeast?: boolean }>(
        `${API_ROOT}/topics/${encodeURIComponent(current.key)}/moderation/cases?page=${page}`,
      )
      if (version !== generation || current !== scope) return
      if (!data || !Array.isArray(data.cases)) throw new Error('The case queue response was incomplete.')
      // A case row must carry a well-formed reference (moderation.js renderCase).
      const wellFormed = (data.cases ?? []).filter(item => caseId(item.caseRef))
      cases.value = append ? [...cases.value, ...wellFormed] : wellFormed
      casesNextPage.value = data.nextPage ?? null
      moreVisible.value = !!data.nextPage
      emptyVisible.value = cases.value.length === 0
      casesStatus.value = data.saturated ? 'Some cases reached their bounded history.'
        : data.atLeast ? 'More cases are available; showing a bounded window.' : 'Current'
      casesStatusError.value = false
    } catch (error) {
      if (version !== generation) return
      casesStatus.value = (error as Error).message
      casesStatusError.value = true
      if ((error as Error & { status?: number }).status === 403) void evaluateScope(null, '')
    } finally {
      refreshing.value = false
      loadingMore.value = false
    }
  }

  async function openCase(reference: string, offset = 0) {
    const id = caseId(reference)
    if (!id || !scope) return
    const version = generation
    casesStatus.value = 'Opening case…'
    try {
      const data = await request<CaseDetail>(
        `${API_ROOT}/moderation/cases/${id}?testimonyOffset=${encodeURIComponent(offset)}&testimonyLimit=8&decisionLimit=8`,
      )
      if (version !== generation || !scope) return
      if (!data?.case || !Array.isArray(data.testimonies)) throw new Error('The case response was incomplete.')
      listVisible.value = false
      emptyVisible.value = true
      moreVisible.value = false
      detailVisible.value = true
      detail.value = data
      previewVisible.value = false
      pendingPreview = null
      resetDecisionForm()
      casesStatus.value = ''
    } catch (error) {
      if (version !== generation) return
      casesStatus.value = (error as Error).message
      casesStatusError.value = true
      if ((error as Error & { status?: number }).status === 403) void evaluateScope(null, '')
    }
  }

  function showCaseList() {
    detail.value = null
    pendingPreview = null
    detailVisible.value = false
    listVisible.value = true
    emptyVisible.value = cases.value.length === 0
    moreVisible.value = !!casesNextPage.value
  }

  function resetDecisionForm() {
    decision.action = 'defer'
    decision.summary = ''
    decision.deferredUntil = localDateTime(new Date(Date.now() + 86400000))
    decision.deferFieldVisible = true
    previewVisible.value = false
  }

  function onDecisionActionChange() {
    const defer = decision.action === 'defer'
    decision.deferFieldVisible = defer
  }

  function decisionBody(): DecisionBody {
    const summary = decision.summary.trim()
    if (!summary) throw new Error('Add a short decision note.')
    const current = detail.value
    if (!current) throw new Error('No case is open.')
    const body: DecisionBody = {
      action: decision.action,
      summary,
      expectedCaseRevision: current.case.revision,
      expectedSubjectRevision: current.case.subjectRevision,
    }
    if (decision.action === 'defer') {
      const value = decision.deferredUntil
      if (!value || !Number.isFinite(Date.parse(value))) throw new Error('Choose when this case should return.')
      body.deferredUntil = new Date(value).toISOString()
    }
    return body
  }

  async function previewDecision() {
    if (!detail.value || !scope) return
    const id = caseId(detail.value.case.caseRef)
    try {
      const body = decisionBody()
      casesStatus.value = 'Preparing preview…'
      const data = await request<{ case?: { revision?: number }; effect?: string }>(
        `${API_ROOT}/moderation/cases/${id}/previews`, body, 'POST',
      )
      if (!data?.case || data.case.revision !== detail.value.case.revision) throw new Error('The preview did not match the open case.')
      pendingPreview = { body }
      previewEffect.value = data.effect || 'Review this decision before applying it.'
      previewVisible.value = true
      casesStatus.value = 'Preview ready'
    } catch (error) {
      casesStatus.value = (error as Error).message
      casesStatusError.value = true
      if ((error as Error & { status?: number }).status === 409 && detail.value) await openCase(detail.value.case.caseRef, detail.value.testimonyOffset || 0)
      if ((error as Error & { status?: number }).status === 403) void evaluateScope(null, '')
    }
  }

  function cancelPreview() {
    pendingPreview = null
    previewVisible.value = false
  }

  async function applyDecision() {
    if (!pendingPreview || !detail.value || !scope) return
    const id = caseId(detail.value.case.caseRef)
    pendingPreview.requestId ||= crypto.randomUUID()
    applying.value = true
    applyLabel.value = 'Applying…'
    casesStatus.value = 'Applying decision…'
    try {
      const data = await request<{ case?: { state?: string } }>(
        `${API_ROOT}/moderation/cases/${id}/actions`,
        { ...pendingPreview.body, requestId: pendingPreview.requestId },
        'POST',
      )
      if (!data?.case) throw new Error('The saved decision response was incomplete.')
      announcement.value = data.case.state === 'escalated' ? 'Case escalated to the human Host.' : 'Case deferred for later review.'
      announcementError.value = false
      await openCase(detail.value.case.caseRef, detail.value.testimonyOffset || 0)
      await loadCases(false)
      casesStatus.value = 'Decision saved'
    } catch (error) {
      applyLabel.value = 'Retry apply'
      casesStatus.value = (error as Error).message
      casesStatusError.value = true
      if ((error as Error & { status?: number }).status === 409 && detail.value) await openCase(detail.value.case.caseRef, detail.value.testimonyOffset || 0)
      if ((error as Error & { status?: number }).status === 403) void evaluateScope(null, '')
    } finally {
      applying.value = false
      if (applyLabel.value === 'Applying…') applyLabel.value = 'Apply decision'
    }
  }

  // --- The report flow ---

  function openReport(messageId: string, authorName?: string) {
    const current = selectedTopic.value
    if (!current?.key || !current?.tangentKey) return
    reportTarget = { messageId, tangentKey: current.tangentKey, topicKey: current.key }
    reportRequestId = crypto.randomUUID()
    reportAuthor.value = authorName || 'this participant'
    reportStatement.value = ''
    reportReason.value = 'conduct'
    reportStatus.value = ''
    reportVisible.value = true
  }

  function closeReport(silent = false) {
    reportVisible.value = false
    if (!silent) reportStatus.value = ''
    reportStatement.value = ''
    reportTarget = null
    reportRequestId = undefined
  }

  async function submitReport() {
    if (!reportTarget || !reportRequestId) return
    const topicSnapshot = selectedTopic.value
    if (!topicSnapshot) return
    const statement = reportStatement.value.trim()
    if (!statement) {
      reportStatus.value = 'Describe the concern in your own words.'
      return
    }
    if (!scope || scope.key !== reportTarget.topicKey || scope.tangentKey !== reportTarget.tangentKey) {
      reportStatus.value = 'The Topic context is still loading. Try again in a moment.'
      return
    }
    const postRef = `${scope.serverRef}::${reportTarget.tangentKey}::${reportTarget.topicKey}::${reportTarget.messageId}`
    reportBusy.value = true
    reportStatus.value = 'Sending a private signal…'
    try {
      const data = await request<{ alreadyReported?: boolean; accepted?: boolean; caseSaturated?: boolean }>(
        `${API_ROOT}/topics/${encodeURIComponent(reportTarget.topicKey)}/reports`,
        { requestId: reportRequestId, postRef, reasonCode: reportReason.value, statement },
        'POST',
      )
      const message = data?.alreadyReported ? 'You already reported this post; your original report remains in the case.'
        : data?.accepted ? 'Report sent privately to this Topic’s stewards.'
          : data?.caseSaturated ? 'This case reached its report limit, so your report was not added. Contact the human Host if the concern is urgent.'
            : 'Your report was not added. Please try again or contact the human Host.'
      closeReport(true)
      announcement.value = message
      announcementError.value = false
      if (scope?.key === reportTarget.topicKey) await loadCases(false)
    } catch (error) {
      reportStatus.value = (error as Error).message
    } finally {
      reportBusy.value = false
    }
  }

  return {
    stewardshipAvailable, panelOpen, announcement, announcementError,
    cases, casesNextPage, casesStatus, casesStatusError, listVisible, emptyVisible, moreVisible, refreshing, loadingMore,
    detail, detailVisible, decision, previewVisible, previewEffect, applying, applyLabel,
    reportVisible, reportAuthor, reportStatement, reportReason, reportStatus, reportBusy,
    allowed, relativeTime, postId,
    openPanel: () => {
      if (!scope) return
      panelOpen.value = true
    },
    closePanel: () => {
      panelOpen.value = false
    },
    loadCases, openCase, showCaseList,
    onDecisionActionChange, previewDecision, cancelPreview, applyDecision,
    openReport, closeReport, submitReport,
    wireViewer,
  }
}

export type Moderation = ReturnType<typeof createModeration>
