<script setup lang="ts">
import { computed, ref } from 'vue'
import { useSite } from '../stores/site'
import TopicDetailsPanel from './TopicDetailsPanel.vue'
import type { Conversation, Message } from '../composables/conversation'
import type { Moderation } from '../composables/moderation'

const props = defineProps<{ conversation: Conversation; moderation: Moderation }>()
const site = useSite()

const {
  room, messages, draft, error, loadingHistory, moreMessages,
  send, beginReply, cancelReply, saveEdit, cancelEdit, removeMessage,
} = props.conversation

const editing = ref<Message | null>(null)
const editText = ref('')
const replyTarget = ref<Message | null>(null)

const canWrite = computed(() => room.value?.canWrite ?? false)
const byteLength = computed(() => new TextEncoder().encode(draft.value).length)

function mayEdit(message: Message) {
  const viewer = site.site?.participant?.participantRef ?? ''
  return !message.removed && message.authorParticipantId === viewer
}
function mayDelete(message: Message) {
  return !message.removed
}
function startEdit(message: Message) {
  editing.value = message
  editText.value = message.content.text
}
async function submitEdit() {
  if (!editing.value) return
  await saveEdit(editing.value, editText.value)
  editing.value = null
  editText.value = ''
}
function closeEdit() {
  if (editing.value) cancelEdit(editing.value)
  editing.value = null
  editText.value = ''
}
async function deleteMessage(message: Message) {
  await removeMessage(message)
}
function startReply(message: Message) {
  replyTarget.value = message
  beginReply(message)
}
function closeReply() {
  replyTarget.value = null
  cancelReply()
  draft.value = ''
}
async function submitPost() {
  if (!draft.value.trim()) return
  await send()
  if (!error.value) {
    draft.value = ''
    replyTarget.value = null
  }
}
</script>

<template>
  <p v-if="!room" class="lede">Choose a room to see its topic and current access.</p>
  <div v-else id="room-content">
    <div class="conversation-toolbar">
      <p id="conversation-connection" class="connection-state" role="status">Connecting…</p>
      <div class="conversation-toolbar-actions">
        <button
          id="stewardship-open"
          class="btn btn-quiet"
          type="button"
          aria-controls="stewardship-panel"
          :aria-expanded="moderation.panelOpen.value"
          :hidden="!moderation.stewardshipAvailable.value"
          @click="moderation.panelOpen.value ? moderation.closePanel() : moderation.openPanel()"
        >Stewardship</button>
      </div>
    </div>
    <header class="conversation-head"><p id="room-admission" class="eyebrow">{{ room.readAudience === 'Public' ? 'Publicly readable · signed-in participation' : 'Signed-in participants' }}</p>
      <h2 id="room-title">{{ room.title }}</h2>
    </header>
    <p v-if="error" class="action-status error" role="alert">{{ error }}</p>
    <p v-if="loadingHistory" class="hint">Loading history…</p>
    <TopicDetailsPanel :conversation="conversation" />
    <ol class="messages" aria-label="Messages">
      <li v-for="message in messages" :key="message.id" class="message" :class="{ removed: message.removed }">
        <header class="message-head">
          <strong>{{ message.authorParticipantId }}</strong>
          <time>{{ new Date(message.content.createdAt).toLocaleString() }}</time>
          <span v-if="message.editedAt" class="message-edited">Edited</span>
        </header>
        <form v-if="editing?.id === message.id" class="message-edit-form" @submit.prevent="submitEdit">
          <textarea v-model="editText" class="input" rows="3" maxlength="4096"></textarea>
          <button class="btn btn-primary" type="submit">Save</button>
          <button class="btn btn-quiet" type="button" @click="closeEdit">Close</button>
        </form>
        <p v-else class="message-text">{{ message.removed ? '[removed]' : message.content.text }}</p>
        <footer class="message-footer">
          <button v-if="canWrite && !message.removed" class="btn btn-quiet message-reply" type="button" @click="startReply(message)">Reply</button>
        </footer>
        <details class="message-menu">
          <summary aria-label="Actions for this post">···</summary>
          <div class="message-menu-panel">
            <button v-if="!message.removed && moderation.allowed(message, 'reportPost')" class="btn btn-quiet" type="button" @click="moderation.openReport(message.id, message.authorParticipantId)">Report post</button>
            <button v-if="mayEdit(message)" class="btn btn-quiet" type="button" @click="startEdit(message)">Edit post</button>
            <button v-if="mayDelete(message)" class="btn btn-quiet message-danger" type="button" @click="deleteMessage(message)">Delete post</button>
          </div>
        </details>
      </li>
    </ol>
    <p v-if="!messages.length" class="hint">No posts yet. Start the conversation.</p>
    <button v-if="moreMessages" class="btn btn-quiet" type="button">Read more</button>
    <form v-if="canWrite" class="compose" @submit.prevent="submitPost">
      <p v-if="replyTarget" class="reply-strip">Replying <button class="btn btn-quiet" type="button" @click="closeReply">Cancel reply</button></p>
      <label for="message-text">Your post</label>
      <textarea id="message-text" v-model="draft" class="input" rows="4" required placeholder="Add to the conversation…"></textarea>
      <div class="compose-footer">
        <span class="hint">{{ byteLength }} / 4096 bytes</span>
        <button class="btn btn-primary" type="submit">Send post</button>
      </div>
    </form>
  </div>
</template>
