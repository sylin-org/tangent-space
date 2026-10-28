<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import type { Settings } from '../composables/settings'
import { createTopicSettings } from '../composables/topic-settings'
import { registerSettingsChange, useSettingsSubmit } from '../composables/settings-changes'
import { rolesRequest } from '../composables/roles-api'
import { mentionables, type MentionTarget } from '../composables/facets'
import AccessPanel from './AccessPanel.vue'
import SettingsWorkspaceHeader from './SettingsWorkspaceHeader.vue'
import SettingsSectionTabs from './SettingsSectionTabs.vue'

const submitSettings = useSettingsSubmit()
const props = defineProps<{ settings: Settings; compact?: boolean }>()
const emit = defineEmits<{ return: [] }>()
const requestedSection = new URLSearchParams(window.location.search).get('section')
const activeSection = ref<'about' | 'people' | 'access'>(requestedSection === 'people' || requestedSection === 'access' ? requestedSection : 'about')
watch(() => props.settings.activeTab?.value, value => { if (!props.compact) activeSection.value = value === 'people' ? 'people' : value === 'access' ? 'access' : 'about' }, { immediate: true })
const sections = [
  { id: 'about', label: 'About' },
  { id: 'people', label: 'People' },
  { id: 'access', label: 'Access' },
]
function selectSection(id: string) {
  if (id === 'about' || id === 'people' || id === 'access') {
    activeSection.value = id
    if (props.compact) {
      const url = new URL(window.location.href)
      if (id === 'about') url.searchParams.delete('section')
      else url.searchParams.set('section', id)
      window.history.replaceState(window.history.state, '', url.pathname + url.search + url.hash)
    }
  }
}
const form = createTopicSettings(props.settings.resource, props.settings.actorKey)
const coordinated = registerSettingsChange({ label: 'Topic details', count: () => Number(form.details.title !== form.saved.title) + Number(form.details.topic !== form.saved.topic), busy: () => form.state.details === 'saving', save: form.saveDetails, discard: () => form.discard('details'), status: () => form.errors.details })
registerSettingsChange({ label: 'Reading audience', count: () => Number(form.dirty.audience.value), busy: () => form.state.audience === 'saving', save: saveAudience, discard: () => form.discard('audience'), status: () => form.errors.audience })
registerSettingsChange({ label: 'Participation', count: () => Number(form.participation.allowPostEditing !== form.saved.allowPostEditing) + Number(form.participation.isLocked !== form.saved.isLocked), busy: () => form.state.participation === 'saving', save: saveParticipation, discard: () => form.discard('participation'), status: () => form.errors.participation })
const topic = computed(() => props.settings.resource.value?.topic)
interface AccessSummary { selected?: Record<string, string[] | null>; effective?: Record<string, string[]>; parent?: Record<string, string[]> }
interface PersonPreview { participantId: string; label: string; role: string; canRead: boolean; canReply: boolean; canManage: boolean; obstacle: string | null }
const effective = ref<AccessSummary | null>(null)
const effectiveError = ref('')
const publicReading = ref(false)
const preview = ref<PersonPreview | null>(null)
const previewError = ref('')
const previewLoading = ref(false)
const lastOutcome = ref('')
const candidates = ref<MentionTarget[]>([])
const searching = ref(false)
const showCandidates = ref(false)
const personQuery = ref('')
let previewVersion = 0
let candidateVersion = 0
let effectiveVersion = 0
let previewTimer: ReturnType<typeof setTimeout> | undefined
const accessPath = computed(() => topic.value
  ? '/api/v1/tangents/' + encodeURIComponent(topic.value.tangentKey) + '/topics/' + encodeURIComponent(topic.value.key) : '')
