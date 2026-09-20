<script setup lang="ts">
import { computed, ref } from 'vue'
import { useSite } from '../stores/site'
import TopicDetailsPanel from './TopicDetailsPanel.vue'
import type { Conversation, Message } from '../composables/conversation'
import type { Moderation } from '../composables/moderation'
import type { Activity } from '../composables/activity'
import {
  byteLength as utf8ByteLength, sizeFacets, recordFacet, replyMention, facetedSegments,
  mentionables, type MentionTarget,
} from '../composables/facets'

const props = defineProps<{ conversation: Conversation; moderation: Moderation; activity: Activity }>()
const site = useSite()

const {
  room, messages, draft, error, loadingHistory, moreMessages, resolved,
  send, beginReply, cancelReply, saveEdit, cancelEdit, removeMessage,
} = props.conversation

const editing = ref<Message | null>(null)
const editText = ref('')
const replyTarget = ref<Message | null>(null)

// --- The @-mention composer (facets.js) ---
const mentionOpen = ref(false)
const mentionTargets = ref<MentionTarget[]>([])
const mentionActive = ref(0)
const composerText = computed({
  get: () => draft.value,
  set: (value: string) => {
    draft.value = value
  },
})
let tokenStart = -1
let lookupVersion = 0
const composerField = ref<HTMLTextAreaElement | null>(null)

function onComposerInput() {
  const textarea = composerField.value
  if (!textarea || !room.value) return
  sizeFacets(room.value.key, textarea.value)
  const before = textarea.value.slice(0, textarea.selectionStart ?? 0)
  const match = /(^|[\s(>])@([\w.-]*)$/.exec(before)
  if (!match) {
    closeMention()
    return
  }
  tokenStart = (textarea.selectionStart ?? 0) - match[2]!.length - 1
  void openMention(textarea, match[2]!)
}

async function openMention(textarea: HTMLTextAreaElement, prefix: string) {
  const topicKey = room.value?.key
  if (!topicKey) {
    closeMention()
    return
  }
  const version = ++lookupVersion
  let list: MentionTarget[]
  try {
    list = await mentionables(topicKey, prefix)
  } catch {
    if (version === lookupVersion) closeMention()
    return
  }
  if (version !== lookupVersion || topicKey !== room.value?.key || document.activeElement !== textarea) return
  mentionTargets.value = list ?? []
  if (!mentionTargets.value.length) {
    closeMention()
    return
  }
  mentionOpen.value = true
  mentionActive.value = 0
  positionMention(textarea)
}

function positionMention(textarea: HTMLTextAreaElement): void {
  requestAnimationFrame(() => {
    const node = document.getElementById('mention-popup')
    const form = textarea.closest('form')?.getBoundingClientRect()
    if (!node || !form) return
    const caret = textarea.selectionStart ?? 0
    const mirror = document.createElement('div')
    const style = getComputedStyle(textarea)
    for (const key of ['fontFamily', 'fontSize', 'lineHeight', 'padding', 'width', 'whiteSpace', 'wordBreak'] as const) {
      (mirror.style as unknown as Record<string, string>)[key] = style[key]
    }
    mirror.style.position = 'absolute'
    mirror.style.visibility = 'hidden'
    mirror.textContent = textarea.value.slice(0, caret)
    const marker = document.createElement('span')
    marker.textContent = '​'
    mirror.appendChild(marker)
    document.body.appendChild(mirror)
    const left = Math.min(marker.offsetLeft, form.width - node.offsetWidth - 8)
    document.body.removeChild(mirror)
    node.style.left = Math.max(0, left) + 'px'
    node.style.top = Math.min(textarea.offsetTop + textarea.offsetHeight + 4, form.height - node.offsetHeight - 4) + 'px'
  })
}

function onComposerKeydown(event: KeyboardEvent) {
  if (!mentionOpen.value) return
  const node = document.getElementById('mention-popup')
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    mentionActive.value = (mentionActive.value + (event.key === 'ArrowDown' ? 1 : -1) + mentionTargets.value.length) % mentionTargets.value.length
    node?.querySelectorAll('.mention-row').forEach((row, index) => {
      row.classList.toggle('active', index === mentionActive.value)
      row.setAttribute('aria-selected', String(index === mentionActive.value))
    })
    composerField.value?.setAttribute('aria-activedescendant', 'mention-option-' + mentionActive.value)
    node?.querySelectorAll('.mention-row')[mentionActive.value]?.scrollIntoView({ block: 'nearest' })
  } else if (event.key === 'Enter' || event.key === 'Tab') {
    event.preventDefault()
    selectMention(mentionActive.value)
  } else if (event.key === 'Escape') {
    closeMention()
  }
}

