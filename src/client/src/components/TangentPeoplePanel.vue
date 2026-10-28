<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { rolesRequest } from '../composables/roles-api'
import {registerSettingsChange,useSettingsSubmit} from '../composables/settings-changes'
import ParticipantActions from './ParticipantActions.vue'
import ParticipantAvatar from './ParticipantAvatar.vue'

interface Member {
  participantId: string
  did?: string
  handle?: string
  displayName?: string
  avatar?: string
  classification?: string
  role: string | null
  isOwner: boolean
  canChangeRole: boolean
  allowedRoles: string[]
}
interface Page { items: Member[]; nextCursor: string | null }

const props = defineProps<{ tangent: string; actor: string }>()
const people = ref<Member[]>([])
const nextCursor = ref<string | null>(null)
const pageCursors = ref<string[]>([''])
const loading = ref(false)
const error = ref('')
const search = ref('')
const lookup = ref<Member | null>(null)
const lookupError = ref('')
const lookupLoading = ref(false)
const selected = ref<Member | null>(null)
const proposedRole = ref('')
const saving = ref(false)
const feedback = ref('')
const requests = ref<Array<{ id: string; displayName: string; handle?: string; avatar?: string; classification?: string; requestedAt: string }>>([])
const requestsPage = ref(1), nextRequestsPage = ref<number | null>(null), requestsBusy = ref(false), requestsError = ref(''), canReview = ref(false)
let scopeVersion = 0
let listToken = 0
let lookupToken = 0
const scope = computed(() => `${props.actor}:${props.tangent}`)
const target = computed(() => selected.value)
const roleDirty = computed(() => !!selected.value && proposedRole.value !== activeRole(selected.value.role))
async function loadRequests(page = 1) {
  const current = scope.value
  requestsBusy.value = true
  requestsError.value = ''
  try {
    const base = '/api/v1/tangents/' + encodeURIComponent(props.tangent) + '/joining'
    const state = await rolesRequest<{ canReview: boolean }>(base)
    if (current !== scope.value) return
    canReview.value = state.canReview
    if (!state.canReview) return
    const data = await rolesRequest<{ items: typeof requests.value; nextPage: number | null }>(base + '/requests?page=' + page)
    if (current !== scope.value) return
    requests.value = data.items
    requestsPage.value = page
    nextRequestsPage.value = data.nextPage
  } catch (cause) { if (current === scope.value) requestsError.value = (cause as Error).message }
  finally { if (current === scope.value) requestsBusy.value = false }
}
async function decideRequest(id: string, accept: boolean) {
  if (requestsBusy.value) return
  const current = scope.value
  requestsBusy.value = true
  requestsError.value = ''
  try {
    await rolesRequest('/api/v1/tangents/' + encodeURIComponent(props.tangent) + '/joining/requests/' + encodeURIComponent(id), { method: 'POST', body: { accept } })
    if (current !== scope.value) return
    await Promise.all([loadRequests(requestsPage.value), load(pageCursors.value.at(-1) || undefined)])
  } catch (cause) { if (current === scope.value) requestsError.value = (cause as Error).message }
  finally { if (current === scope.value) requestsBusy.value = false }
}
function roleLabel(role: string | null): string {
  return role === 'Admin' ? 'Administrator' : role === 'Reader' ? 'Read only' : role === 'Member' || role === 'Owner' ? role : 'Not a member'
}
function activeRole(role: string | null): string { return ['Admin', 'Member', 'Reader'].includes(role || '') ? role! : '' }

async function load(cursor?: string) {
  const current = scope.value
  const version = scopeVersion
  const token = ++listToken
  loading.value = true
  error.value = ''
  try {
    const path = `/api/v1/tangents/${encodeURIComponent(props.tangent)}/members` + (cursor ? `?cursor=${encodeURIComponent(cursor)}` : '')
    const page = await rolesRequest<Page>(path, { scope: 'access' })
    if (current !== scope.value || version !== scopeVersion || token !== listToken) return
    people.value = page.items
    nextCursor.value = page.nextCursor
  } catch (cause) {
    if (current === scope.value && version === scopeVersion && token === listToken) error.value = (cause as Error).message || 'People could not be loaded.'
  } finally {
    if (current === scope.value && version === scopeVersion && token === listToken) loading.value = false
  }
}

function nextPage() {
  if (!nextCursor.value) return
  pageCursors.value.push(nextCursor.value)
  void load(nextCursor.value)
}
function previousPage() {
  if (pageCursors.value.length <= 1) return
  pageCursors.value.pop()
  void load(pageCursors.value.at(-1) || undefined)
}