async function loadEffective() {
  if (!accessPath.value) return
  const version = ++effectiveVersion
  try {
    const [access, described] = await Promise.all([
      rolesRequest<AccessSummary>(accessPath.value + '/access', { scope: 'access' }),
      rolesRequest<{ publiclyReadable?: boolean }>(accessPath.value, { scope: 'access' }),
    ])
    if (version !== effectiveVersion) return
    effective.value = access
    publicReading.value = described.publiclyReadable === true
    effectiveError.value = ''
  }
  catch (error) { if (version === effectiveVersion) effectiveError.value = (error as Error).message }
}
watch(accessPath, () => { effectiveVersion++; effective.value = null; void loadEffective() }, { immediate: true })
watch(() => form.member.identifier, identifier => {
  if (identifier && !personQuery.value.trim()) personQuery.value = identifier
})
onUnmounted(() => {
  effectiveVersion++
  previewVersion++
  candidateVersion++
  if (previewTimer) clearTimeout(previewTimer)
})
watch(() => [form.member.identifier, form.member.role, accessPath.value], () => {
  if (previewTimer) clearTimeout(previewTimer)
  previewVersion++
  preview.value = null
  previewError.value = ''
  previewLoading.value = false
  if (form.member.identifier.trim()) previewTimer = setTimeout(() => { void loadPreview() }, 250)
})
watch(personQuery, async identifier => {
  lastOutcome.value = ''
  const version = ++candidateVersion
  candidates.value = []
  searching.value = false
  if (identifier.trim().replace(/^@/, '').length < 2 || !topic.value) return
  searching.value = true
  try {
    const results = await mentionables(topic.value.key, identifier.trim())
    if (version === candidateVersion) candidates.value = results.filter(item => item.kind === 'participant')
  } catch { if (version === candidateVersion) candidates.value = [] }
  finally { if (version === candidateVersion) searching.value = false }
})
function selectCandidate(person: MentionTarget) {
  personQuery.value = person.label
  form.member.identifier = person.did || person.value || person.insert
  showCandidates.value = false
}
function onPersonInput() {
  // Search text is not a person. Only a selected result or explicit exact lookup may be saved.
  form.member.identifier = ''
  showCandidates.value = true
}
const exactLookupAvailable = computed(() => {
  const value = personQuery.value.trim()
  return value.startsWith('did:') || value.startsWith('@') || value.includes('.')
})
function selectExactIdentifier() {
  form.member.identifier = personQuery.value.trim().replace(/^@/, '')
  showCandidates.value = false
}
registerSettingsChange({ label: 'Topic participant', count: () => form.member.identifier.trim() ? 1 : 0, busy: () => form.state.member === 'saving', save: savePerson, discard: discardPerson, status: () => form.errors.member })
function discardPerson() {
  form.discard('member')
  personQuery.value = ''
  candidates.value = []
  showCandidates.value = false
}
function groups(action: string): string {
  const tokens = effective.value?.effective?.[action] ?? []
  const labels = tokens.filter(token => !token.startsWith('global:')).map(token =>
    token === 'everyone' ? 'anyone' : token === 'authenticated' ? 'signed-in people' : '@' + token.replace(/^(role|group):/, ''))
  return labels.length ? labels.join(', ') : 'Topic and Host stewards only'
}
const inherits = computed(() => effective.value?.selected && ['see', 'post', 'manage'].every(action => effective.value!.selected?.[action] == null))
const readOutcome = computed(() => publicReading.value ? 'Anyone on the web, without signing in'
  : form.saved.readAudience === 'Public' ? 'Only admitted people; a rule blocks anonymous reading'
    : (effective.value?.effective?.see ?? []).includes('everyone') ? 'All admitted people' : 'Selected admitted people')
const replyOutcome = computed(() => form.saved.isLocked ? 'No new replies while this conversation is locked'
  : (effective.value?.effective?.post ?? []).includes('everyone') ? 'Admitted readers who can post in this Tangent'
    : 'Selected admitted readers who can post in this Tangent')