function selectMention(index: number) {
  const textarea = composerField.value
  const entry = mentionTargets.value[index]
  if (!textarea || !room.value || !entry) {
    closeMention()
    return
  }
  const insert = '@' + entry.insert
  const after = textarea.value.slice(textarea.selectionStart ?? 0)
  const beforeKeep = textarea.value.slice(0, tokenStart)
  const start = (beforeKeep + insert).length
  const next = beforeKeep + insert + ' ' + after
  draft.value = next
  const caret = start + 1
  recordFacet(room.value.key, next, tokenStart, entry, insert)
  closeMention()
  void textarea
  requestAnimationFrame(() => {
    composerField.value?.focus()
    composerField.value?.setSelectionRange(caret, caret)
  })
}

function closeMention() {
  lookupVersion++
  mentionOpen.value = false
  mentionActive.value = 0
  mentionTargets.value = []
  composerField.value?.setAttribute('aria-expanded', 'false')
  composerField.value?.removeAttribute('aria-activedescendant')
}

function onMentionBlur() {
  setTimeout(() => {
    if (mentionOpen.value && document.activeElement?.closest?.('#mention-popup') == null) closeMention()
  }, 120)
}

const canWrite = computed(() => room.value?.canWrite ?? false)


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
  if (room.value && message.authorParticipantId !== (site.site?.participant?.participantRef ?? '')) {
    const inserted = replyMention(room.value.key, draft.value, message.authorParticipantId, authorHandle(message))
    if (inserted) {
      draft.value = inserted.text
      requestAnimationFrame(() => composerField.value?.setSelectionRange(inserted.caret, inserted.caret))
    }
  }
}

function authorHandle(message: Message): string {
  const entry = resolved.value[message.authorParticipantId]
  return entry?.handle ?? ''
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
      <p
        id="conversation-connection"
        class="connection-state"
        role="status"
        :data-state="activity.connection.state"
        :title="activity.connection.title"
      >{{ activity.connection.state === 'connecting' ? 'Connecting…' : activity.connection.label }}</p>
      <div class="conversation-toolbar-actions">
        <details id="conversation-elsewhere" class="conversation-elsewhere" :hidden="!activity.elsewhere.value.length">
          <summary id="elsewhere-label">Elsewhere · {{ activity.elsewhere.value.length }} {{ activity.elsewhere.value.length === 1 ? 'topic' : 'topics' }}</summary>
          <nav id="elsewhere-links" aria-label="Updates in other topics">
            <a
              v-for="entry in activity.elsewhere.value.slice(0, 3)"
              :key="entry.topic.key"
              :href="'/t/' + encodeURIComponent(entry.tangent.key) + '/topics/' + encodeURIComponent(entry.topic.key)"
            >{{ entry.tangent.name || entry.tangent.key }} / {{ entry.topic.title || entry.topic.key }} · {{ entry.activity.directReplies > 0 ? 'reply to you' : (entry.activity.unreadCountCapped ? '50+' : entry.activity.unreadCount) + ' new' }}</a>
            <a v-if="activity.elsewhere.value.length > 3" href="/tangents/">See all Tangents →</a>
          </nav>
        </details>
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
        <p v-else class="message-text">
          <template v-if="message.removed">[removed]</template>
          <template v-else>
            <template v-for="(segment, index) in facetedSegments(message.content.text, message.facets, resolved)" :key="index">
              <a v-if="segment.kind === 'mention'" class="mention" :href="segment.href" :title="segment.title">{{ segment.text }}</a>
              <span v-else-if="segment.kind === 'group'" class="mention-group" :title="segment.title">{{ segment.text }}</span>
              <span v-else-if="segment.kind === 'tag'" class="mention-tag">{{ segment.text }}</span>
              <template v-else>{{ segment.text }}</template>
            </template>
          </template>
        </p>
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
      <textarea
        id="message-text"
        ref="composerField"
        v-model="composerText"
        class="input"
        rows="4"
        required
        placeholder="Add to the conversation…"
        role="combobox"
        aria-autocomplete="list"
        :aria-expanded="mentionOpen"
        aria-controls="mention-popup"
        @input="onComposerInput"
        @keydown="onComposerKeydown"
        @blur="onMentionBlur"
      ></textarea>
      <div id="mention-popup" class="mention-popup" role="listbox" aria-label="Mention someone" :hidden="!mentionOpen">
        <div
          v-for="(entry, index) in mentionTargets"
          :id="'mention-option-' + index"
          :key="entry.insert + index"
          class="mention-row"
          :class="{ active: index === mentionActive }"
          role="option"
          :aria-selected="index === mentionActive"
          @mousedown.prevent
          @click="selectMention(index)"
        >
          <span class="mention-label">@{{ entry.label }}</span>
          <span class="mention-meta">{{ entry.kind === 'group' ? (entry.count != null ? entry.count + ' member' + (entry.count === 1 ? '' : 's') : 'role group') : [entry.badge, entry.classification].filter(Boolean).join(' · ') || 'participant' }}</span>
        </div>
      </div>
      <div class="compose-footer">
        <span class="hint">{{ utf8ByteLength(draft) }} / 4096 bytes</span>
        <button class="btn btn-primary" type="submit">Send post</button>
      </div>
    </form>
  </div>
</template>