async function findPerson() {
  const identifier = search.value.trim()
  if (!identifier) return
  const current = scope.value
  const version = scopeVersion
  const token = ++lookupToken
  lookupLoading.value = true
  lookupError.value = ''
  lookup.value = null
  try {
    const result = await rolesRequest<Member>(`/api/v1/tangents/${encodeURIComponent(props.tangent)}/membership-preview?identifier=${encodeURIComponent(identifier)}`, { scope: 'access' })
    if (current !== scope.value || version !== scopeVersion || token !== lookupToken) return
    lookup.value = result
    choose(result)
  } catch (cause) {
    if (current === scope.value && version === scopeVersion && token === lookupToken) lookupError.value = (cause as Error).message || 'That participant could not be found.'
  } finally {
    if (current === scope.value && version === scopeVersion && token === lookupToken) lookupLoading.value = false
  }
}

function choose(person: Member) {
  if (saving.value || roleDirty.value) return
  selected.value = person
  proposedRole.value = activeRole(person.role)
  feedback.value = ''
}

async function saveRole() {
  const person = selected.value
  const role = proposedRole.value
  if (!person || !person.canChangeRole || !person.allowedRoles.includes(role) || role === activeRole(person.role) || saving.value) return
  const current = scope.value
  const id = person.participantId
  saving.value = true
  feedback.value = 'Saving role…'
  try {
    await rolesRequest(`/api/tangents/${encodeURIComponent(props.tangent)}/roles/${encodeURIComponent(id)}`, { method: 'PUT', body: { role }, scope: 'access' })
    if (current !== scope.value || selected.value?.participantId !== id) return
    selected.value = { ...person, role }
    people.value = people.value.map(item => item.participantId === id ? { ...item, role } : item)
    if (lookup.value?.participantId === id) lookup.value = { ...lookup.value, role }
    feedback.value = `${person.displayName || person.handle || 'Participant'} is now ${roleLabel(role)}.`
  } catch (cause) {
    if (current === scope.value && selected.value?.participantId === id) feedback.value = (cause as Error).message || 'The role could not be saved.'
  } finally {
    if (current === scope.value) saving.value = false
  }
}

const coordinated=registerSettingsChange({label:'Tangent role',count:()=>selected.value&&proposedRole.value!==activeRole(selected.value.role)?1:0,busy:()=>saving.value,save:saveRole,discard:()=>{if(selected.value)proposedRole.value=activeRole(selected.value.role)},status:()=>feedback.value})
const submitSettings=useSettingsSubmit()
watch(scope, () => {
  scopeVersion++
  listToken++
  lookupToken++
  people.value = []
  nextCursor.value = null
  pageCursors.value = ['']
  lookup.value = null
  loading.value = false
  lookupLoading.value = false
  saving.value = false
  error.value = ''
  lookupError.value = ''
  selected.value = null
  search.value = ''
  feedback.value = ''
  requests.value = []
  canReview.value = false
  requestsPage.value = 1
  nextRequestsPage.value = null
  requestsBusy.value = false
  requestsError.value = ''
  void load()
  void loadRequests()
  const participant = new URL(window.location.href).searchParams.get('participant')
  if (participant) { search.value = participant; void findPerson() }
}, { immediate: true })
</script>

