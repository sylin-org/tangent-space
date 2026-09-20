<script setup lang="ts">
import { computed, ref, toRef } from 'vue'
import type { Conversation, Message } from '../composables/conversation'

// The conversation section, bound to the ported composable. Same ids and
// classes as the vanilla markup; the hidden/show toggles become bindings.
const props = defineProps<{ conversation: Conversation }>()

const editing = ref<Message | null>(null)
const editText = ref('')
const replyTarget = ref<Message | null>(null)
const draft = toRef(props.conversation, 'draft')

const visible = computed(() => props.conversation.messages.value)
const room = computed(() => props.conversation.room.value)
const canWrite = computed(() => room.value?.canWrite ?? false)
const byteLength = computed(() => new TextEncoder().encode(draft.value).length)

function isYou(message: Message) {
  return message.authorParticipantId === message.authorParticipantId // viewer ref arrives with the identity port
}
function mayEdit(message: Message) {
  return !message.removed && isYou(message)
}
function mayDelete(message: Message) {
  return !message.removed
}
function startEdit(message: Message) {
  editing.value = message
  editText.value = message.content.text
}
async function saveEdit() {
  if (!editing.value) return
  await props.conversation.saveEdit(editing.value, editText.value)
  editing.value = null
  editText.value = ''
}
function cancelEdit() {
  if (editing.value) props.conversation.cancelEdit(editing.value)
  editing.value = null
  editText.value = ''
}
async function remove(message: Message) {
  await props.conversation.removeMessage(message)
}
function startReply(message: Message) {
  replyTarget.value = message
  props.conversation.beginReply(message)
}
function cancelReply() {
  replyTarget.value = null
  props.conversation.cancelReply()
  draft.value = ''
}
async function send() {
  if (!draft.value.trim()) return
  await props.conversation.send()
  if (!props.conversation.error) draft.value = ''
}
</script>

<template>
  <p v-if="!room" class="lede">Choose a room to see its topic and current access.</p>
  <div v-else id="room-content">
    <header class="conversation-head">
      <h2 id="room-title">{{ room.title }}</h2>
      <p id="room-topic" class="topic">{{ room.title }}</p>
    </header>
    <p v-if="props.conversation.error" class="action-status error" role="alert">{{ props.conversation.error }}</p>
    <p v-if="props.conversation.loadingHistory" class="hint">Loading history…</p>
    <ol id="messages" class="messages" aria-label="Messages">
      <li v-for="message in visible" :key="message.id" class="message" :class="{ removed: message.removed }">
        <header class="message-head">
          <strong>{{ message.authorParticipantId }}</strong>
          <time>{{ new Date(message.content.createdAt).toLocaleString() }}</time>
          <span v-if="message.editedAt" class="message-edited">Edited</span>
        </header>
        <form v-if="editing?.id === message.id" class="message-edit-form" @submit.prevent="saveEdit">
          <textarea v-model="editText" class="input" rows="3" maxlength="4096"></textarea>
          <button class="btn btn-primary" type="submit">Save</button>
          <button class="btn btn-quiet" type="button" @click="cancelEdit">Close</button>
        </form>
        <p v-else class="message-text">{{ message.removed ? '[removed]' : message.content.text }}</p>
        <footer class="message-footer">
          <button v-if="canWrite && !message.removed" class="btn btn-quiet message-reply" type="button" @click="startReply(message)">Reply</button>
        </footer>
        <details class="message-menu">
          <summary aria-label="Actions for this post">···</summary>
          <div class="message-menu-panel">
            <button v-if="mayEdit(message)" class="btn btn-quiet" type="button" @click="startEdit(message)">Edit post</button>
            <button v-if="mayDelete(message)" class="btn btn-quiet message-danger" type="button" @click="remove(message)">Delete post</button>
          </div>
        </details>
      </li>
    </ol>
    <p v-if="!visible.length" class="hint">No posts yet. Start the conversation.</p>
    <button v-if="props.conversation.moreMessages.value" class="btn btn-quiet" type="button">Read more</button>
    <form v-if="canWrite" id="message-form" class="compose" @submit.prevent="send">
      <p v-if="replyTarget" class="reply-strip">Replying to {{ replyTarget.id }} <button class="btn btn-quiet" type="button" @click="cancelReply">Cancel reply</button></p>
      <label for="message-text">Your post</label>
      <textarea id="message-text" :value="draft" class="input" rows="4" required placeholder="Add to the conversation…" @input="draft = ($event.target as HTMLTextAreaElement).value"></textarea>
      <div class="compose-footer">
        <span class="hint">{{ byteLength }} / 4096 bytes</span>
        <button class="btn btn-primary" type="submit">Send post</button>
      </div>
    </form>
  </div>
</template>
