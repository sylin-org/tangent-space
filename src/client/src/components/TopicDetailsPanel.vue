<script setup lang="ts">
// The Topic's details and settings, ported from rooms.js renderRoom + the
// topic forms: the access note, and for those who manage it — the description,
// the reading audience, participation, and people.
import { computed, ref, watch } from 'vue'
import type { Conversation } from '../composables/conversation'

const props = defineProps<{ conversation: Conversation }>()

const {
  room, status, saveTopicDescription, updateMember, saveReadAudience, saveParticipation,
} = props.conversation

const formError = ref('')
async function run(work: () => Promise<void>) {
  formError.value = ''
  try {
    await work()
  } catch (error) {
    formError.value = (error as Error).message
  }
}

const can = (action: string): boolean => room.value?.permissions?.allowedActions?.includes(action) ?? false

const adminVisible = computed(() => !!room.value && (room.value.canManage === true || can('manageTopic')))
const appointsManagers = computed(() => room.value?.canAppointManagers === true)

const accessNote = computed(() => {
  const notes: Record<string, string> = {
    'sign-in-required': 'Sign in to enter this room.',
    'not-admitted': 'The current rules do not admit you.',
    removed: 'Your access to this room has been removed.',
    suspended: 'Your participation at this site is suspended.',
  }
  const r = room.value
  if (!r) return ''
  return notes[r.accessState ?? ''] || (r.canWrite ? 'You can read and take part.' : r.canRead ? 'You can read this room.' : 'Content is not available under your current access.')
})

const permissionsNote = computed(() => {
  const r = room.value
  if (!r) return ''
  const labels: Record<string, string> = {
    read: 'read', reply: 'reply', manageServer: 'manage this server', createTangent: 'create Tangents',
    manageTangent: 'manage this Tangent', createTopic: 'create Topics', manageTopic: 'manage this Topic',
    manageParticipants: 'manage participants', editOwnPost: 'edit your posts', deleteOwnPost: 'delete your posts',
    removePost: 'remove posts', reportPost: 'report posts',
  }
  const summary = r.permissions
    ? 'Your role: ' + (r.permissions.role ?? '') + '. You can ' + (r.permissions.allowedActions ?? []).map(a => labels[a] || a).join(', ') + '.'
    : ''
  const tail = r.isLocked ? ' This Topic is locked.' : r.allowPostEditing ? ' Authors may edit their posts.' : ' Posts are write-once; corrections are new replies.'
  return summary + tail
})

// --- Description form ---
const topicText = ref('')
watch(room, r => {
  topicText.value = r?.topic || ''
}, { immediate: true })

// --- Reading audience ---
const audience = ref('Restricted')
const confirmPublish = ref(false)
const audienceNote = ref('')
watch(room, r => {
  audience.value = r?.readAudience || 'Restricted'
  confirmPublish.value = false
  updateAudienceNote()
}, { immediate: true })

const publishing = computed(() => appointsManagers.value && room.value?.readAudience !== 'Public' && audience.value === 'Public')

function updateAudienceNote() {
  const current = room.value?.readAudience
  audienceNote.value =
    audience.value === 'Public'
      ? current === 'Public'
        ? 'Anyone with the link can read without signing in. Restricting this Topic later cannot erase copies already made.'
        : 'Saving will make this Topic and its existing posts readable without signing in. Confirm below before publishing.'
      : current === 'Public'
        ? 'Saving will restrict reading to admitted participants. Copies already downloaded or archived cannot be recalled.'
        : 'Only admitted participants can read. This Topic is not available to anonymous visitors.'
}

function onAudienceChange() {
  if (!publishing.value) confirmPublish.value = false
  updateAudienceNote()
}

function submitReadAudience() {
  return run(async () => {
    if (publishing.value && !confirmPublish.value) {
      throw new Error('Confirm that existing posts will become public before saving.')
    }
    await saveReadAudience(audience.value, publishing.value && confirmPublish.value)
  })
}

// --- Participation ---
const allowPostEditing = ref(false)
const isLocked = ref(false)
watch(room, r => {
  allowPostEditing.value = r?.allowPostEditing === true
  isLocked.value = r?.isLocked === true
}, { immediate: true })

// --- People ---
const memberDid = ref('')
const memberRole = ref('Member')
const roleNote = ref('')
watch(memberRole, updateRoleNote, { immediate: true })