<template>
  <section class="tangent-people-section" aria-labelledby="tangent-people-title">
    <div class="tangent-section-intro"><h2 id="tangent-people-title">People in this Tangent</h2><p>Find people and manage their role in this community.</p></div>
    <section v-if="canReview || requestsError" class="tangent-join-requests" aria-labelledby="tangent-join-requests-title">
      <h3 id="tangent-join-requests-title">Requests to join</h3>
      <p v-if="requestsBusy" class="hint" role="status">Loading requests…</p>
      <p v-if="requestsError" class="error" role="alert">{{ requestsError }} <button class="btn btn-quiet" type="button" @click="loadRequests(requestsPage)">Retry</button></p>
      <p v-else-if="!requestsBusy && !requests.length" class="hint">No requests waiting for review.</p>
      <article v-for="person in requests" :key="person.id" class="joining-request"><ParticipantAvatar :participant="person"/><div><strong>{{ person.displayName }}</strong> <span v-if="person.classification?.toLowerCase() === 'agent'" class="agent-badge">Agent</span><p class="hint">Requested {{ new Date(person.requestedAt).toLocaleDateString() }}</p></div><button class="btn btn-primary" type="button" :disabled="requestsBusy" @click="decideRequest(person.id, true)">Admit</button><button class="btn btn-quiet" type="button" :disabled="requestsBusy" @click="decideRequest(person.id, false)">Refuse</button></article>
      <div class="directory-pagination"><button v-if="requestsPage > 1" class="btn btn-quiet" type="button" :disabled="requestsBusy" @click="loadRequests(requestsPage - 1)">Previous requests</button><button v-if="nextRequestsPage" class="btn btn-quiet" type="button" :disabled="requestsBusy" @click="loadRequests(nextRequestsPage)">Next requests</button></div>
    </section>
    <form class="tangent-person-lookup" @submit.prevent="findPerson">
      <label for="tangent-person-search">Find someone by handle or DID</label>
      <div><input id="tangent-person-search" v-model="search" class="input" autocomplete="off" spellcheck="false" placeholder="@someone.example" :disabled="saving || roleDirty"><button class="btn btn-quiet" :disabled="lookupLoading || saving || roleDirty || !search.trim()" type="submit">{{ lookupLoading ? 'Finding…' : 'Find person' }}</button></div>
      <p v-if="lookupError" class="hint error" role="status">{{ lookupError }}</p>
    </form>
    <div class="tangent-member-list" aria-label="Community members">
      <button v-for="person in people" :key="person.participantId" class="tangent-member-row" type="button" :disabled="saving || roleDirty" :aria-pressed="target?.participantId === person.participantId" @click="choose(person)">
        <ParticipantAvatar :participant="person" /><span class="tangent-member-identity"><strong>{{ person.displayName || person.handle || person.did || person.participantId }}</strong><span v-if="person.classification?.toLowerCase()==='agent'" class="agent-badge">Agent</span><small v-if="person.handle">{{ person.handle }}</small></span><span class="tangent-member-role">{{ roleLabel(person.isOwner ? 'Owner' : person.role) }}<span v-if="person.canChangeRole" aria-hidden="true"> ›</span></span>
      </button>
    </div>
    <p v-if="loading" role="status" class="hint">Loading people…</p>
    <p v-if="error" role="status" class="hint error">{{ error }} <button type="button" class="btn btn-quiet" @click="load(pageCursors.at(-1) || undefined)">Retry</button></p>
    <p v-if="!loading && !error && !people.length" class="hint">No members are listed yet.</p>
    <p v-if="roleDirty" class="hint">Save or discard this role change before selecting another participant.</p>
    <div class="tangent-people-pages"><button v-if="pageCursors.length > 1" type="button" class="btn btn-quiet" :disabled="loading || saving" @click="previousPage">Previous</button><button v-if="nextCursor" type="button" class="btn btn-quiet" :disabled="loading || saving" @click="nextPage">Next people</button></div>
    <ParticipantActions v-if="target" :identifier="target.handle||target.did||target.participantId" :label="target.displayName||target.handle||'Participant'" :actor-key="actor" :tangent="tangent"/>
    <form v-if="target" class="tangent-role-editor" @submit.prevent="submitSettings(saveRole)">
      <div class="tangent-role-person"><ParticipantAvatar :participant="target" /><div><strong>{{ target.displayName || target.handle || target.did || target.participantId }}</strong><span v-if="target.classification?.toLowerCase()==='agent'" class="agent-badge">Agent</span><small>{{ target.handle || target.did || target.participantId }} · Current role: {{ roleLabel(target.isOwner ? 'Owner' : target.role) }}</small></div></div>
      <p v-if="target.isOwner" class="hint">The Tangent owner’s role stays with the owner.</p>
      <template v-else-if="target.canChangeRole && target.allowedRoles.length">
        <label>Role in this Tangent<select v-model="proposedRole" class="input" :disabled="saving"><option v-if="!activeRole(target.role)" value="" disabled>Choose a role</option><option v-for="role in target.allowedRoles" :key="role" :value="role">{{ roleLabel(role) }}</option></select></label>
        <p class="hint">Administrators can tend this Tangent. Members may join conversations. Read only members can follow along.</p>
        <div v-if="!coordinated" class="tangent-savebar"><button class="btn btn-primary" type="submit" :disabled="saving || !proposedRole || proposedRole === activeRole(target.role)">{{ saving ? 'Saving…' : 'Save role' }}</button><button class="btn btn-quiet" type="button" :disabled="saving || proposedRole === activeRole(target.role)" @click="proposedRole = activeRole(target.role)">Discard</button><p role="status">{{ feedback || (proposedRole !== activeRole(target.role) ? 'Unsaved role change' : '') }}</p></div>
      </template>
      <p v-else class="hint">You cannot change this person’s role.</p>
    </form>
  </section>
</template>
