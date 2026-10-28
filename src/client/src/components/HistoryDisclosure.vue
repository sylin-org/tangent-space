<script setup lang="ts">
import { httpRequest } from '../api'

// The edit-history viewer is a closed disclosure under an
// edited post. Reads the post's recorded pre-edit snapshots and renders each era
// verbatim — the server's text, facets and timestamps; nothing is reconstructed.
import { ref, watch } from 'vue'
import { useSite } from '../stores/site'
import PostContent from './PostContent.vue'
import type { Conversation, Message, PostFormat } from '../composables/conversation'

const MAX_ERAS = 20

const props = defineProps<{ conversation: Conversation; message: Message }>()

const { resolved } = props.conversation
const site = useSite()
const open = ref(false)
const loaded = ref(false)
const loading = ref(false)
const loadError = ref('')
const restricted = ref(false)
const eras = ref<HistoryRow[]>([])
const truncated = ref(false)
let generation = 0

interface HistoryRow {
  id: string
  previousChangeId?: string
  authorParticipantId?: string
  editedAt?: string
  acceptedAt?: string
  content?: { text?: string; format?: PostFormat; html?: string | null }
  facets?: unknown
}

function authorLabel(did?: string): string {
  if (!did) return ''
  if (did === (site.site?.participant?.participantRef ?? '')) return 'You'
  const handle = resolved.value[did]?.handle
  return resolved.value[did]?.displayName || (handle ? '@' + handle.replace(/^@/, '') : 'Participant')
}

function eraStamp(row: HistoryRow): string {
  if (row.editedAt) return 'Edited ' + new Date(row.editedAt).toLocaleString()
  return row.acceptedAt ? 'Posted ' + new Date(row.acceptedAt).toLocaleString() : 'Posted earlier'
}

async function load(): Promise<void> {
  const token = ++generation
  const postId = props.message.id
  let next = props.message.changeId || ''
  loading.value = true
  loadError.value = ''
  restricted.value = false
  try {
    const rows: HistoryRow[] = [], seen = new Set<string>()
    do {
      const response = await httpRequest('/api/history/messages' + (next ? '/' + encodeURIComponent(next) : '')
        + '?set=changelog&size=1&postId=' + encodeURIComponent(postId), {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' },
      })
      if (!response.ok) throw Object.assign(new Error('History is unavailable.'), { status: response.status })
      const value = await response.json()
      if (token !== generation) return
      const row = (next ? value : Array.isArray(value) ? value[0] : null) as HistoryRow | null
      if (!row) { next = ''; break }
      if (typeof row.id !== 'string' || seen.has(row.id)) throw new Error('History could not be read.')
      seen.add(row.id); rows.push(row)
      next = row.previousChangeId || ''
    } while (next && rows.length < MAX_ERAS)
    eras.value = rows
    truncated.value = !!next
  } catch (error) {
    if (token !== generation) return
    restricted.value = [401, 403].includes((error as Error & { status?: number }).status ?? 0)
    loadError.value = restricted.value
      ? 'Earlier versions aren’t available to this account.'
      : 'Earlier versions couldn’t be loaded. Your post is still here.'
  } finally {
    if (token === generation) loading.value = false
  }
}

watch(() => [props.message.id, props.message.changeId, site.site?.participant?.participantRef], () => {
  generation++; loaded.value = false; loading.value = false; eras.value = []; truncated.value = false; loadError.value = ''; restricted.value = false
  if (open.value) { loaded.value = true; void load() }
})

function onToggle(event: Event) {
  open.value = (event.currentTarget as HTMLDetailsElement).open
  // Lazy: the scoped fetch runs on first open only.
  if (!open.value || loaded.value) return
  loaded.value = true
  void load()
}
</script>

<template>
  <details class="source-details history-details" @toggle="onToggle">
    <summary :title="message.editedAt ? 'Last edited ' + new Date(message.editedAt).toLocaleString() : undefined" >Edited · history</summary>
    <div class="history-body" aria-live="polite">
      <p v-if="loading" class="hint">Loading earlier versions…</p>
      <template v-else-if="loadError">
        <p class="hint">{{ loadError }}</p>
        <button v-if="!restricted" class="btn btn-quiet" type="button" @click="load">Try again</button>
      </template>
      <template v-else-if="!eras.length">
        <p class="hint">No earlier versions are available to you.</p>
      </template>
      <template v-else>
        <div v-for="row in eras" :key="row.id" class="history-era">
          <div class="message-byline">
            <strong class="message-author">{{ authorLabel(row.authorParticipantId) }}</strong>
            <time>{{ eraStamp(row) }}</time>
          </div>
          <PostContent class="message-text" :text="row.content?.text ?? ''" :format="row.content?.format" :html="row.content?.html" :facets="row.facets" :resolved="resolved" />
        </div>
        <p v-if="truncated" class="hint">Showing the most recent available versions.</p>
      </template>
    </div>
  </details>
</template>