function outcome(result: PersonPreview): string {
  const abilities = [result.canRead ? 'read' : null, result.canReply ? 'reply' : null,
    result.canManage ? 'manage' : null].filter(Boolean)
  return abilities.length ? 'can ' + abilities.join(', ') + ' in this conversation.' : 'cannot read, reply, or manage this conversation.'
}
const personOutcome = computed(() => {
  const result = preview.value
  if (!result) return ''
  return 'This person ' + outcome(result) + (result.obstacle ? ' ' + result.obstacle : '')
})
function refreshPolicy() {
  void loadEffective()
  previewVersion++
  preview.value = null
  previewError.value = ''
  previewLoading.value = false
  if (form.member.identifier.trim()) void loadPreview()
}
async function saveAudience() {
  await form.saveAudience()
  if (form.state.audience === 'saved') refreshPolicy()
}
async function saveParticipation() {
  await form.saveParticipation()
  if (form.state.participation === 'saved') refreshPolicy()
}
async function loadPreview() {
  const version = ++previewVersion
  const identifier = form.member.identifier.trim()
  if (!identifier || !accessPath.value) return
  previewLoading.value = true
  try {
    const result = await rolesRequest<PersonPreview>(accessPath.value + '/membership-preview?identifier=' + encodeURIComponent(identifier) + '&role=' + encodeURIComponent(form.member.role), { scope: 'access' })
    if (version === previewVersion) preview.value = result
  } catch (error) { if (version === previewVersion) previewError.value = (error as Error).message }
  finally { if (version === previewVersion) previewLoading.value = false }
}
async function savePerson() {
  if (!preview.value || previewLoading.value || previewError.value) return
  const identifier = form.member.identifier.trim()
  const role = form.member.role
  await form.saveMember()
  if (form.state.member !== 'saved') return
  try {
    const result = await rolesRequest<PersonPreview>(accessPath.value + '/membership-preview?identifier=' + encodeURIComponent(identifier) + '&role=' + encodeURIComponent(role), { scope: 'access' })
    lastOutcome.value = identifier + ' ' + outcome(result) + (result.obstacle ? ' ' + result.obstacle : '')
  } catch { lastOutcome.value = identifier + ': membership saved. Reopen this person to check current access.' }
}
const tangentName = computed(() => props.settings.resource.value?.tangent?.name || topic.value?.tangentKey || 'Tangent')
const draftPublicReading = computed(() => form.audience.value === 'Public' && (effective.value?.effective?.see ?? []).includes('everyone'))
const audienceNote = computed(() => draftPublicReading.value
  ? form.publishing.value
    ? 'Saving Public will make this Topic and its existing posts readable without signing in.'
    : 'Anyone can read this Topic without signing in. Copies already made may persist.'
  : form.audience.value === 'Public'
    ? 'Public is selected, but a reading rule still blocks anonymous visitors. Review the access rules before sharing this Topic as public.'
  : form.saved.readAudience === 'Public'
    ? 'Saving Restricted will limit future reading to admitted participants. Existing public copies cannot be recalled.'
    : 'Only admitted participants can read this Topic.')
const roleNote = computed(() => ({
  Member: 'Invite to this conversation, within the Tangent’s admission and access rules.',
  Reader: 'Invite to read here without replying, within the Tangent’s rules.',
  Manager: 'Ask this person to look after this conversation, within the Tangent’s rules.',
  Removed: 'Remove this account’s Topic access. Public Posts remain readable to visitors.',
})[form.member.role] || '')
</script>