function updateRoleNote() {
  const notes: Record<string, string> = {
    Member: 'Can take part, subject to Tangent access and Topic locks.',
    Reader: 'Can read without posting, subject to Tangent access.',
    Manager: 'Can manage this Topic and its participants. Owner-only powers, including publication and appointing managers, are not included.',
    Removed: 'Removes signed-in access to this Topic. Public posts remain readable without signing in.',
  }
  roleNote.value = notes[memberRole.value] || notes.Member!
}

watch(appointsManagers, allowed => {
  if (!allowed && memberRole.value === 'Manager') memberRole.value = 'Member'
})

const managerChoiceHidden = computed(() => !appointsManagers.value)

function submitMember() {
  return run(async () => {
    if (!memberDid.value.trim()) return
    await updateMember(memberDid.value, memberRole.value)
    memberDid.value = ''
  })
}
</script>

<template>
  <details id="topic-details" class="topic-details" :open="!!room && !room.canRead">
    <summary>Topic details</summary>
    <div class="topic-details-body">
      <p id="room-access" class="note">{{ accessNote }}</p>
      <p id="topic-permissions" class="hint">{{ permissionsNote }}</p>
      <details v-if="room" id="room-admin" class="setup" :hidden="!adminVisible">
        <summary>Topic settings</summary>
        <section class="topic-settings-section" aria-labelledby="topic-about-heading">
          <header><h3 id="topic-about-heading">About</h3><p class="hint">Set the scene for this conversation.</p></header>
          <form id="topic-form" class="stack" @submit.prevent="run(() => saveTopicDescription(topicText))">
            <label>Description<textarea v-model="topicText" name="topic" class="input" maxlength="2000" rows="3"></textarea></label>
            <button class="btn btn-quiet">Save description</button>
          </form>
        </section>
        <section class="topic-settings-section" aria-labelledby="topic-access-heading">
          <header><h3 id="topic-access-heading">Access</h3><p class="hint">Reading and taking part are separate choices.</p></header>
          <div class="topic-settings-controls">
            <div v-if="appointsManagers" id="topic-access-settings">
              <form id="read-audience-form" class="stack" @submit.prevent="submitReadAudience">
                <label>Who can read
                  <select v-model="audience" name="audience" class="input" aria-describedby="read-audience-note" @change="onAudienceChange">
                    <option value="Restricted">Only admitted participants</option>
                    <option value="Public">Anyone on the web</option>
                  </select>
                </label>
                <p id="read-audience-note" class="hint" aria-live="polite">{{ audienceNote }}</p>
                <label v-if="publishing" id="publish-history-confirmation" class="check-row">
                  <input v-model="confirmPublish" type="checkbox" :required="publishing"> I understand that existing posts will become public and copies may persist
                </label>
                <button class="btn btn-quiet">Save reading audience</button>
              </form>
              <form id="topic-settings-form" class="stack" @submit.prevent="run(() => saveParticipation(allowPostEditing, isLocked))">
                <label class="check-row"><input v-model="allowPostEditing" type="checkbox" name="allowPostEditing"> Allow authors to edit their posts</label>
                <label class="check-row"><input v-model="isLocked" type="checkbox" name="isLocked"> Lock this topic</label>
                <button class="btn btn-quiet">Save participation settings</button>
              </form>
            </div>
          </div>
        </section>
        <section class="topic-settings-section" aria-labelledby="topic-people-heading">
          <header><h3 id="topic-people-heading">People</h3><p class="hint">Give a participant a role in this Topic.</p></header>
          <form id="member-form" class="stack" @submit.prevent="submitMember">
            <label>Participant DID<input v-model="memberDid" class="input" name="did" required maxlength="2048" autocomplete="off" spellcheck="false"></label>
            <label>Topic role
              <select v-model="memberRole" class="input" name="role" aria-describedby="topic-role-note">
                <option value="Member">Member</option>
                <option value="Reader">Read only</option>
                <option value="Removed">Remove access</option>
                <option id="manager-choice" value="Manager" :hidden="managerChoiceHidden" :disabled="managerChoiceHidden">Manager</option>
              </select>
            </label>
            <p id="topic-role-note" class="hint" aria-live="polite">{{ roleNote }}</p>
            <button class="btn btn-quiet">Update membership</button>
          </form>
        </section>
      </details>
      <p id="history-note" class="hint" aria-live="polite">{{ status }}</p>
      <p v-if="formError" class="hint error" role="alert">{{ formError }}</p>
    </div>
  </details>
</template>