<template>
  <div v-if="topic" class="topic-settings-page" :class="{ 'topic-settings-compact': compact }">
    <header v-if="compact" class="topic-settings-rail-heading"><div><p class="eyebrow">{{ tangentName }}</p><h2 id="article-settings-title">{{ topic.presentation === 'article' ? 'Article Settings' : 'Topic Settings' }}</h2><p>{{ form.saved.title }}</p></div><button id="article-settings-close" class="btn btn-quiet" type="button" @click="emit('return')">Close</button></header>
    <SettingsWorkspaceHeader v-else :eyebrow="tangentName + ' · ' + (topic.presentation === 'article' ? 'ARTICLE SETTINGS' : 'TOPIC SETTINGS')" :name="form.saved.title" :description="form.saved.topic" back-label="conversation" @back="emit('return')" />
    <SettingsSectionTabs :tabs="sections" :active="activeSection" tab-prefix="topic-settings" :label="topic.presentation === 'article' ? 'Article settings' : 'Topic settings'" @select="selectSection" />
    <p v-if="form.hasDirty.value" class="topic-settings-draft-note" role="status">{{ form.storageAvailable.value ? 'Unsaved edits are kept in this browser tab when you leave or reload.' : 'Save or discard your edits before leaving this page.' }}</p>

    <section id="topic-settings-access-panel" v-show="activeSection === 'access'" class="topic-settings-card topic-outcomes" role="tabpanel" aria-labelledby="topic-settings-access-tab">
      <header><p class="eyebrow">CURRENT OUTCOME</p><h2 id="topic-outcomes-title">Who can take part here?</h2><p>{{ inherits ? 'This conversation follows its Tangent’s access rules.' : 'This conversation has at least one local access rule. Tangent and Host limits still apply.' }}</p></header>
      <p v-if="effectiveError" class="error" role="alert">{{ effectiveError }} <button class="btn btn-quiet" type="button" @click="loadEffective">Try again</button></p>
      <p v-else-if="!effective" class="hint">Checking effective access…</p>
      <dl v-else class="topic-outcome-grid">
        <div><dt>Who can read</dt><dd>{{ readOutcome }}<small v-if="form.saved.readAudience === 'Public' && !publicReading">Public reading is selected, but an inherited or local rule currently prevents anonymous reading. Review Local access rules below.</small><small v-else-if="!publicReading">Current groups: {{ groups('see') }}</small></dd></div>
        <div><dt>Who can reply</dt><dd>{{ replyOutcome }}<small v-if="!form.saved.isLocked">Current groups: {{ groups('post') }}</small></dd></div>
        <div><dt>Who looks after this conversation</dt><dd>Authorized Topic, Tangent, and Host stewards<small>Current groups: {{ groups('manage') }}</small></dd></div>
      </dl>
      <p class="hint">A person’s suspension, classification, membership, and inherited rules can narrow these groups. Preview a person in People before changing their access.</p>
    </section>

    <section id="topic-settings-people-panel" v-show="activeSection === 'people'" class="topic-settings-card" role="tabpanel" aria-labelledby="topic-settings-people-tab">
      <header><p class="eyebrow">01 · PEOPLE</p><h2 id="topic-settings-stewardship-title">People &amp; roles</h2><p>Choose a person and preview what they will actually be able to do here.</p></header>
      <form class="topic-settings-form topic-settings-subform" @submit.prevent="submitSettings(savePerson)">
        <h3>Change one person’s participation</h3>
        <p class="hint">Choose someone who has already arrived on this Host. Tangent admission and inherited rules still apply.</p>
        <label>Find a person<input v-model="personQuery" class="input" name="identifier" required maxlength="253" autocomplete="off" spellcheck="false" placeholder="Name, @handle, or DID" @focus="showCandidates = true" @input="onPersonInput"></label>
        <div v-if="showCandidates && (searching || candidates.length)" class="topic-person-candidates" aria-label="Matching people">
          <p v-if="searching" class="hint">Finding people…</p>
          <button v-for="person in candidates" :key="person.did || person.value || person.insert" type="button" class="topic-person-candidate" @click="selectCandidate(person)"><strong>{{ person.label }}</strong><span>{{ person.classification || 'Participant' }}{{ person.badge ? ' · ' + person.badge : '' }}</span></button>
        </div>
        <div v-else-if="showCandidates && personQuery.trim().length > 2 && !searching" class="topic-person-no-results"><p class="hint">No nearby person matched.</p><button v-if="exactLookupAvailable" type="button" class="btn btn-quiet" @click="selectExactIdentifier">Look up this exact handle or DID</button></div>
        <label>What should this person do?<select v-model="form.member.role" class="input" name="role" aria-describedby="topic-member-role-note"><option value="Member">Read and reply</option><option value="Reader">Read only</option><option value="Manager">Look after this conversation</option><option value="Removed">Remove access</option></select></label>
        <p id="topic-member-role-note" class="hint">{{ roleNote }}</p>
        <p v-if="previewLoading" class="hint" role="status">Checking this person’s effective access…</p>
        <p v-else-if="previewError" class="note error" role="alert">{{ previewError }}</p>
        <p v-else-if="preview" class="note" role="status"><strong>{{ preview.label }}</strong><br><strong>After this change:</strong> {{ personOutcome }}</p>
        <div v-if="!coordinated" class="topic-settings-actions">
          <button class="btn btn-primary" :disabled="!preview || previewLoading || form.state.member === 'saving'">{{ form.state.member === 'saving' ? 'Saving…' : 'Apply to this person' }}</button>
          <button class="btn btn-quiet" type="button" :disabled="!form.dirty.member.value || form.state.member === 'saving'" @click="discardPerson">Discard</button>
          <p role="status" :class="{ error: form.state.member === 'error' }">{{ form.errors.member || lastOutcome || (form.dirty.member.value ? 'Unsaved changes' : '') }}</p>
        </div>
      </form>
    </section>

    <section id="topic-settings-about-panel" v-show="activeSection === 'about'" class="topic-settings-card" role="tabpanel" aria-labelledby="topic-settings-about-tab">
      <header><p class="eyebrow">02 · DETAILS</p><h2 id="topic-settings-details-title">Details</h2><p>Give this conversation a clear name and description.</p></header>
      <form class="topic-settings-form" @submit.prevent="submitSettings(form.saveDetails)">
        <label>{{ topic.presentation === 'article' ? 'Article title' : 'Topic title' }}<input v-model="form.details.title" class="input" name="title" maxlength="120" required></label>
        <label>{{ topic.presentation === 'article' ? 'Article description' : 'Topic description' }}<textarea v-model="form.details.topic" class="input" name="topic" maxlength="2000" rows="4"></textarea></label>
        <div v-if="!coordinated" class="topic-settings-actions">
          <button class="btn btn-primary" :disabled="!form.dirty.details.value || form.state.details === 'saving'">{{ form.state.details === 'saving' ? 'Saving…' : 'Save details' }}</button>
          <button class="btn btn-quiet" type="button" :disabled="!form.dirty.details.value || form.state.details === 'saving'" @click="form.discard('details')">Discard</button>
          <p role="status" :class="{ error: form.state.details === 'error' }">{{ form.errors.details || (form.state.details === 'saved' && !form.dirty.details.value ? 'Details saved.' : form.dirty.details.value ? 'Unsaved changes' : '') }}</p>
        </div>
      </form>
    </section>

    <section v-show="activeSection === 'access'" class="topic-settings-card" aria-labelledby="topic-settings-reading-title">
      <header><p class="eyebrow">03 · CONVERSATION</p><h2 id="topic-settings-reading-title">Reading &amp; participation</h2><p>Set the audience separately from who may post or manage.</p></header>
      <form class="topic-settings-form topic-settings-subform" @submit.prevent="submitSettings(saveAudience)">
        <fieldset>
          <legend>Who can read</legend>
          <label class="topic-settings-radio"><input v-model="form.audience.value" type="radio" name="readAudience" value="Restricted"> <span><strong>Restricted</strong><small>Only admitted participants</small></span></label>
          <label class="topic-settings-radio"><input v-model="form.audience.value" type="radio" name="readAudience" value="Public"> <span><strong>Public</strong><small>Anonymous reading when access rules allow it</small></span></label>
        </fieldset>
        <p class="hint" aria-live="polite">{{ audienceNote }}</p>
        <label v-if="form.publishing.value" class="check-row"><input v-model="form.publishHistory.value" type="checkbox"> I understand that existing posts will become public and copies may persist.</label>
        <div v-if="!coordinated" class="topic-settings-actions">
          <button class="btn btn-primary" :disabled="!form.dirty.audience.value || form.state.audience === 'saving'">{{ form.state.audience === 'saving' ? 'Saving…' : 'Save reading audience' }}</button>
          <button class="btn btn-quiet" type="button" :disabled="!form.dirty.audience.value || form.state.audience === 'saving'" @click="form.discard('audience')">Discard</button>
          <p role="status" :class="{ error: form.state.audience === 'error' }">{{ form.errors.audience || (form.state.audience === 'saved' && !form.dirty.audience.value ? 'Reading audience saved.' : form.dirty.audience.value ? 'Unsaved changes' : '') }}</p>
        </div>
      </form>
      <form class="topic-settings-form topic-settings-subform" @submit.prevent="submitSettings(saveParticipation)">
        <h3>Participation</h3>
        <label class="check-row"><input v-model="form.participation.allowPostEditing" type="checkbox"> Allow authors to edit their Posts</label>
        <label class="check-row"><input v-model="form.participation.isLocked" type="checkbox"> Lock this Topic to new Posts</label>
        <div v-if="!coordinated" class="topic-settings-actions">
          <button class="btn btn-primary" :disabled="!form.dirty.participation.value || form.state.participation === 'saving'">{{ form.state.participation === 'saving' ? 'Saving…' : 'Save participation' }}</button>
          <button class="btn btn-quiet" type="button" :disabled="!form.dirty.participation.value || form.state.participation === 'saving'" @click="form.discard('participation')">Discard</button>
          <p role="status" :class="{ error: form.state.participation === 'error' }">{{ form.errors.participation || (form.state.participation === 'saved' && !form.dirty.participation.value ? 'Participation saved.' : form.dirty.participation.value ? 'Unsaved changes' : '') }}</p>
        </div>
      </form>
    </section>
    <section v-show="activeSection === 'access'" class="topic-settings-card topic-access-rules"><h2>Local access rules</h2><p class="hint">A local Topic rule can narrow its inherited Tangent rule. Public reading is a separate choice above.</p><AccessPanel :settings="settings" @saved="refreshPolicy" /></section>
  </div>
  <div v-else class="topic-settings-page"><SettingsWorkspaceHeader eyebrow="TOPIC SETTINGS" name="Conversation settings" back-label="conversation" @back="emit('return')" /><p v-if="settings.loading.value" class="hint" role="status">Loading conversation settings…</p><p v-else-if="settings.denied.value" class="error" role="status">{{ settings.denied.value }}</p><p v-else class="hint">This conversation is unavailable.</p></div>
</template>
